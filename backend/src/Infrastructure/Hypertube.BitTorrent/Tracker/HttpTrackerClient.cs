using System.Net;
using System.Text;
using System.Web;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using Hypertube.BitTorrent.Models;
using Hypertube.BitTorrent.Utils;

namespace Hypertube.BitTorrent.Tracker;

/// <summary>
/// Client pour communiquer avec les trackers BitTorrent via HTTP/HTTPS
/// </summary>
public class HttpTrackerClient : ITrackerClient
{
    private readonly HttpClient _httpClient;
    private readonly string _peerId;

    /// <summary>
    /// Protocole utilisé par ce client
    /// </summary>
    public string Protocol => "HTTP";

    public HttpTrackerClient()
    {
        _httpClient = new HttpClient();
        _peerId = PeerIdGenerator.GenerateString();
    }

    public HttpTrackerClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _peerId = PeerIdGenerator.GenerateString();
    }

    /// <summary>
    /// Envoie une requête announce au tracker
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
            var url = BuildAnnounceUrl(torrent, port, uploaded, downloaded, left, eventType);
            var responseBytes = await _httpClient.GetByteArrayAsync(url);
            return ParseTrackerResponse(responseBytes);
        }
        catch (Exception ex)
        {
            return new TrackerResponse
            {
                FailureReason = $"Failed to contact tracker: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Construit l'URL de la requête announce
    /// </summary>
    private string BuildAnnounceUrl(
        TorrentFile torrent,
        int port,
        long uploaded,
        long downloaded,
        long left,
        string eventType)
    {
        var queryParams = new Dictionary<string, string>
        {
            ["info_hash"] = UrlEncodeInfoHash(torrent.InfoHash),
            ["peer_id"] = UrlEncodePeerId(_peerId),
            ["port"] = port.ToString(),
            ["uploaded"] = uploaded.ToString(),
            ["downloaded"] = downloaded.ToString(),
            ["left"] = left.ToString(),
            ["compact"] = "1", // Demander le format compact (6 bytes par peer)
        };

        // Ajouter event seulement si spécifié
        if (!string.IsNullOrEmpty(eventType))
        {
            queryParams["event"] = eventType;
        }

        var queryString = string.Join("&", queryParams.Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{torrent.Announce}?{queryString}";
    }

    /// <summary>
    /// URL-encode l'InfoHash (format spécial BitTorrent)
    /// </summary>
    private static string UrlEncodeInfoHash(byte[] infoHash)
    {
        var sb = new StringBuilder();
        foreach (var b in infoHash)
        {
            sb.Append($"%{b:X2}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// URL-encode le Peer ID
    /// </summary>
    private static string UrlEncodePeerId(string peerId)
    {
        var bytes = Encoding.UTF8.GetBytes(peerId);
        var sb = new StringBuilder();
        foreach (var b in bytes)
        {
            // Encoder tous les caractères sauf alphanumériques et - _ . ~
            if ((b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9') ||
                b == '-' || b == '_' || b == '.' || b == '~')
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append($"%{b:X2}");
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parse la réponse Bencode du tracker
    /// </summary>
    private static TrackerResponse ParseTrackerResponse(byte[] responseBytes)
    {
        var parser = new BencodeParser();
        BDictionary responseDict;

        try
        {
            using var stream = new MemoryStream(responseBytes);
            responseDict = parser.Parse<BDictionary>(stream);
        }
        catch (Exception ex)
        {
            return new TrackerResponse
            {
                FailureReason = $"Failed to parse tracker response: {ex.Message}"
            };
        }

        var response = new TrackerResponse();

        // Vérifier failure reason
        if (responseDict.TryGetValue("failure reason", out var failureValue) && failureValue is BString failureString)
        {
            response.FailureReason = failureString.ToString();
            return response;
        }

        // Warning message (optionnel)
        if (responseDict.TryGetValue("warning message", out var warningValue) && warningValue is BString warningString)
        {
            response.WarningMessage = warningString.ToString();
        }

        // Interval
        if (responseDict.TryGetValue("interval", out var intervalValue) && intervalValue is BNumber intervalNumber)
        {
            response.Interval = (int)intervalNumber.Value;
        }

        // Min interval (optionnel)
        if (responseDict.TryGetValue("min interval", out var minIntervalValue) && minIntervalValue is BNumber minIntervalNumber)
        {
            response.MinInterval = (int)minIntervalNumber.Value;
        }

        // Tracker ID (optionnel)
        if (responseDict.TryGetValue("tracker id", out var trackerIdValue) && trackerIdValue is BString trackerIdString)
        {
            response.TrackerId = trackerIdString.ToString();
        }

        // Complete (seeders)
        if (responseDict.TryGetValue("complete", out var completeValue) && completeValue is BNumber completeNumber)
        {
            response.Complete = (int)completeNumber.Value;
        }

        // Incomplete (leechers)
        if (responseDict.TryGetValue("incomplete", out var incompleteValue) && incompleteValue is BNumber incompleteNumber)
        {
            response.Incomplete = (int)incompleteNumber.Value;
        }

        // Peers
        if (responseDict.TryGetValue("peers", out var peersValue))
        {
            // Format compact (6 bytes par peer)
            if (peersValue is BString peersString)
            {
                response.Peers = ParseCompactPeers(peersString.Value.ToArray());
            }
            // Format dictionnaire (rare)
            else if (peersValue is BList peersList)
            {
                response.Peers = ParseDictionaryPeers(peersList);
            }
        }

        return response;
    }

    /// <summary>
    /// Parse les peers en format compact (6 bytes par peer: 4 bytes IP + 2 bytes port big-endian)
    /// </summary>
    private static List<PeerInfo> ParseCompactPeers(byte[] peersData)
    {
        var peers = new List<PeerInfo>();

        for (int i = 0; i < peersData.Length; i += 6)
        {
            if (i + 6 > peersData.Length)
                break;

            // 4 bytes pour l'IP
            var ip = new IPAddress(peersData[i..(i + 4)]);

            // 2 bytes pour le port (big-endian)
            var port = (peersData[i + 4] << 8) | peersData[i + 5];

            peers.Add(new PeerInfo
            {
                IP = ip.ToString(),
                Port = port
            });
        }

        return peers;
    }

    /// <summary>
    /// Parse les peers en format dictionnaire (rarement utilisé)
    /// </summary>
    private static List<PeerInfo> ParseDictionaryPeers(BList peersList)
    {
        var peers = new List<PeerInfo>();

        foreach (var peerItem in peersList)
        {
            if (peerItem is BDictionary peerDict)
            {
                var peer = new PeerInfo();

                if (peerDict.TryGetValue("ip", out var ipValue) && ipValue is BString ipString)
                {
                    peer.IP = ipString.ToString();
                }

                if (peerDict.TryGetValue("port", out var portValue) && portValue is BNumber portNumber)
                {
                    peer.Port = (int)portNumber.Value;
                }

                if (peerDict.TryGetValue("peer id", out var peerIdValue) && peerIdValue is BString peerIdString)
                {
                    peer.PeerId = peerIdString.ToString();
                }

                peers.Add(peer);
            }
        }

        return peers;
    }
}
