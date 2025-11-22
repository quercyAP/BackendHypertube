namespace BitTorrent.ComparativeTests.Models;

/// <summary>
/// Résultat de recherche de torrent depuis YTS ou PirateBay
/// </summary>
public class TorrentSearchResult
{
    public string Title { get; set; } = string.Empty;
    public string TorrentUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int Seeders { get; set; }
    public string Quality { get; set; } = string.Empty;
}
