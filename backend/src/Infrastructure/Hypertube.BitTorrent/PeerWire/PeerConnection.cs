using System.Net.Sockets;
using System.Text;
using Hypertube.BitTorrent.Models;

namespace Hypertube.BitTorrent.PeerWire;

/// <summary>
/// Gère la connexion TCP avec un peer BitTorrent
/// </summary>
public class PeerConnection : IDisposable
{
    private readonly PeerInfo _peerInfo;
    private readonly byte[] _infoHash;
    private readonly byte[] _peerId;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _streamLock = new SemaphoreSlim(1, 1); // Async-safe lock for thread-safe stream access

    // État de la connexion
    public bool IsConnected => _tcpClient?.Connected ?? false;

    // État du choking (nous)
    public bool AmChoking { get; private set; } = true;
    public bool AmInterested { get; private set; } = false;

    // État du choking (peer)
    public bool PeerChoking { get; private set; } = true;
    public bool PeerInterested { get; private set; } = false;

    // Expose peer info for deduplication
    public string PeerAddress => $"{_peerInfo.IP}:{_peerInfo.Port}";

    // Bitfield du peer (quelles pièces il possède)
    public byte[]? PeerBitfield { get; private set; }

    // Upload support: Queue of block requests from this peer
    private readonly Queue<BlockRequest> _requestQueue = new();
    public Queue<BlockRequest> RequestQueue => _requestQueue;  // Exposed for TorrentDownloadManager

    // UNCHOKE LOGIC: Track download rate from this peer
    private long _bytesReceivedFromPeer = 0;
    private DateTime _lastRateCalculation = DateTime.UtcNow;

    /// <summary>
    /// Total bytes received from this peer (thread-safe)
    /// </summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceivedFromPeer);

    /// <summary>
    /// Represents a block request from a peer (REQUEST message payload)
    /// </summary>
    public class BlockRequest
    {
        public int PieceIndex { get; set; }
        public int Begin { get; set; }
        public int Length { get; set; }
    }

    public PeerConnection(PeerInfo peerInfo, byte[] infoHash, byte[] peerId)
    {
        _peerInfo = peerInfo;
        _infoHash = infoHash;
        _peerId = peerId;
    }

    /// <summary>
    /// Établit la connexion TCP avec le peer
    /// </summary>
    public async Task ConnectAsync(int timeoutMs = 5000)
    {
        _tcpClient = new TcpClient();

        // Timeout pour la connexion
        using var cts = new CancellationTokenSource(timeoutMs);
        await _tcpClient.ConnectAsync(_peerInfo.IP, _peerInfo.Port, cts.Token);

        _stream = _tcpClient.GetStream();
    }

    /// <summary>
    /// Envoie le handshake BitTorrent
    /// Format: <pstrlen><pstr><reserved><info_hash><peer_id>
    /// Total: 68 bytes (1 + 19 + 8 + 20 + 20)
    /// </summary>
    public async Task SendHandshakeAsync()
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected");

        var handshake = new byte[68];
        int offset = 0;

        // pstrlen (1 byte) = 19
        handshake[offset++] = 19;

        // pstr (19 bytes) = "BitTorrent protocol"
        var pstr = Encoding.ASCII.GetBytes("BitTorrent protocol");
        Array.Copy(pstr, 0, handshake, offset, 19);
        offset += 19;

        // reserved (8 bytes) = 0x00
        offset += 8; // Déjà à 0

        // info_hash (20 bytes)
        Array.Copy(_infoHash, 0, handshake, offset, 20);
        offset += 20;

        // peer_id (20 bytes)
        Array.Copy(_peerId, 0, handshake, offset, 20);

        await _stream.WriteAsync(handshake, 0, 68);
        await _stream.FlushAsync();
    }

    /// <summary>
    /// Reçoit et valide le handshake du peer avec timeout
    /// </summary>
    /// <param name="timeoutMs">Timeout en millisecondes (défaut: 10000ms)</param>
    public async Task<bool> ReceiveHandshakeAsync(int timeoutMs = 10000)
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected");

        var handshake = new byte[68];
        int totalRead = 0;

        using var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            // Lire les 68 bytes du handshake
            while (totalRead < 68)
            {
                int bytesRead = await _stream.ReadAsync(handshake.AsMemory(totalRead, 68 - totalRead), cts.Token);
                if (bytesRead == 0)
                {
                    Console.WriteLine($"  [ReceiveHandshakeAsync] ✗ Connection closed by peer (read {totalRead}/68 bytes)");
                    return false; // Connexion fermée
                }
                totalRead += bytesRead;
            }
        }
        catch (OperationCanceledException)
        {
            return false; // Timeout
        }

        // Vérifier pstrlen = 19
        if (handshake[0] != 19)
        {
            Console.WriteLine($"  [ReceiveHandshakeAsync] ✗ Invalid pstrlen: {handshake[0]} (expected 19)");
            return false;
        }

        // Vérifier pstr = "BitTorrent protocol"
        var pstr = Encoding.ASCII.GetString(handshake, 1, 19);
        if (pstr != "BitTorrent protocol")
        {
            Console.WriteLine($"  [ReceiveHandshakeAsync] ✗ Invalid protocol string: '{pstr}'");
            return false;
        }

        // Vérifier info_hash correspond
        var receivedInfoHash = new byte[20];
        Array.Copy(handshake, 28, receivedInfoHash, 0, 20);

        if (!_infoHash.SequenceEqual(receivedInfoHash))
        {
            Console.WriteLine($"  [ReceiveHandshakeAsync] ✗ InfoHash mismatch");
            return false;
        }

        // Handshake valide !
        return true;
    }

    /// <summary>
    /// Reçoit un message du peer avec timeout
    /// Format: <length prefix (4 bytes)><message ID (1 byte)><payload>
    /// </summary>
    /// <param name="timeoutMs">Timeout en millisecondes (défaut: 30000ms)</param>
    public async Task<PeerMessage?> ReceiveMessageAsync(int timeoutMs = 30000)
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected");

        await _streamLock.WaitAsync();

        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);

            // Lire length prefix (4 bytes, big-endian)
            var lengthBytes = new byte[4];
            int totalRead = 0;

            while (totalRead < 4)
            {
                int bytesRead = await _stream.ReadAsync(lengthBytes.AsMemory(totalRead, 4 - totalRead), cts.Token);
                if (bytesRead == 0)
                {
                    return null; // Connexion fermée
                }
                totalRead += bytesRead;
            }

            var length = (lengthBytes[0] << 24) | (lengthBytes[1] << 16) |
                         (lengthBytes[2] << 8) | lengthBytes[3];

            // Keep-alive message (length = 0)
            if (length == 0)
            {
                return new PeerMessage(MessageType.KeepAlive);
            }

            // Lire le message complet
            var messageData = new byte[length];
            totalRead = 0;

            while (totalRead < length)
            {
                int bytesRead = await _stream.ReadAsync(messageData.AsMemory(totalRead, length - totalRead), cts.Token);
                if (bytesRead == 0)
                {
                    return null; // Connexion fermée
                }
                totalRead += bytesRead;
            }

            // Parser le message
            var messageType = (MessageType)messageData[0];
            var payload = length > 1 ? messageData[1..] : null;

            return new PeerMessage(messageType, payload);
        }
        catch (OperationCanceledException)
        {
            return null; // Timeout
        }
        finally
        {
            _streamLock.Release();
        }
    }

    /// <summary>
    /// Traite un message reçu et met à jour l'état
    /// </summary>
    public void ProcessMessage(PeerMessage message)
    {
        switch (message.Type)
        {
            case MessageType.Choke:
                PeerChoking = true;
                Console.WriteLine($"  [ProcessMessage] Peer is now CHOKING us");
                break;

            case MessageType.Unchoke:
                PeerChoking = false;
                Console.WriteLine($"  [ProcessMessage] Peer is now UNCHOKING us");
                break;

            case MessageType.Interested:
                PeerInterested = true;
                break;

            case MessageType.NotInterested:
                PeerInterested = false;
                break;

            case MessageType.Have:
                // Payload: 4 bytes (piece index, big-endian)
                if (message.Payload != null && message.Payload.Length == 4)
                {
                    int pieceIndex = (message.Payload[0] << 24) | (message.Payload[1] << 16) |
                                   (message.Payload[2] << 8) | message.Payload[3];
                    // TODO: Mettre à jour le bitfield du peer
                }
                break;

            case MessageType.Bitfield:
                // Payload: bitfield (1 bit par pièce)
                if (message.Payload != null)
                {
                    PeerBitfield = message.Payload;
                    Console.WriteLine($"  [ProcessMessage] ✓ BITFIELD received and stored ({message.Payload.Length} bytes)");
                }
                break;

            case MessageType.Request:
                // UPLOAD SUPPORT: Parse REQUEST message and queue block request
                if (message.Payload != null && message.Payload.Length == 12)
                {
                    // Parse REQUEST: <index><begin><length> (3×4 bytes, big-endian)
                    int pieceIndex = (message.Payload[0] << 24) | (message.Payload[1] << 16) |
                                     (message.Payload[2] << 8) | message.Payload[3];
                    int begin = (message.Payload[4] << 24) | (message.Payload[5] << 16) |
                                (message.Payload[6] << 8) | message.Payload[7];
                    int length = (message.Payload[8] << 24) | (message.Payload[9] << 16) |
                                 (message.Payload[10] << 8) | message.Payload[11];

                    // Validate block size (BEP-0003: max 16 KiB = 16384 bytes)
                    if (length > 16384)
                    {
                        Console.WriteLine($"  [ProcessMessage] Invalid REQUEST: block size {length} exceeds 16 KiB limit");
                        break;
                    }

                    // Queue request for processing by upload handler
                    lock (_requestQueue)
                    {
                        _requestQueue.Enqueue(new BlockRequest
                        {
                            PieceIndex = pieceIndex,
                            Begin = begin,
                            Length = length
                        });
                    }

                    Console.WriteLine($"  [ProcessMessage] Peer requested piece {pieceIndex} block [{begin}:{begin + length}] ({length} bytes)");
                }
                else
                {
                    Console.WriteLine($"  [ProcessMessage] Malformed REQUEST message (expected 12 bytes payload, got {message.Payload?.Length ?? 0})");
                }
                break;

            case MessageType.Piece:
                // Pièce reçue (sera gérée par le DownloadManager)
                Console.WriteLine($"  [ProcessMessage] Received PIECE message");
                break;

            case MessageType.Cancel:
                // Annulation d'une requête
                Console.WriteLine($"  [ProcessMessage] Peer sent CANCEL");
                break;

            case MessageType.KeepAlive:
                // Rien à faire
                break;
        }
    }

    /// <summary>
    /// Envoie un message "interested" au peer
    /// </summary>
    public async Task SendInterestedAsync()
    {
        var message = new PeerMessage(MessageType.Interested);
        await SendMessageAsync(message);
        AmInterested = true;
    }

    /// <summary>
    /// Envoie un message "not interested" au peer
    /// </summary>
    public async Task SendNotInterestedAsync()
    {
        var message = new PeerMessage(MessageType.NotInterested);
        await SendMessageAsync(message);
        AmInterested = false;
    }

    /// <summary>
    /// UNCHOKE LOGIC: Envoie un message "unchoke" au peer
    /// Indique que nous allons répondre à ses REQUEST messages
    /// </summary>
    public async Task SendUnchokeAsync()
    {
        var message = new PeerMessage(MessageType.Unchoke);
        await SendMessageAsync(message);
        AmChoking = false;
        Console.WriteLine($"  [Unchoke] Unchoked peer");
    }

    /// <summary>
    /// UNCHOKE LOGIC: Envoie un message "choke" au peer
    /// Indique que nous ne répondrons pas à ses REQUEST messages
    /// </summary>
    public async Task SendChokeAsync()
    {
        var message = new PeerMessage(MessageType.Choke);
        await SendMessageAsync(message);
        AmChoking = true;
        Console.WriteLine($"  [Choke] Choked peer");
    }

    /// <summary>
    /// UNCHOKE LOGIC: Calculate download rate from this peer (bytes/second)
    /// Used to determine which peers to unchoke (best 4 by download rate)
    /// </summary>
    /// <returns>Download rate in bytes per second</returns>
    public double GetDownloadRate()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastRateCalculation).TotalSeconds;

        // Avoid division by zero
        if (elapsed < 0.001)
            return 0;

        // Calculate bytes/second over the last 20 seconds
        var bytesReceived = Interlocked.Read(ref _bytesReceivedFromPeer);
        var rate = bytesReceived / elapsed;

        // Reset counter periodically (every 20 seconds)
        if (elapsed >= 20.0)
        {
            Interlocked.Exchange(ref _bytesReceivedFromPeer, 0);
            _lastRateCalculation = now;
        }

        return rate;
    }

    /// <summary>
    /// UNCHOKE LOGIC: Increment bytes received from this peer (thread-safe)
    /// Called by TorrentDownloadManager when PIECE messages are received
    /// </summary>
    /// <param name="bytes">Number of bytes received</param>
    public void AddBytesReceived(int bytes)
    {
        Interlocked.Add(ref _bytesReceivedFromPeer, bytes);
    }

    /// <summary>
    /// Envoie un block de données au peer (UPLOAD SUPPORT)
    /// Format PIECE message: <index><begin><block>
    /// Payload: 8 bytes header + block data
    /// </summary>
    /// <param name="pieceIndex">Zero-based piece index</param>
    /// <param name="begin">Byte offset within the piece</param>
    /// <param name="blockData">Block data to send (typically 16 KiB)</param>
    public async Task SendPieceAsync(int pieceIndex, int begin, byte[] blockData)
    {
        // Build payload: <index (4 bytes)><begin (4 bytes)><block data>
        var payload = new byte[8 + blockData.Length];

        // Write piece index (big-endian, 4 bytes)
        payload[0] = (byte)(pieceIndex >> 24);
        payload[1] = (byte)(pieceIndex >> 16);
        payload[2] = (byte)(pieceIndex >> 8);
        payload[3] = (byte)pieceIndex;

        // Write begin offset (big-endian, 4 bytes)
        payload[4] = (byte)(begin >> 24);
        payload[5] = (byte)(begin >> 16);
        payload[6] = (byte)(begin >> 8);
        payload[7] = (byte)begin;

        // Copy block data
        Array.Copy(blockData, 0, payload, 8, blockData.Length);

        // Create and send PIECE message
        var message = new PeerMessage(MessageType.Piece, payload);
        await SendMessageAsync(message);
    }

    /// <summary>
    /// Envoie une requête pour un block de données
    /// Payload: 12 bytes (index: 4, begin: 4, length: 4) en big-endian
    /// </summary>
    public async Task SendRequestAsync(int pieceIndex, int begin, int length)
    {
        var payload = new byte[12];

        // Piece index (4 bytes, big-endian)
        payload[0] = (byte)(pieceIndex >> 24);
        payload[1] = (byte)(pieceIndex >> 16);
        payload[2] = (byte)(pieceIndex >> 8);
        payload[3] = (byte)pieceIndex;

        // Begin offset (4 bytes, big-endian)
        payload[4] = (byte)(begin >> 24);
        payload[5] = (byte)(begin >> 16);
        payload[6] = (byte)(begin >> 8);
        payload[7] = (byte)begin;

        // Length (4 bytes, big-endian)
        payload[8] = (byte)(length >> 24);
        payload[9] = (byte)(length >> 16);
        payload[10] = (byte)(length >> 8);
        payload[11] = (byte)length;

        var message = new PeerMessage(MessageType.Request, payload);
        await SendMessageAsync(message);
    }

    /// <summary>
    /// Envoie un message générique
    /// </summary>
    private async Task SendMessageAsync(PeerMessage message)
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected");

        await _streamLock.WaitAsync();
        try
        {
            var bytes = message.ToBytes();
            await _stream.WriteAsync(bytes, 0, bytes.Length);
            await _stream.FlushAsync();
        }
        finally
        {
            _streamLock.Release();
        }
    }

    /// <summary>
    /// Vérifie si le peer possède une pièce spécifique
    /// </summary>
    public bool HasPiece(int pieceIndex)
    {
        if (PeerBitfield == null)
            return false;

        int byteIndex = pieceIndex / 8;
        int bitIndex = 7 - (pieceIndex % 8); // Big-endian bit order

        if (byteIndex >= PeerBitfield.Length)
            return false;

        return (PeerBitfield[byteIndex] & (1 << bitIndex)) != 0;
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _streamLock?.Dispose();
    }
}
