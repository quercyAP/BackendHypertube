using System.Net;
using System.Net.Sockets;
using System.Text;
using Hypertube.BitTorrent.Models;
using Hypertube.BitTorrent.Utils;

namespace Hypertube.BitTorrent.Tracker;

/// <summary>
/// Client pour communiquer avec les trackers BitTorrent via UDP (BEP-15)
/// </summary>
public class UdpTrackerClient : ITrackerClient, IDisposable
{
    private readonly string _peerId;
    private readonly Random _random = new();

    // Connection ID caching (valid for 60s)
    private long? _connectionId;
    private DateTime _connectionIdExpiry;
    private string? _lastTrackerHost;
    private int _lastTrackerPort;

    // Constants
    private const long ProtocolId = 0x41727101980;  // Magic constant for BEP-15

    /// <summary>
    /// Protocole utilisé par ce client
    /// </summary>
    public string Protocol => "UDP";

    public UdpTrackerClient()
    {
        _peerId = PeerIdGenerator.GenerateString();
    }

    /// <summary>
    /// Envoie une requête announce au tracker UDP
    /// </summary>
    public async Task<TrackerResponse> AnnounceAsync(
        TorrentFile torrent,
        int port,
        long uploaded,
        long downloaded,
        long left,
        string eventType = "started")
    {
        try
        {
            // Console.WriteLine($"[UDP] AnnounceAsync START - Tracker: {torrent.Announce}");  // TOO VERBOSE

            // Parser l'URL UDP
            var uri = new Uri(torrent.Announce);
            var host = uri.Host;
            var trackerPort = uri.Port;
            // Console.WriteLine($"[UDP] Parsed - Host: {host}, Port: {trackerPort}");  // TOO VERBOSE

            // Résoudre l'hôte en IPEndPoint
            // Console.WriteLine($"[UDP] Resolving DNS for {host}...");  // TOO VERBOSE
            var addresses = await Dns.GetHostAddressesAsync(host);
            // Console.WriteLine($"[UDP] DNS resolved: {addresses[0]} ({addresses.Length} addresses found)");  // TOO VERBOSE
            var trackerEndpoint = new IPEndPoint(addresses[0], trackerPort);

            // Créer un nouveau UdpClient pour cette requête (NE PAS appeler Connect!)
            // Console.WriteLine($"[UDP] Creating UdpClient...");  // TOO VERBOSE
            using var udpClient = new UdpClient();
            // Console.WriteLine($"[UDP] UdpClient created");  // TOO VERBOSE

            // Obtenir connection_id (avec cache)
            // Console.WriteLine($"[UDP] Getting connection ID...");  // TOO VERBOSE
            var connectionId = await GetConnectionIdAsync(host, trackerPort, udpClient, trackerEndpoint);
            // Console.WriteLine($"[UDP] Got connection ID: {connectionId}");  // TOO VERBOSE

            // Construire et envoyer announce request
            // Console.WriteLine($"[UDP] Building announce request...");  // TOO VERBOSE
            var announceRequest = BuildAnnounceRequest(
                connectionId,
                torrent.InfoHash,
                Encoding.ASCII.GetBytes(_peerId),
                downloaded,
                left,
                uploaded,
                eventType,
                port
            );
            // Console.WriteLine($"[UDP] Announce request built ({announceRequest.Length} bytes)");  // TOO VERBOSE

            // Console.WriteLine($"[UDP] Sending announce request...");  // TOO VERBOSE
            var announceResponse = await SendWithRetryAsync(announceRequest, udpClient, trackerEndpoint);
            // Console.WriteLine($"[UDP] Got announce response ({announceResponse.Length} bytes)");  // TOO VERBOSE

            // Parser la réponse
            // Console.WriteLine($"[UDP] Parsing announce response...");  // TOO VERBOSE
            var result = ParseAnnounceResponse(announceResponse);
            Console.WriteLine($"[UDP] AnnounceAsync SUCCESS - {result.Peers.Count} peers");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UDP] AnnounceAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            return new TrackerResponse
            {
                FailureReason = $"UDP tracker error: {ex.GetType().Name}: {ex.Message} | Stack: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}"
            };
        }
    }

    /// <summary>
    /// Obtient un connection ID (avec cache de 60s)
    /// </summary>
    private async Task<long> GetConnectionIdAsync(string host, int port, UdpClient udpClient, IPEndPoint trackerEndpoint)
    {
        // Console.WriteLine($"[UDP.GetConnID] START - host={host}, port={port}");  // TOO VERBOSE

        // Réutiliser si encore valide (même tracker, <60s)
        if (_connectionId.HasValue &&
            DateTime.UtcNow < _connectionIdExpiry &&
            _lastTrackerHost == host &&
            _lastTrackerPort == port)
        {
            // Console.WriteLine($"[UDP.GetConnID] Using cached connection ID: {_connectionId.Value}");  // TOO VERBOSE
            return _connectionId.Value;
        }

        // Sinon, faire une nouvelle connexion
        // Console.WriteLine($"[UDP.GetConnID] Calling ConnectAsync...");  // TOO VERBOSE
        var connectionId = await ConnectAsync(host, port, udpClient, trackerEndpoint);
        // Console.WriteLine($"[UDP.GetConnID] ConnectAsync returned: {connectionId}");  // TOO VERBOSE

        _connectionId = connectionId;
        _connectionIdExpiry = DateTime.UtcNow.AddSeconds(60);
        _lastTrackerHost = host;
        _lastTrackerPort = port;

        // Console.WriteLine($"[UDP.GetConnID] END - returning {connectionId}");  // TOO VERBOSE
        return connectionId;
    }

    /// <summary>
    /// Phase Connect : obtient un connection_id du tracker
    /// </summary>
    private async Task<long> ConnectAsync(string host, int port, UdpClient udpClient, IPEndPoint trackerEndpoint)
    {
        // Console.WriteLine($"[UDP.Connect] START - {host}:{port}");  // TOO VERBOSE

        // Construire Connect Request (16 bytes)
        var request = new byte[16];
        var transactionId = _random.Next();

        // Offset 0: protocol_id (64-bit)
        WriteInt64BigEndian(request, 0, ProtocolId);

        // Offset 8: action = 0 (connect)
        WriteInt32BigEndian(request, 8, 0);

        // Offset 12: transaction_id
        WriteInt32BigEndian(request, 12, transactionId);

        // Console.WriteLine($"[UDP.Connect] Built connect request ({request.Length} bytes), txId={transactionId}");  // TOO VERBOSE
        // Console.WriteLine($"[UDP.Connect] Calling SendWithRetryAsync...");  // TOO VERBOSE

        // Envoyer et recevoir
        var response = await SendWithRetryAsync(request, udpClient, trackerEndpoint);

        // Console.WriteLine($"[UDP.Connect] SendWithRetryAsync returned ({response.Length} bytes)");  // TOO VERBOSE

        // Vérifier réponse (minimum 16 bytes)
        if (response.Length < 16)
        {
            throw new Exception("Invalid connect response length");
        }

        // Parser Connect Response
        var action = ReadInt32BigEndian(response, 0);
        var responseTransactionId = ReadInt32BigEndian(response, 4);
        var connectionId = ReadInt64BigEndian(response, 8);

        // Console.WriteLine($"[UDP.Connect] Parsed response - action={action}, txId={responseTransactionId}, connId={connectionId}");  // TOO VERBOSE

        // Vérifier action = 0 (connect)
        if (action == 3)
        {
            // Error response
            var errorMsg = Encoding.UTF8.GetString(response, 8, response.Length - 8);
            throw new Exception($"Tracker error: {errorMsg}");
        }

        if (action != 0)
        {
            throw new Exception($"Invalid connect response action: {action}");
        }

        // Vérifier transaction_id
        if (responseTransactionId != transactionId)
        {
            throw new Exception("Transaction ID mismatch");
        }

        // Console.WriteLine($"[UDP.Connect] SUCCESS - returning connectionId={connectionId}");  // TOO VERBOSE
        return connectionId;
    }

    /// <summary>
    /// Construit une Announce Request (98 bytes)
    /// </summary>
    private byte[] BuildAnnounceRequest(
        long connectionId,
        byte[] infoHash,
        byte[] peerId,
        long downloaded,
        long left,
        long uploaded,
        string eventType,
        int port)
    {
        var request = new byte[98];
        var transactionId = _random.Next();

        // Offset 0: connection_id (64-bit)
        WriteInt64BigEndian(request, 0, connectionId);

        // Offset 8: action = 1 (announce)
        WriteInt32BigEndian(request, 8, 1);

        // Offset 12: transaction_id (32-bit)
        WriteInt32BigEndian(request, 12, transactionId);

        // Offset 16: info_hash (20 bytes)
        Array.Copy(infoHash, 0, request, 16, 20);

        // Offset 36: peer_id (20 bytes)
        Array.Copy(peerId, 0, request, 36, 20);

        // Offset 56: downloaded (64-bit)
        WriteInt64BigEndian(request, 56, downloaded);

        // Offset 64: left (64-bit)
        WriteInt64BigEndian(request, 64, left);

        // Offset 72: uploaded (64-bit)
        WriteInt64BigEndian(request, 72, uploaded);

        // Offset 80: event (32-bit: 0=none, 1=completed, 2=started, 3=stopped)
        int eventValue = eventType.ToLower() switch
        {
            "completed" => 1,
            "started" => 2,
            "stopped" => 3,
            _ => 0
        };
        WriteInt32BigEndian(request, 80, eventValue);

        // Offset 84: IP address = 0 (default)
        WriteInt32BigEndian(request, 84, 0);

        // Offset 88: key (32-bit random)
        WriteInt32BigEndian(request, 88, _random.Next());

        // Offset 92: num_want = -1 (default)
        WriteInt32BigEndian(request, 92, -1);

        // Offset 96: port (16-bit)
        WriteInt16BigEndian(request, 96, (short)port);

        return request;
    }

    /// <summary>
    /// Parse une Announce Response
    /// </summary>
    private TrackerResponse ParseAnnounceResponse(byte[] response)
    {
        if (response.Length < 20)
        {
            throw new Exception("Invalid announce response length");
        }

        var action = ReadInt32BigEndian(response, 0);

        // Check for error response
        if (action == 3)
        {
            var errorMsg = Encoding.UTF8.GetString(response, 8, response.Length - 8);
            return new TrackerResponse
            {
                FailureReason = errorMsg
            };
        }

        if (action != 1)
        {
            throw new Exception($"Invalid announce response action: {action}");
        }

        var transactionId = ReadInt32BigEndian(response, 4);
        var interval = ReadInt32BigEndian(response, 8);
        var leechers = ReadInt32BigEndian(response, 12);
        var seeders = ReadInt32BigEndian(response, 16);

        // Parser les peers (6 bytes each: 4-byte IP + 2-byte port)
        var peers = new List<PeerInfo>();
        for (int i = 20; i + 6 <= response.Length; i += 6)
        {
            var ip = new IPAddress(response[i..(i + 4)]);
            var peerPort = (response[i + 4] << 8) | response[i + 5];

            peers.Add(new PeerInfo
            {
                IP = ip.ToString(),
                Port = peerPort
            });
        }

        return new TrackerResponse
        {
            Interval = interval,
            Incomplete = leechers,
            Complete = seeders,
            Peers = peers
        };
    }

    /// <summary>
    /// Envoie une requête avec retry logic (exponential backoff)
    /// Timeout: 5 × 2^n secondes (n = 0)
    /// </summary>
    private async Task<byte[]> SendWithRetryAsync(byte[] request, UdpClient udpClient, IPEndPoint endpoint, int maxRetries = 2)
    {
        // Console.WriteLine($"[UDP.SendRetry] START - endpoint={endpoint}, maxRetries={maxRetries}");  // TOO VERBOSE

        for (int n = 0; n <= maxRetries; n++)
        {
            try
            {
                // Timeout: 5 × 2^n seconds (réduit pour fallback rapide)
                var timeoutMs = 5 * 1000 * (int)Math.Pow(2, n);
                // Console.WriteLine($"[UDP.SendRetry] Attempt {n + 1}/{maxRetries + 1}, timeout={timeoutMs}ms");  // TOO VERBOSE

                // Envoyer la requête avec endpoint explicite (ne nécessite PAS Connect())
                // Console.WriteLine($"[UDP.SendRetry] Sending {request.Length} bytes...");  // TOO VERBOSE
                await udpClient.SendAsync(request, request.Length, endpoint);
                // Console.WriteLine($"[UDP.SendRetry] Sent successfully");  // TOO VERBOSE

                // Recevoir la réponse avec timeout (Task.WhenAny est plus fiable que CancellationToken)
                // Console.WriteLine($"[UDP.SendRetry] Waiting for response (timeout={timeoutMs}ms)...");  // TOO VERBOSE
                var receiveTask = udpClient.ReceiveAsync();
                var timeoutTask = Task.Delay(timeoutMs);
                var completedTask = await Task.WhenAny(receiveTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    // Console.WriteLine($"[UDP.SendRetry] TIMEOUT after {timeoutMs}ms");  // TOO VERBOSE
                    // Timeout - throw pour retry
                    if (n == maxRetries)
                    {
                        // Console.WriteLine($"[UDP.SendRetry] Max retries reached, throwing TimeoutException");  // TOO VERBOSE
                        throw new TimeoutException($"UDP tracker did not respond after {maxRetries + 1} attempts");
                    }
                    // Console.WriteLine($"[UDP.SendRetry] Retrying...");  // TOO VERBOSE
                    // Continuer la boucle pour retry
                    continue;
                }

                // Console.WriteLine($"[UDP.SendRetry] Response received!");  // TOO VERBOSE
                // Succès - retourner le résultat
                var result = await receiveTask;
                // Console.WriteLine($"[UDP.SendRetry] SUCCESS - received {result.Buffer.Length} bytes");  // TOO VERBOSE
                return result.Buffer;
            }
            catch (OperationCanceledException)
            {
                // Console.WriteLine($"[UDP.SendRetry] OperationCanceledException caught");  // TOO VERBOSE
                // Timeout, retry si pas dernier essai
                if (n == maxRetries)
                {
                    throw new TimeoutException($"UDP tracker did not respond after {maxRetries + 1} attempts");
                }
                // Sinon, on continue la boucle pour retry
            }
        }

        throw new Exception("Should not reach here");
    }

    #region Binary Serialization Helpers (Big-Endian)

    private static void WriteInt64BigEndian(byte[] buffer, int offset, long value)
    {
        buffer[offset + 0] = (byte)(value >> 56);
        buffer[offset + 1] = (byte)(value >> 48);
        buffer[offset + 2] = (byte)(value >> 40);
        buffer[offset + 3] = (byte)(value >> 32);
        buffer[offset + 4] = (byte)(value >> 24);
        buffer[offset + 5] = (byte)(value >> 16);
        buffer[offset + 6] = (byte)(value >> 8);
        buffer[offset + 7] = (byte)value;
    }

    private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset + 0] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteInt16BigEndian(byte[] buffer, int offset, short value)
    {
        buffer[offset + 0] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    private static long ReadInt64BigEndian(byte[] buffer, int offset)
    {
        return ((long)buffer[offset + 0] << 56) |
               ((long)buffer[offset + 1] << 48) |
               ((long)buffer[offset + 2] << 40) |
               ((long)buffer[offset + 3] << 32) |
               ((long)buffer[offset + 4] << 24) |
               ((long)buffer[offset + 5] << 16) |
               ((long)buffer[offset + 6] << 8) |
               (long)buffer[offset + 7];
    }

    private static int ReadInt32BigEndian(byte[] buffer, int offset)
    {
        return (buffer[offset + 0] << 24) |
               (buffer[offset + 1] << 16) |
               (buffer[offset + 2] << 8) |
               buffer[offset + 3];
    }

    private static short ReadInt16BigEndian(byte[] buffer, int offset)
    {
        return (short)((buffer[offset + 0] << 8) | buffer[offset + 1]);
    }

    #endregion

    public void Dispose()
    {
        // Rien à disposer - les UdpClients sont créés et disposés dans les méthodes locales
    }
}
