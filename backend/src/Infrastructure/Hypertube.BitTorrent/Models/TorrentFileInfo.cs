namespace Hypertube.BitTorrent.Models;

/// <summary>
/// Représente un fichier individuel dans un torrent multi-fichiers
/// </summary>
public class TorrentFileInfo
{
    /// <summary>
    /// Taille du fichier en bytes
    /// </summary>
    public long Length { get; set; }

    /// <summary>
    /// Chemin du fichier (relatif à la racine du torrent)
    /// Exemple: ["folder", "subfolder", "file.mkv"]
    /// </summary>
    public List<string> Path { get; set; } = new();

    /// <summary>
    /// Chemin complet du fichier (rejoint avec '/')
    /// </summary>
    public string FullPath => string.Join("/", Path);
}
