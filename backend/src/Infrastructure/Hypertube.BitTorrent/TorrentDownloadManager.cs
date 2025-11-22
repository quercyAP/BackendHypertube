using System.Text;
using Hypertube.BitTorrent.FileIO;
using Hypertube.BitTorrent.Models;
using Hypertube.BitTorrent.PeerWire;
using Hypertube.BitTorrent.PieceManagement;
using Hypertube.BitTorrent.Tracker;

namespace Hypertube.BitTorrent;

/// <summary>
/// Orchestrateur principal du téléchargement torrent avec support multi-peer et retry automatique.
/// Optimisé pour le streaming vidéo avec téléchargement séquentiel.
/// </summary>
public class TorrentDownloadManager : IDisposable
{
    // Configuration et données du torrent
    private readonly TorrentFile _torrent;
    private readonly string _downloadPath;

    // Gestion des peers (max 10 connexions simultanées)
    private readonly List<PeerConnection> _allPeers = new();

    // Backoff exponentiel pour les peers avec erreurs temporaires
    private class PeerCooldown
    {
        public PeerConnection Peer { get; set; }
        public int FailureCount { get; set; }
        public DateTime CooldownUntil { get; set; }
        public TimeSpan CurrentBackoff { get; set; }
    }

    private readonly Dictionary<PeerConnection, PeerCooldown> _peerCooldowns = new();
    private readonly HashSet<PeerConnection> _permanentBlacklist = new();  // Uniquement pour erreurs fatales

    private const int PERMANENT_BLACKLIST_THRESHOLD = 10;  // Liste noire permanente après 10 échecs
    private static readonly TimeSpan INITIAL_BACKOFF = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MAX_BACKOFF = TimeSpan.FromMinutes(5);
    private const double BACKOFF_MULTIPLIER = 3.0;

    // Composants de gestion des pièces et fichiers
    private PieceBitfield? _bitfield;
    private PieceVerifier? _verifier;
    private TorrentFileWriter? _writer;

    // Statistiques simples pour le suivi de vitesse
    private long _bytesDownloaded = 0;
    private long _bytesUploaded = 0;  // SUPPORT UPLOAD: Suivi des bytes envoyés aux peers
    private DateTime _startTime;
    private DateTime _lastSuccessfulPiece = DateTime.UtcNow;  // Suivi de la dernière pièce réussie pour la logique de ré-annonce

    // STATISTIQUES TRACKER: Suivi de la première annonce pour déterminer le type d'événement
    private bool _firstAnnounce = true;

    // LOGIQUE UNCHOKE: Suivi de l'unchoke optimiste (toutes les 30 secondes)
    private DateTime _lastOptimisticUnchoke = DateTime.UtcNow;
    private PeerConnection? _optimisticPeer = null;

    // ITÉRATION 2: Un thread de téléchargement dédié par peer (au lieu de 5 threads partagés)
    // Chaque PeerConnection a sa propre Task qui télécharge en boucle
    private readonly Dictionary<PeerConnection, Task> _activePeerTasks = new();
    private readonly object _peerTasksLock = new object();

    // État du téléchargement
    private bool _disposed = false;

    /// <summary>
    /// Progression du téléchargement (0-100%)
    /// </summary>
    public double Progress => _bitfield?.Progress ?? 0.0;

    /// <summary>
    /// Vitesse de téléchargement en octets/sec
    /// </summary>
    public long DownloadSpeed => CalculateDownloadSpeed();

    /// <summary>
    /// Vitesse d'envoi en octets/sec (SUPPORT UPLOAD)
    /// </summary>
    public long UploadSpeed => CalculateUploadSpeed();

    /// <summary>
    /// Total des octets envoyés aux peers (SUPPORT UPLOAD)
    /// </summary>
    public long BytesUploaded => Interlocked.Read(ref _bytesUploaded);

    /// <summary>
    /// Indique si le téléchargement est terminé
    /// </summary>
    public bool IsComplete => _bitfield?.IsComplete ?? false;

    /// <summary>
    /// Constructeur du TorrentDownloadManager
    /// </summary>
    /// <param name="torrent">Fichier torrent parsé</param>
    /// <param name="downloadPath">Chemin de sauvegarde du fichier téléchargé</param>
    /// <exception cref="ArgumentNullException">Si torrent ou downloadPath est null</exception>
    /// <exception cref="ArgumentException">Si downloadPath est vide</exception>
    public TorrentDownloadManager(TorrentFile torrent, string downloadPath)
    {
        _torrent = torrent ?? throw new ArgumentNullException(nameof(torrent));

        if (string.IsNullOrWhiteSpace(downloadPath))
            throw new ArgumentException("Download path cannot be empty", nameof(downloadPath));

        _downloadPath = downloadPath;
    }

    /// <summary>
    /// Calcule la vitesse de téléchargement actuelle
    /// </summary>
    /// <returns>Vitesse en octets/sec, 0 si moins d'une seconde écoulée</returns>
    private long CalculateDownloadSpeed()
    {
        var elapsed = DateTime.UtcNow - _startTime;
        if (elapsed.TotalSeconds < 1)
            return 0;

        return (long)(_bytesDownloaded / elapsed.TotalSeconds);
    }

    /// <summary>
    /// SUPPORT UPLOAD: Calcule la vitesse d'envoi en octets/sec
    /// </summary>
    private long CalculateUploadSpeed()
    {
        var elapsed = DateTime.UtcNow - _startTime;
        if (elapsed.TotalSeconds < 1)
            return 0;

        return (long)(BytesUploaded / elapsed.TotalSeconds);
    }

    /// <summary>
    /// STATISTIQUES TRACKER: Calcule les octets restants à télécharger
    /// Utilisé pour le paramètre "left" de l'annonce au tracker
    /// </summary>
    private long CalculateBytesLeft()
    {
        if (_bitfield == null || _torrent == null)
            return _torrent?.Info.TotalLength ?? 0;

        // Calculer les octets complétés (pièces terminées * taille de pièce)
        long completedBytes = (long)_bitfield.CompletedPieces * _torrent.Info.PieceLength;

        // Gérer la dernière pièce (peut être plus petite que la taille normale)
        if (_bitfield.CompletedPieces > 0 && _bitfield.CompletedPieces == _torrent.Info.PieceCount)
        {
            // Toutes les pièces terminées, recalculer selon la taille totale
            completedBytes = _torrent.Info.TotalLength;
        }

        long totalBytes = _torrent.Info.TotalLength;
        return Math.Max(0, totalBytes - completedBytes);
    }

    /// <summary>
    /// STATISTIQUES TRACKER: Détermine le type d'événement pour l'annonce au tracker
    /// Retourne: "started" (première annonce), "completed" (téléchargement terminé), ou "" (annonce régulière)
    /// </summary>
    private string DetermineEventType()
    {
        if (_bitfield?.IsComplete == true)
            return "completed";

        if (_firstAnnounce)
        {
            _firstAnnounce = false;
            return "started";
        }

        return "";  // Annonce régulière (pas d'événement)
    }

    /// <summary>
    /// Contacte les trackers (avec fallback announce-list) pour obtenir la liste des peers disponibles
    /// </summary>
    /// <param name="ct">Token d'annulation</param>
    /// <returns>Liste des peers retournés par les trackers</returns>
    private async Task<List<PeerInfo>> ContactTrackerAsync(CancellationToken ct)
    {
        Console.WriteLine($"[ContactTracker] DÉBUT");
        var peers = new List<PeerInfo>();

        // Essayer tous les trackers avec fallback automatique (announce-list)
        Console.WriteLine($"[ContactTracker] Appel de TryTrackerWithFallbackAsync()...");
        var trackerPeers = await TryTrackerWithFallbackAsync(ct);
        Console.WriteLine($"[ContactTracker] TryTrackerWithFallbackAsync() a retourné {trackerPeers.Count} peers");
        peers.AddRange(trackerPeers);
        Console.WriteLine($"Les trackers ont retourné {trackerPeers.Count} peers");

        Console.WriteLine($"[ContactTracker] Déduplication des peers...");
        // Dédupliquer les peers (même IP:Port)
        peers = peers.DistinctBy(p => $"{p.IP}:{p.Port}").ToList();

        Console.WriteLine($"Total peers uniques: {peers.Count}");

        if (peers.Count == 0)
        {
            Console.WriteLine($"[ContactTracker] Aucun peer disponible, levée d'exception");
            throw new Exception("Aucun peer disponible depuis les trackers");
        }

        Console.WriteLine($"[ContactTracker] FIN - Retour de {peers.Count} peers");
        return peers;
    }

    /// <summary>
    /// Construit la liste complète des tiers de trackers (Announce + AnnounceList)
    /// </summary>
    /// <returns>Liste de tiers, chaque tier contenant une liste d'URLs</returns>
    private List<List<string>> BuildTrackerTiers()
    {
        var tiers = new List<List<string>>();

        // Tier 0: Annonce principale (si présente)
        if (!string.IsNullOrWhiteSpace(_torrent.Announce))
        {
            tiers.Add(new List<string> { _torrent.Announce });
        }

        // Tiers suivants: AnnounceList (si présente)
        if (_torrent.AnnounceList != null && _torrent.AnnounceList.Count > 0)
        {
            tiers.AddRange(_torrent.AnnounceList);
        }

        return tiers;
    }

    /// <summary>
    /// Essaie de contacter un tracker spécifique
    /// </summary>
    /// <param name="trackerUrl">URL du tracker</param>
    /// <param name="tierIndex">Index du tier (pour les logs)</param>
    /// <param name="ct">Token d'annulation</param>
    /// <returns>Liste des peers retournés par ce tracker</returns>
    private async Task<List<PeerInfo>> TryTrackerAsync(
        string trackerUrl,
        int tierIndex,
        CancellationToken ct)
    {
        try
        {
            // Vérifier que le protocole est supporté
            if (!TrackerClientFactory.IsSupported(trackerUrl))
            {
                Console.WriteLine($"[TryTracker] Tier {tierIndex}: Protocole non supporté: {trackerUrl}");
                return new List<PeerInfo>();
            }

            Console.WriteLine($"[TryTracker] Tier {tierIndex}: Tentative de connexion à {trackerUrl}");

            // CONTOURNEMENT: UdpTrackerClient.AnnounceAsync() utilise torrent.Announce au lieu du paramètre
            // On doit donc temporairement modifier _torrent.Announce
            var originalAnnounce = _torrent.Announce;
            _torrent.Announce = trackerUrl;

            try
            {
                var client = TrackerClientFactory.CreateClient(trackerUrl);
                var response = await client.AnnounceAsync(
                    _torrent,
                    6881,                           // Port d'écoute
                    BytesUploaded,                  // uploaded (STATISTIQUES TRACKER)
                    _bytesDownloaded,               // downloaded (STATISTIQUES TRACKER)
                    CalculateBytesLeft(),           // left (STATISTIQUES TRACKER)
                    DetermineEventType()            // type d'événement (STATISTIQUES TRACKER: started/completed/vide)
                );

                if (response.IsSuccess)
                {
                    Console.WriteLine($"[TryTracker] Tier {tierIndex}: SUCCÈS {trackerUrl} - {response.Peers.Count} peers");
                    return response.Peers;
                }
                else
                {
                    Console.WriteLine($"[TryTracker] Tier {tierIndex}: ÉCHEC {trackerUrl} - {response.FailureReason}");
                    return new List<PeerInfo>();
                }
            }
            finally
            {
                // Restaurer l'annonce originale
                _torrent.Announce = originalAnnounce;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TryTracker] Tier {tierIndex}: EXCEPTION {trackerUrl} - {ex.Message}");
            return new List<PeerInfo>();
        }
    }

    /// <summary>
    /// Essaie tous les trackers d'un tier en parallèle
    /// </summary>
    /// <param name="trackerUrls">Liste des URLs de trackers du tier</param>
    /// <param name="tierIndex">Index du tier (pour les logs)</param>
    /// <param name="ct">Token d'annulation</param>
    /// <returns>Liste des peers obtenus de tous les trackers du tier</returns>
    private async Task<List<PeerInfo>> TryTrackerTierAsync(
        List<string> trackerUrls,
        int tierIndex,
        CancellationToken ct)
    {
        var allPeers = new List<PeerInfo>();
        var tasks = new List<Task<List<PeerInfo>>>();

        // Lancer toutes les requêtes en parallèle
        foreach (var trackerUrl in trackerUrls)
        {
            tasks.Add(TryTrackerAsync(trackerUrl, tierIndex, ct));
        }

        // Attendre toutes les requêtes avec timeout de 10 secondes
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Timeout ou annulation - on collecte ce qu'on a
            Console.WriteLine($"[TryTrackerTier] Tier {tierIndex}: Délai dépassé");
        }

        // Collecter tous les peers des trackers qui ont réussi
        foreach (var task in tasks)
        {
            if (task.IsCompletedSuccessfully)
            {
                allPeers.AddRange(task.Result);
            }
        }

        return allPeers;
    }

    /// <summary>
    /// Essaie de contacter les trackers avec fallback automatique sur announce-list
    /// Stratégie: Essayer tous les trackers d'un tier en parallèle, passer au tier suivant si échec
    /// </summary>
    /// <param name="ct">Token d'annulation</param>
    /// <returns>Liste des peers obtenus</returns>
    private async Task<List<PeerInfo>> TryTrackerWithFallbackAsync(CancellationToken ct)
    {
        var allPeers = new List<PeerInfo>();

        // Construire la liste complète des tiers
        var tiers = BuildTrackerTiers();

        Console.WriteLine($"[TryTrackerFallback] {tiers.Count} tiers disponibles");

        // Essayer chaque tier séquentiellement
        for (int tierIndex = 0; tierIndex < tiers.Count; tierIndex++)
        {
            var tier = tiers[tierIndex];
            Console.WriteLine($"[TryTrackerFallback] Tier {tierIndex}: {tier.Count} tracker(s)");

            var tierPeers = await TryTrackerTierAsync(tier, tierIndex, ct);

            if (tierPeers.Count > 0)
            {
                Console.WriteLine($"[TryTrackerFallback] Tier {tierIndex} SUCCÈS: {tierPeers.Count} peers");
                allPeers.AddRange(tierPeers);
                // SUCCÈS: On a des peers, pas besoin d'essayer les tiers suivants
                break;
            }

            Console.WriteLine($"[TryTrackerFallback] Tier {tierIndex} ÉCHEC: Essai du tier suivant...");
        }

        return allPeers;
    }

    /// <summary>
    /// Se connecte à plusieurs peers en parallèle (max 150)
    /// Étant donné un taux d'échec TCP d'environ 90%, on doit essayer beaucoup de peers pour obtenir 5-10 connexions réussies
    /// </summary>
    /// <param name="peers">Liste des peers retournés par le tracker</param>
    /// <param name="ct">Token d'annulation</param>
    private async Task ConnectToPeersAsync(List<PeerInfo> peers, CancellationToken ct)
    {
        const int MAX_PEERS = 150;  // OPTIMISATION: Augmenté de 50 → 150 pour plus de peers
        var tasks = new List<Task>();

        Console.WriteLine($"[ConnectToPeersAsync] Tentative de connexion à {Math.Min(MAX_PEERS, peers.Count)} peers sur {peers.Count} disponibles");

        foreach (var peerInfo in peers.Take(MAX_PEERS))
        {
            tasks.Add(Task.Run(async () =>
            {
                PeerConnection? peer = null;
                var peerAddress = $"{peerInfo.IP}:{peerInfo.Port}";
                try
                {
                    // Console.WriteLine($"[PEER {peerAddress}] Step 1/5: Starting TCP connection...");  // TOO VERBOSE
                    peer = new PeerConnection(peerInfo, _torrent.InfoHash, GeneratePeerId());
                    await peer.ConnectAsync(timeoutMs: 5000);  // OPTIMISATION: Réduit de 10s → 5s
                    // Console.WriteLine($"[PEER {peerAddress}] Step 1/5: ✓ TCP connected");  // TOO VERBOSE

                    // Console.WriteLine($"[PEER {peerAddress}] Step 2/5: Sending handshake...");  // TOO VERBOSE
                    await peer.SendHandshakeAsync();
                    // Console.WriteLine($"[PEER {peerAddress}] Step 2/5: ✓ Handshake sent");  // TOO VERBOSE

                    // Console.WriteLine($"[PEER {peerAddress}] Step 3/5: Waiting for handshake response (timeout: 5s)...");  // TOO VERBOSE
                    if (await peer.ReceiveHandshakeAsync(timeoutMs: 5000))  // OPTIMISATION: Réduit de 10s → 5s
                    {
                        // Console.WriteLine($"[PEER {peerAddress}] Step 3/5: ✓ Handshake received and validated");  // TOO VERBOSE

                        // Console.WriteLine($"[PEER {peerAddress}] Step 4/5: Sending interested message...");  // TOO VERBOSE
                        await peer.SendInterestedAsync();
                        // Console.WriteLine($"[PEER {peerAddress}] Step 4/5: ✓ Interested sent");  // TOO VERBOSE

                        // Console.WriteLine($"[PEER {peerAddress}] Step 5/5: Waiting for messages (bitfield, unchoke, etc.)...");  // TOO VERBOSE
                        // Attendre les messages initiaux (bitfield, unchoke, etc.)
                        // OPTIMISATION: Continue la lecture pendant max 5 secondes (réduit de 10s)
                        using var cts = new CancellationTokenSource(5000);
                        int messageCount = 0;
                        bool hasBitfield = false;
                        bool hasUnchoke = false;
                        try
                        {
                            while (!cts.Token.IsCancellationRequested)
                            {
                                var msg = await peer.ReceiveMessageAsync(timeoutMs: 3000);
                                if (msg != null)
                                {
                                    messageCount++;
                                    peer.ProcessMessage(msg);

                                    // Suivre ce qu'on a reçu
                                    if (peer.PeerBitfield != null && !hasBitfield)
                                    {
                                        hasBitfield = true;
                                    }
                                    if (!peer.PeerChoking && !hasUnchoke)
                                    {
                                        hasUnchoke = true;
                                    }

                                    // Arrêter si on a DEUX bitfield ET unchoke
                                    if (hasBitfield && hasUnchoke)
                                    {
                                        break;
                                    }
                                }
                                else
                                {
                                    break; // Timeout ou connexion fermée
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // Timeout - continuer quand même si on a le bitfield
                        }

                        // Ajouter le peer seulement s'il a un bitfield (sait quelles pièces il a)
                        if (peer.PeerBitfield != null)
                        {
                            lock (_allPeers)
                            {
                                _allPeers.Add(peer);
                            }
                            Console.WriteLine($"Connexion réussie au peer {peerAddress}");
                            peer = null; // Ne pas disposer, il est ajouté à _allPeers
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Exception lors de la connexion - ignorée (trop verbeux)
                }
                finally
                {
                    // Disposer le peer s'il n'est pas ajouté à _allPeers
                    if (peer != null)
                    {
                        peer.Dispose();
                    }
                }
            }, ct));
        }

        // Attendre que toutes les connexions se terminent (succès ou échec)
        await Task.WhenAll(tasks);

        var unchokedPeers = _allPeers.Count(p => !p.PeerChoking);
        Console.WriteLine($"[ConnectToPeersAsync] Tentatives terminées: {_allPeers.Count}/{MAX_PEERS} peers connectés avec succès");
        Console.WriteLine($"[ConnectToPeersAsync] Peers prêts: {unchokedPeers}/{_allPeers.Count} peers sont unchoked et prêts à télécharger");
    }

    /// <summary>
    /// Génère un peer ID unique pour identifier notre client
    /// Format: "-HY0001-" + 12 caractères aléatoires
    /// </summary>
    /// <returns>Peer ID de 20 octets</returns>
    private byte[] GeneratePeerId()
    {
        return Encoding.ASCII.GetBytes("-HY0001-" + Guid.NewGuid().ToString().Substring(0, 12));
    }

    /// <summary>
    /// Démarre le téléchargement du torrent
    /// </summary>
    /// <param name="ct">Token d'annulation</param>
    public async Task StartDownloadAsync(CancellationToken ct = default)
    {
        Console.WriteLine($"[TDM] StartDownloadAsync() - DÉBUT");

        // Initialisation des composants
        Console.WriteLine($"[TDM] Création du PieceBitfield (_torrent.Info.PieceCount={_torrent.Info.PieceCount})");
        _bitfield = new PieceBitfield(_torrent.Info.PieceCount);

        Console.WriteLine($"[TDM] Création du PieceVerifier");
        _verifier = new PieceVerifier(_torrent.Info.Pieces);

        Console.WriteLine($"[TDM] Création du TorrentFileWriter (chemin={_downloadPath})");
        _writer = new TorrentFileWriter(_downloadPath, _torrent.Info);
        _startTime = DateTime.UtcNow;

        Console.WriteLine($"[TDM] Composants créés avec succès");

        // Obtenir les peers depuis le tracker
        Console.WriteLine($"[TDM] Appel de ContactTrackerAsync()");
        var peers = await ContactTrackerAsync(ct);
        Console.WriteLine($"[TDM] ContactTrackerAsync() a retourné {peers.Count} peers");

        Console.WriteLine($"[TDM] Appel de ConnectToPeersAsync()");
        await ConnectToPeersAsync(peers, ct);
        Console.WriteLine($"[TDM] ConnectToPeersAsync() terminé, _allPeers.Count={_allPeers.Count}");

        // LOGIQUE UNCHOKE: Démarrer la tâche de fond de l'algorithme unchoke (s'exécute toutes les 10s)
        Console.WriteLine($"[TDM] Démarrage de la tâche de fond de l'algorithme unchoke");
        var unchokeTask = Task.Run(async () => await RunUnchokeAlgorithmAsync(ct), ct);

        if (_allPeers.Count == 0)
        {
            Console.WriteLine($"[TDM] ERREUR: Aucun peer disponible, levée d'exception");
            throw new Exception("Aucun peer disponible");
        }

        // Télécharger avec parallélisme
        Console.WriteLine($"[TDM] Appel de DownloadWithParallelismAsync()");
        await DownloadWithParallelismAsync(ct);

        // Diagnostic: Vérifier si le téléchargement est incomplet
        if (!_bitfield!.IsComplete)
        {
            Console.WriteLine($"[TDM] ⚠️ Téléchargement incomplet! Analyse des pièces manquantes...");
            var missing = _bitfield.GetMissingPieces().ToList();
            Console.WriteLine($"[TDM] Pièces manquantes: {missing.Count} → [{string.Join(", ", missing)}]");

            foreach (var pieceIndex in missing)
            {
                bool claimed = _bitfield.IsPieceClaimed(pieceIndex);
                Console.WriteLine($"[TDM]   Pièce {pieceIndex}: réclamée={claimed}");
            }
        }
        else
        {
            // STATISTIQUES TRACKER: Envoyer l'événement "completed" au tracker
            Console.WriteLine($"[TDM] Téléchargement 100% terminé! Annonce au tracker...");
            try
            {
                await ContactTrackerAsync(ct);
                Console.WriteLine($"[TDM] Annonce de complétion au tracker réussie");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TDM] Avertissement: Échec de l'annonce de complétion au tracker: {ex.Message}");
                // Ne pas lever d'exception - l'échec d'annonce ne doit pas bloquer le seeding
            }
        }

        Console.WriteLine($"[TDM] Téléchargement terminé!");
    }

    /// <summary>
    /// Vérifie s'il reste des pièces non réclamées et incomplètes disponibles au téléchargement
    /// Utilisé pour éviter une boucle infinie quand toutes les pièces restantes sont déjà réclamées par des peers actifs
    /// </summary>
    private bool HasUnclaimedPieces()
    {
        for (int i = 0; i < _bitfield!.TotalPieces; i++)
        {
            if (!_bitfield.HasPiece(i) && !_bitfield.IsPieceClaimed(i))
                return true;
        }
        return false;
    }

    /// <summary>
    /// ITÉRATION 2: Boucle de téléchargement avec un thread dédié par peer
    /// Au lieu de 5 threads qui se partagent tous les peers, chaque peer a SON propre thread
    /// Cela élimine complètement les conflits de concurrence sur NetworkStream
    /// </summary>
    /// <param name="ct">Token d'annulation</param>
    private async Task DownloadWithParallelismAsync(CancellationToken ct)
    {
        const int MAX_ACTIVE_PEERS = 30; // OPTIMISATION: Augmenté de 5 → 30 pour une vitesse 6x supérieure
        bool shouldReannounce = false;

        while (!_bitfield!.IsComplete && !ct.IsCancellationRequested)
        {
            lock (_peerTasksLock)
            {
                // Nettoyer les tasks terminées
                var completedPeers = _activePeerTasks.Where(kvp => kvp.Value.IsCompleted).Select(kvp => kvp.Key).ToList();
                foreach (var peer in completedPeers)
                {
                    _activePeerTasks.Remove(peer);
                }

                // Trouver les peers qui n'ont pas encore de task active ET qui ne sont pas en cooldown
                var idlePeers = _allPeers.Where(p =>
                    !_activePeerTasks.ContainsKey(p) &&
                    p.PeerBitfield != null &&
                    !_permanentBlacklist.Contains(p) &&
                    (!_peerCooldowns.TryGetValue(p, out var cd) || cd.CooldownUntil <= DateTime.UtcNow)
                ).ToList();

                // Lancer des tasks pour les peers inactifs (jusqu'à MAX_ACTIVE_PEERS au total)
                // Ne relancer les peers que s'il reste des pièces non réclamées (évite une boucle infinie à la fin)
                if (HasUnclaimedPieces())
                {
                    while (_activePeerTasks.Count < MAX_ACTIVE_PEERS && idlePeers.Count > 0)
                    {
                        var peer = idlePeers[0];
                        idlePeers.RemoveAt(0);

                        // Chaque peer a SON propre thread qui télécharge EN BOUCLE
                        var downloadTask = Task.Run(async () => await DownloadFromPeerAsync(peer, ct), ct);
                        _activePeerTasks[peer] = downloadTask;

                        // SUPPORT UPLOAD: Démarrer le gestionnaire d'upload pour ce peer (s'exécute en parallèle du téléchargement)
                        var uploadTask = Task.Run(async () => await ProcessUploadRequestsAsync(peer, ct), ct);
                        // Note: La task upload est fire-and-forget, non suivie dans _activePeerTasks
                    }
                }

                // Détecter si on est bloqué (pas de pièces inactives ET aucun succès depuis 30s)
                if (idlePeers.Count == 0 && !_bitfield.IsComplete)
                {
                    var timeSinceLastSuccess = DateTime.UtcNow - _lastSuccessfulPiece;
                    if (timeSinceLastSuccess.TotalSeconds > 30)
                    {
                        shouldReannounce = true;
                    }
                }
            }

            // Ré-annoncer au tracker si nécessaire (en dehors du lock)
            if (shouldReannounce)
            {
                shouldReannounce = false;  // Réinitialiser le flag
                Console.WriteLine($"[TDM] ⚠️ Bloqué! Aucune pièce réussie depuis 30s, ré-annonce au tracker pour nouveaux peers...");

                try
                {
                    var newPeerInfos = await ContactTrackerAsync(ct);

                    // Obtenir les adresses des peers existants pour déduplication
                    HashSet<string> existingAddresses;
                    lock (_allPeers)
                    {
                        existingAddresses = _allPeers.Select(p => p.PeerAddress).ToHashSet();
                    }

                    // Filtrer les peers qu'on a déjà
                    var uniqueNewPeers = newPeerInfos
                        .Where(pi => !existingAddresses.Contains($"{pi.IP}:{pi.Port}"))
                        .ToList();

                    Console.WriteLine($"[TDM] Trouvé {uniqueNewPeers.Count} nouveaux peers uniques (filtré {newPeerInfos.Count - uniqueNewPeers.Count} doublons)");

                    var connectedPeers = new List<PeerConnection>();

                    // Essayer de se connecter à jusqu'à 50 nouveaux peers uniques
                    int attempted = 0;
                    foreach (var peerInfo in uniqueNewPeers.Take(50))
                    {
                        attempted++;
                        var newPeer = new PeerConnection(peerInfo, _torrent.InfoHash, GeneratePeerId());
                        try
                        {
                            await newPeer.ConnectAsync(timeoutMs: 5000);
                            await newPeer.SendHandshakeAsync();
                            if (await newPeer.ReceiveHandshakeAsync(timeoutMs: 5000))
                            {
                                await newPeer.SendInterestedAsync();
                                connectedPeers.Add(newPeer);
                                Console.WriteLine($"[TDM] ✅ Connecté au nouveau peer {connectedPeers.Count}/{attempted}: {peerInfo.IP}:{peerInfo.Port}");
                            }
                            else
                            {
                                newPeer.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            // Logger les premiers échecs pour le débogage
                            if (attempted <= 3)
                            {
                                Console.WriteLine($"[TDM] ❌ Échec de connexion à {peerInfo.IP}:{peerInfo.Port}: {ex.Message}");
                            }
                            newPeer.Dispose();
                        }
                    }

                    // Ajouter les peers connectés avec succès au pool
                    lock (_allPeers)
                    {
                        foreach (var peer in connectedPeers)
                        {
                            _allPeers.Add(peer);
                        }
                    }

                    Console.WriteLine($"[TDM] Ré-annonce terminée. Ajouté {connectedPeers.Count}/{attempted} nouveaux peers (pool total: {_allPeers.Count})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TDM] ⚠️ Échec de la ré-annonce: {ex.Message}");
                }
            }

            // Si aucun peer actif, attendre
            if (_activePeerTasks.Count == 0)
            {
                Console.WriteLine($"[TDM] Aucun peer actif, attente...");
                await Task.Delay(2000, ct);
                continue;
            }

            // Attendre qu'au moins une task se termine
            Task[] tasks;
            lock (_peerTasksLock)
            {
                tasks = _activePeerTasks.Values.ToArray();
            }

            if (tasks.Length > 0)
                await Task.WhenAny(tasks);
        }

        // Attendre toutes les tasks restantes
        Task[] remainingTasks;
        lock (_peerTasksLock)
        {
            remainingTasks = _activePeerTasks.Values.ToArray();
        }
        if (remainingTasks.Length > 0)
            await Task.WhenAll(remainingTasks);
    }

    /// <summary>
    /// OPTIMISATION PHASE 1: Téléchargement séquentiel de pièces avec pipelining élevé (50 requêtes en vol).
    /// CORRECTION: Réduit de 5 pièces concurrentes à 1 pour éviter le bug de corrélation de messages.
    /// Plusieurs tasks DownloadPieceAsync concurrentes partageant le même NetworkStream causaient des pertes de messages.
    /// Avec 30 peers × 1 pièce chacun + PIPELINE_SIZE=50 = Parallélisme toujours significatif
    /// Amélioration attendue: 2-3x plus rapide
    /// </summary>
    private async Task DownloadFromPeerAsync(PeerConnection peer, CancellationToken ct)
    {
        const int PIECES_PER_PEER = 1;  // CORRECTION: 1 pièce à la fois pour éviter la perte de messages (était 5)
        var selector = new SequentialPieceSelector();
        var activePieces = new Dictionary<int, Task>();  // pieceIndex → task de téléchargement
        int consecutiveFailures = 0;
        const int MAX_CONSECUTIVE_FAILURES = 3;  // Abandon rapide des peers morts (réduit de 15)

        try
        {
            while (!_bitfield!.IsComplete && !ct.IsCancellationRequested)
            {
                var peerBitfield = CreatePeerBitfield(peer);

                // PHASE 1: Remplir le pipeline avec les téléchargements de pièces concurrents
                while (activePieces.Count < PIECES_PER_PEER && !_bitfield.IsComplete)
                {
                    var nextPiece = selector.SelectNextPiece(_bitfield!, peerBitfield);

                    if (!nextPiece.HasValue)
                    {
                        // Plus de pièces disponibles depuis ce peer (toutes les pièces nécessaires sont réclamées ou terminées)
                        break;
                    }

                    // Réclamer atomiquement la pièce pour empêcher les autres peers de la télécharger
                    if (_bitfield.TryClaimPiece(nextPiece.Value))
                    {
                        activePieces[nextPiece.Value] = DownloadPieceAsync(nextPiece.Value, peer, ct);
                    }
                }

                // Si aucune pièce n'est en téléchargement, on a fini avec ce peer
                if (activePieces.Count == 0)
                {
                    break;
                }

                // PHASE 2: Attendre qu'AU MOINS UNE pièce se termine (fenêtre glissante)
                var completedTask = await Task.WhenAny(activePieces.Values);
                var completedPieceIndex = activePieces.First(kvp => kvp.Value == completedTask).Key;

                try
                {
                    // Propager les exceptions de la task de téléchargement
                    await completedTask;

                    // Succès! La pièce a été téléchargée, vérifiée et écrite
                    // La réclamation est automatiquement libérée quand SetPiece(true) est appelé dans DownloadPieceAsync
                    Console.WriteLine($"[DownloadFromPeer] ✓ Pièce {completedPieceIndex} terminée avec succès ({_bitfield.CompletedPieces}/{_bitfield.TotalPieces})");
                    consecutiveFailures = 0;  // Réinitialiser au succès
                    _lastSuccessfulPiece = DateTime.UtcNow;  // Suivi pour la logique de ré-annonce
                    HandlePeerSuccess(peer);  // Réinitialiser le backoff de cooldown
                }
                catch (Exception ex)
                {
                    // Échec du téléchargement - libérer la réclamation pour qu'un autre peer puisse essayer
                    _bitfield.ReleasePieceClaim(completedPieceIndex);

                    consecutiveFailures++;

                    // Appliquer le backoff exponentiel au lieu d'une liste noire permanente
                    lock (_peerCooldowns)
                    {
                        HandlePeerFailure(peer, ex);
                    }

                    if (consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                    {
                        Console.WriteLine($"[DownloadFromPeer] Trop d'échecs consécutifs, mise en cooldown du peer");
                        Console.WriteLine($"[DownloadFromPeer] 🔓 Libération de {activePieces.Count} pièces réclamées: [{string.Join(", ", activePieces.Keys)}]");

                        // Libérer toutes les pièces réclamées restantes
                        foreach (var pieceIndex in activePieces.Keys)
                        {
                            _bitfield.ReleasePieceClaim(pieceIndex);
                        }
                        break;
                    }

                    await Task.Delay(500, ct);  // Délai bref avant réessai
                }

                // Retirer la task terminée de l'ensemble actif
                activePieces.Remove(completedPieceIndex);
            }

            // Nettoyage: Libérer les pièces réclamées restantes si on sort prématurément
            foreach (var pieceIndex in activePieces.Keys)
            {
                if (!_bitfield.HasPiece(pieceIndex))
                {
                    _bitfield.ReleasePieceClaim(pieceIndex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[DownloadFromPeer] Task peer annulée");

            // Libérer toutes les pièces réclamées à l'annulation
            foreach (var pieceIndex in activePieces.Keys)
            {
                _bitfield?.ReleasePieceClaim(pieceIndex);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DownloadFromPeer] Task peer échouée: {ex.Message}");

            // Libérer toutes les pièces réclamées en cas d'erreur
            foreach (var pieceIndex in activePieces.Keys)
            {
                _bitfield?.ReleasePieceClaim(pieceIndex);
            }
        }
    }

    /// <summary>
    /// SUPPORT UPLOAD: Traite les requêtes d'upload d'un peer en arrière-plan
    /// Défile les requêtes de blocs et envoie des messages PIECE en réponse
    /// </summary>
    /// <param name="peer">Connexion peer pour laquelle traiter les uploads</param>
    /// <param name="ct">Token d'annulation</param>
    private async Task ProcessUploadRequestsAsync(PeerConnection peer, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && peer.IsConnected)
            {
                PeerConnection.BlockRequest? request = null;

                // Essayer de défiler une requête (thread-safe)
                lock (peer.RequestQueue)
                {
                    if (peer.RequestQueue.Count > 0)
                    {
                        request = peer.RequestQueue.Dequeue();
                    }
                }

                if (request != null)
                {
                    try
                    {
                        // Vérifier si on a cette pièce complète
                        if (!_bitfield!.HasPiece(request.PieceIndex))
                        {
                            Console.WriteLine($"[Upload] Impossible de satisfaire la requête: pièce {request.PieceIndex} incomplète");
                            continue;
                        }

                        // Lire le bloc depuis le disque via l'API RandomAccess (sans verrou)
                        byte[] blockData = await _writer!.ReadPieceBlockAsync(
                            request.PieceIndex,
                            request.Begin,
                            request.Length
                        );

                        // Envoyer le message PIECE au peer
                        await peer.SendPieceAsync(request.PieceIndex, request.Begin, blockData);

                        // Suivre les statistiques d'upload (thread-safe)
                        Interlocked.Add(ref _bytesUploaded, blockData.Length);

                        Console.WriteLine($"[Upload] Envoyé pièce {request.PieceIndex} bloc [{request.Begin}:{request.Begin + request.Length}] au peer ({blockData.Length} octets, total uploadé: {BytesUploaded} octets)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Upload] Erreur lors du traitement de la requête pour la pièce {request.PieceIndex}: {ex.Message}");
                        // Continuer à traiter les autres requêtes
                    }
                }
                else
                {
                    // Pas de requêtes dans la file, dormir brièvement pour éviter l'attente active
                    await Task.Delay(100, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[Upload] Task upload annulée pour le peer");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Upload] Task upload échouée: {ex.Message}");
        }
    }

    /// <summary>
    /// LOGIQUE UNCHOKE: Exécute l'algorithme unchoke toutes les 10 secondes
    /// Implémente la réciprocité tit-for-tat: unchoke les 4 meilleurs peers par taux de téléchargement
    /// Implémente l'unchoke optimiste toutes les 30 secondes (sélection aléatoire de peer)
    /// </summary>
    private async Task RunUnchokeAlgorithmAsync(CancellationToken ct)
    {
        try
        {
            Console.WriteLine("[Unchoke] Démarrage de la tâche de fond de l'algorithme unchoke");

            while (!ct.IsCancellationRequested)
            {
                // Attendre 10 secondes entre les tours d'unchoke
                await Task.Delay(10000, ct);

                List<PeerConnection> interestedPeers;
                lock (_allPeers)
                {
                    // Obtenir tous les peers connectés où PeerInterested == true
                    interestedPeers = _allPeers
                        .Where(p => p.IsConnected && p.PeerInterested)
                        .ToList();
                }

                if (interestedPeers.Count == 0)
                {
                    continue;
                }

                // Vérifier si c'est le moment pour l'unchoke optimiste (toutes les 30 secondes)
                var now = DateTime.UtcNow;
                bool doOptimisticUnchoke = (now - _lastOptimisticUnchoke).TotalSeconds >= 30;

                List<PeerConnection> toUnchoke;
                List<PeerConnection> toChoke;

                if (doOptimisticUnchoke)
                {
                    // UNCHOKE OPTIMISTE (toutes les 30 secondes)
                    // Sélectionner les 3 meilleurs peers par taux de téléchargement + 1 peer aléatoire
                    _lastOptimisticUnchoke = now;

                    var sortedPeers = interestedPeers
                        .OrderByDescending(p => p.GetDownloadRate())
                        .ToList();

                    // Les 3 meilleurs peers
                    var best3 = sortedPeers.Take(3).ToList();

                    // Peer aléatoire parmi les peers chokés (exclure les 3 meilleurs)
                    var chokedPeers = sortedPeers.Skip(3).Where(p => p.AmChoking).ToList();
                    PeerConnection? randomPeer = null;
                    if (chokedPeers.Count > 0)
                    {
                        var random = new Random();
                        randomPeer = chokedPeers[random.Next(chokedPeers.Count)];
                        _optimisticPeer = randomPeer;
                        Console.WriteLine($"[Unchoke] Unchoke optimiste sélectionné: {randomPeer.PeerAddress}");
                    }

                    // Unchoker les 3 meilleurs + peer aléatoire
                    toUnchoke = best3;
                    if (randomPeer != null && !toUnchoke.Contains(randomPeer))
                    {
                        toUnchoke.Add(randomPeer);
                    }

                    // Choker tous les autres
                    toChoke = interestedPeers.Except(toUnchoke).ToList();
                }
                else
                {
                    // UNCHOKE RÉGULIER (toutes les 10 secondes)
                    // Unchoker les 4 meilleurs peers par taux de téléchargement
                    var sortedPeers = interestedPeers
                        .OrderByDescending(p => p.GetDownloadRate())
                        .ToList();

                    toUnchoke = sortedPeers.Take(4).ToList();
                    toChoke = sortedPeers.Skip(4).ToList();
                }

                // Envoyer les messages UNCHOKE aux peers sélectionnés (si actuellement chokés)
                int unchokedCount = 0;
                foreach (var peer in toUnchoke)
                {
                    if (peer.AmChoking)
                    {
                        await peer.SendUnchokeAsync();
                        unchokedCount++;
                    }
                }

                // Envoyer les messages CHOKE aux autres peers (si actuellement unchokés)
                int chokedCount = 0;
                foreach (var peer in toChoke)
                {
                    if (!peer.AmChoking)
                    {
                        await peer.SendChokeAsync();
                        chokedCount++;
                    }
                }

                Console.WriteLine($"[Unchoke] Unchoké {unchokedCount} peers, choké {chokedCount} peers (total intéressés: {interestedPeers.Count})");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[Unchoke] Tâche algorithme unchoke annulée");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Unchoke] Algorithme unchoke échoué: {ex.Message}");
        }
    }

    /// <summary>
    /// Trouve le prochain peer disponible et la pièce à télécharger de ce peer
    /// NOTE: Ignore le flag PeerChoking (unchoke optimiste) - les peers vont unchoker quand on envoie des requêtes
    /// </summary>
    /// <param name="selector">Sélecteur de pièce</param>
    /// <returns>Tuple (peer, pieceIndex) ou (null, null) si aucune pièce disponible</returns>
    private (PeerConnection?, int?) FindNextPeerAndPiece(IPieceSelector selector)
    {
        lock (_allPeers)
        {
            // Essayer chaque peer pour trouver une pièce à télécharger
            foreach (var peer in _allPeers)
            {
                // IGNORER PeerChoking - essayer quand même (unchoke optimiste)
                // Beaucoup de peers vont nous unchoker quand on envoie une requête
                if (peer.PeerBitfield == null)
                    continue;

                // Créer un bitfield temporaire pour ce peer
                var peerBitfield = CreatePeerBitfield(peer);

                // Demander au sélecteur quelle pièce télécharger
                var nextPiece = selector.SelectNextPiece(_bitfield!, peerBitfield);

                if (nextPiece.HasValue)
                    return (peer, nextPiece.Value);
            }

            return (null, null);
        }
    }

    /// <summary>
    /// Crée un PieceBitfield à partir du bitfield brut d'un peer
    /// </summary>
    private PieceBitfield CreatePeerBitfield(PeerConnection peer)
    {
        var bitfield = new PieceBitfield(_torrent.Info.PieceCount);

        if (peer.PeerBitfield == null)
            return bitfield;

        for (int i = 0; i < _torrent.Info.PieceCount; i++)
        {
            if (peer.HasPiece(i))
                bitfield.SetPiece(i, true);
        }

        return bitfield;
    }

    /// <summary>
    /// Trouve un peer qui possède la pièce demandée et qui n'est pas en mode choking
    /// </summary>
    /// <param name="pieceIndex">Index de la pièce recherchée</param>
    /// <returns>PeerConnection ou null si aucun peer disponible</returns>
    private PeerConnection? FindPeerForPiece(int pieceIndex)
    {
        lock (_allPeers)
        {
            return _allPeers.FirstOrDefault(p =>
                p.HasPiece(pieceIndex) && !p.PeerChoking
            );
        }
    }

    /// <summary>
    /// Télécharge une pièce avec retry automatique (max 3 tentatives)
    /// </summary>
    /// <param name="pieceIndex">Index de la pièce à télécharger</param>
    /// <param name="ct">Token d'annulation</param>
    private async Task DownloadPieceWithRetryAsync(int pieceIndex, CancellationToken ct)
    {
        const int MAX_RETRIES = 3;
        Exception? lastException = null;

        for (int attempt = 0; attempt < MAX_RETRIES; attempt++)
        {
            var peer = FindPeerForPiece(pieceIndex);
            if (peer == null)
            {
                await Task.Delay(1000, ct);
                continue;
            }

            try
            {
                await DownloadPieceAsync(pieceIndex, peer, ct);
                return; // Succès!
            }
            catch (Exception ex)
            {
                lastException = ex;
                Console.WriteLine($"Pièce {pieceIndex} échouée (tentative {attempt + 1}): {ex.Message}");

                // Retirer le peer défaillant
                lock (_allPeers)
                {
                    _allPeers.Remove(peer);
                }
                peer.Dispose();
            }
        }

        throw new Exception($"Échec du téléchargement de la pièce {pieceIndex} après {MAX_RETRIES} tentatives", lastException);
    }

    /// <summary>
    /// Télécharge une pièce complète depuis un peer avec request pipelining (ITÉRATION 3)
    /// Envoie 50 requêtes simultanément au lieu d'attendre chaque réponse (fenêtre glissante)
    /// </summary>
    /// <param name="pieceIndex">Index de la pièce</param>
    /// <param name="peer">Peer depuis lequel télécharger</param>
    /// <param name="ct">Token d'annulation</param>
    private async Task DownloadPieceAsync(int pieceIndex, PeerConnection peer, CancellationToken ct)
    {
        const int BLOCK_SIZE = 16384;
        const int PIPELINE_SIZE = 50;  // OPTIMISATION: Augmenté de 10 → 50 pour gain de vitesse 2-3x supplémentaire

        long pieceLength = _writer!.CalculatePieceLength(pieceIndex);
        int totalBlocks = (int)Math.Ceiling((double)pieceLength / BLOCK_SIZE);

        var receivedBlocks = new Dictionary<int, byte[]>(); // begin → données
        int nextBlockToSend = 0;

        // PHASE 1: Remplir le pipeline initial (envoyer les premières requêtes)
        int initialRequests = Math.Min(PIPELINE_SIZE, totalBlocks);
        for (int i = 0; i < initialRequests; i++)
        {
            int begin = i * BLOCK_SIZE;
            int length = (int)Math.Min(BLOCK_SIZE, pieceLength - begin);
            await peer.SendRequestAsync(pieceIndex, begin, length);
            nextBlockToSend++;
        }

        // PHASE 2: Recevoir les réponses et maintenir le pipeline (fenêtre glissante)
        var globalTimeout = DateTime.UtcNow.AddSeconds(30);  // OPTIMISATION: Réduit de 60s → 30s
        while (receivedBlocks.Count < totalBlocks)
        {
            if (DateTime.UtcNow > globalTimeout)
                throw new TimeoutException($"Délai dépassé pour le téléchargement de la pièce {pieceIndex} ({receivedBlocks.Count}/{totalBlocks} blocs reçus)");

            var msg = await peer.ReceiveMessageAsync(timeoutMs: 10000);  // OPTIMISATION: Réduit de 30s → 10s

            if (msg == null)
                continue;

            // Messages non-Piece: traiter normalement (Have, KeepAlive, etc.)
            if (msg.Type != MessageType.Piece)
            {
                peer.ProcessMessage(msg);
                continue;
            }

            // Message Piece: vérifier que c'est pour cette pièce
            var (msgPieceIndex, msgBegin) = ParsePieceMetadata(msg);

            if (msgPieceIndex != pieceIndex)
                continue;  // Ignorer Piece pour une autre pièce

            // Stocker le bloc reçu (éviter les doublons)
            if (!receivedBlocks.ContainsKey(msgBegin))
            {
                receivedBlocks[msgBegin] = ExtractBlockData(msg);
                Interlocked.Add(ref _bytesDownloaded, receivedBlocks[msgBegin].Length);

                // LOGIQUE UNCHOKE: Suivre les octets reçus de ce peer pour le calcul du taux de téléchargement
                peer.AddBytesReceived(receivedBlocks[msgBegin].Length);

                // FENÊTRE GLISSANTE: Envoyer immédiatement la prochaine requête
                if (nextBlockToSend < totalBlocks)
                {
                    int begin = nextBlockToSend * BLOCK_SIZE;
                    int length = (int)Math.Min(BLOCK_SIZE, pieceLength - begin);
                    await peer.SendRequestAsync(pieceIndex, begin, length);
                    nextBlockToSend++;
                }
            }
        }

        // PHASE 3: Assembler la pièce dans le bon ordre
        var pieceData = new byte[pieceLength];
        int offset = 0;
        for (int begin = 0; begin < pieceLength; begin += BLOCK_SIZE)
        {
            var block = receivedBlocks[begin];
            Array.Copy(block, 0, pieceData, offset, block.Length);
            offset += block.Length;
        }

        // Vérifier SHA1
        if (!_verifier!.VerifyPiece(pieceIndex, pieceData))
        {
            throw new InvalidDataException($"Non-correspondance SHA1 de la pièce {pieceIndex}");
        }

        // Écrire sur disque
        await _writer.WritePieceAsync(pieceIndex, pieceData);
        _bitfield!.SetPiece(pieceIndex, true);

        Console.WriteLine($"Pièce {pieceIndex}/{_torrent.Info.PieceCount} terminée " +
                          $"({Progress:F1}% @ {DownloadSpeed / 1024} Ko/s)");
    }

    /// <summary>
    /// Extrait les données du bloc du payload d'un message Piece
    /// Format: [4 octets index][4 octets begin][données du bloc]
    /// </summary>
    /// <param name="msg">Message Piece reçu du peer</param>
    /// <returns>Données du bloc (sans les 8 premiers octets)</returns>
    private byte[] ExtractBlockData(PeerMessage msg)
    {
        if (msg.Payload == null || msg.Payload.Length < 8)
            throw new InvalidDataException("Payload de message piece invalide");

        var blockData = new byte[msg.Payload.Length - 8];
        Array.Copy(msg.Payload, 8, blockData, 0, blockData.Length);
        return blockData;
    }

    /// <summary>
    /// Extrait pieceIndex et begin offset du payload d'un message Piece
    /// Format: [4 octets index][4 octets begin][données du bloc]
    /// </summary>
    /// <param name="msg">Message Piece reçu du peer</param>
    /// <returns>Tuple (pieceIndex, begin offset)</returns>
    private (int pieceIndex, int begin) ParsePieceMetadata(PeerMessage msg)
    {
        if (msg.Payload == null || msg.Payload.Length < 8)
            throw new InvalidDataException("Payload de message piece invalide");

        // Parser l'index de pièce (4 octets, big-endian)
        int index = (msg.Payload[0] << 24) | (msg.Payload[1] << 16) |
                    (msg.Payload[2] << 8) | msg.Payload[3];

        // Parser l'offset begin (4 octets, big-endian)
        int begin = (msg.Payload[4] << 24) | (msg.Payload[5] << 16) |
                    (msg.Payload[6] << 8) | msg.Payload[7];

        return (index, begin);
    }

    /// <summary>
    /// Gère l'échec d'un peer avec backoff exponentiel temporaire
    /// </summary>
    private void HandlePeerFailure(PeerConnection peer, Exception ex)
    {
        // Vérifier si c'est une erreur fatale qui mérite une liste noire immédiate
        if (IsFatalError(ex))
        {
            _permanentBlacklist.Add(peer);
            Console.WriteLine($"[TDM] ⛔ Peer mis en liste noire permanente: {ex.GetType().Name}");
            return;
        }

        // Backoff exponentiel pour erreurs temporaires
        if (!_peerCooldowns.TryGetValue(peer, out var cooldown))
        {
            cooldown = new PeerCooldown
            {
                Peer = peer,
                FailureCount = 0,
                CurrentBackoff = INITIAL_BACKOFF
            };
            _peerCooldowns[peer] = cooldown;
        }

        cooldown.FailureCount++;
        cooldown.CooldownUntil = DateTime.UtcNow + cooldown.CurrentBackoff;

        Console.WriteLine($"[TDM] 🕒 Peer en cooldown pour {cooldown.CurrentBackoff.TotalSeconds:F1}s (échec {cooldown.FailureCount})");

        // Augmenter le backoff pour la prochaine fois (exponentiel)
        cooldown.CurrentBackoff = TimeSpan.FromSeconds(
            Math.Min(cooldown.CurrentBackoff.TotalSeconds * BACKOFF_MULTIPLIER, MAX_BACKOFF.TotalSeconds)
        );

        // Liste noire permanente si trop d'échecs au total
        if (cooldown.FailureCount >= PERMANENT_BLACKLIST_THRESHOLD)
        {
            _permanentBlacklist.Add(peer);
            _peerCooldowns.Remove(peer);
            Console.WriteLine($"[TDM] ⛔ Peer mis en liste noire permanente après {cooldown.FailureCount} échecs");
        }
    }

    /// <summary>
    /// Vérifie si l'erreur est fatale (liste noire immédiate) ou temporaire (cooldown)
    /// </summary>
    private bool IsFatalError(Exception ex)
    {
        return ex is InvalidOperationException ||  // Échec du handshake
               ex is ArgumentException ||           // Format de données invalide
               (ex.Message?.Contains("InfoHash") ?? false);  // Mauvais torrent
    }

    /// <summary>
    /// Réinitialise le backoff d'un peer après un succès
    /// </summary>
    private void HandlePeerSuccess(PeerConnection peer)
    {
        if (_peerCooldowns.TryGetValue(peer, out var cooldown))
        {
            // Réinitialiser le backoff à la valeur initiale après succès
            cooldown.CurrentBackoff = INITIAL_BACKOFF;
        }
    }

    /// <summary>
    /// Libère les ressources utilisées (peers, writer de fichier, sémaphore)
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        // Fermer toutes les connexions peers
        foreach (var peer in _allPeers)
        {
            peer.Dispose();
        }
        _allPeers.Clear();

        // Fermer le writer de fichier
        _writer?.Dispose();

        _disposed = true;
    }
}
