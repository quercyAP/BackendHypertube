namespace Hypertube.BitTorrent.Models;

/// <summary>
/// Représente le dictionnaire "info" d'un fichier .torrent
/// </summary>
public class TorrentInfo
{
    /// <summary>
    /// Nom du torrent (nom du fichier ou du dossier racine)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Taille d'une pièce en bytes (habituellement 256 KB, 512 KB, 1 MB, 2 MB...)
    /// </summary>
    public long PieceLength { get; set; }

    /// <summary>
    /// Hashes SHA1 concaténés de toutes les pièces (20 bytes par pièce)
    /// </summary>
    public byte[] Pieces { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Nombre total de pièces (calculé depuis Pieces.Length / 20)
    /// </summary>
    public int PieceCount => Pieces.Length / 20;

    /// <summary>
    /// Taille totale du fichier (pour single-file torrent)
    /// Null si multi-file torrent
    /// </summary>
    public long? Length { get; set; }

    /// <summary>
    /// Liste des fichiers (pour multi-file torrent)
    /// Null ou vide si single-file torrent
    /// </summary>
    public List<TorrentFileInfo>? Files { get; set; }

    /// <summary>
    /// Est-ce un torrent single-file ?
    /// </summary>
    public bool IsSingleFile => Length.HasValue;

    /// <summary>
    /// Taille totale de tous les fichiers
    /// </summary>
    public long TotalLength => IsSingleFile
        ? Length!.Value
        : Files?.Sum(f => f.Length) ?? 0;
}
