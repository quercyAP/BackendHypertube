using BitTorrent.ComparativeTests.Models;

namespace BitTorrent.ComparativeTests.ApiClients;

/// <summary>
/// Client pour PirateBay (fallback si YTS ne fonctionne pas)
/// Note: Pour l'instant, on retourne null (YTS devrait suffire)
/// </summary>
public class PirateBayApiClient
{
    public async Task<TorrentSearchResult?> SearchPopularMovieAsync()
    {
        // Pour l'instant, on ne l'implémente pas
        // YTS devrait suffire pour notre test comparatif
        Console.WriteLine("[PirateBay] API non implémentée (fallback non nécessaire)");
        await Task.Delay(100);
        return null;
    }
}
