namespace Hypertube.BitTorrent.Models;

/// <summary>
/// Représente un peer BitTorrent
/// </summary>
public class PeerInfo
{
    /// <summary>
    /// Adresse IP du peer
    /// </summary>
    public string IP { get; set; } = string.Empty;

    /// <summary>
    /// Port TCP du peer
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Peer ID (optionnel, retourné par certains trackers)
    /// </summary>
    public string? PeerId { get; set; }

    public override string ToString()
    {
        return $"{IP}:{Port}";
    }
}
