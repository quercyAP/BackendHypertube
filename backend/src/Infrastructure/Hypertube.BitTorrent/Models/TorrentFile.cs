namespace Hypertube.BitTorrent.Models;

/// <summary>
/// Représente un fichier .torrent complet
/// </summary>
public class TorrentFile
{
    /// <summary>
    /// URL du tracker principal
    /// </summary>
    public string Announce { get; set; } = string.Empty;

    /// <summary>
    /// Liste de trackers de backup (tier list)
    /// </summary>
    public List<List<string>>? AnnounceList { get; set; }

    /// <summary>
    /// Informations sur le(s) fichier(s) du torrent
    /// </summary>
    public TorrentInfo Info { get; set; } = new();

    /// <summary>
    /// InfoHash: SHA1 hash du dictionnaire "info" bencodé (20 bytes)
    /// Identifiant unique du torrent
    /// </summary>
    public byte[] InfoHash { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// InfoHash en format hexadécimal (40 caractères)
    /// </summary>
    public string InfoHashHex => BitConverter.ToString(InfoHash).Replace("-", "").ToLower();

    /// <summary>
    /// Commentaire optionnel
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Créateur du torrent
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Date de création (Unix timestamp)
    /// </summary>
    public long? CreationDate { get; set; }

    /// <summary>
    /// Encoding utilisé pour les strings
    /// </summary>
    public string? Encoding { get; set; }
}
