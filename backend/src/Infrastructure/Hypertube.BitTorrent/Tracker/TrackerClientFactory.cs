namespace Hypertube.BitTorrent.Tracker;

/// <summary>
/// Factory pour créer le bon type de tracker client selon l'URL
/// </summary>
public static class TrackerClientFactory
{
    /// <summary>
    /// Crée un tracker client approprié selon le protocole de l'URL
    /// </summary>
    /// <param name="trackerUrl">URL du tracker (http://, https://, ou udp://)</param>
    /// <returns>Instance de ITrackerClient (HttpTrackerClient ou UdpTrackerClient)</returns>
    /// <exception cref="NotSupportedException">Si le protocole n'est pas supporté</exception>
    public static ITrackerClient CreateClient(string trackerUrl)
    {
        if (string.IsNullOrWhiteSpace(trackerUrl))
        {
            throw new ArgumentException("Tracker URL cannot be null or empty", nameof(trackerUrl));
        }

        if (trackerUrl.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
        {
            return new UdpTrackerClient();
        }

        if (trackerUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trackerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpTrackerClient();
        }

        throw new NotSupportedException($"Unsupported tracker protocol in URL: {trackerUrl}. Supported protocols: http://, https://, udp://");
    }

    /// <summary>
    /// Détermine si un protocole tracker est supporté
    /// </summary>
    /// <param name="trackerUrl">URL du tracker</param>
    /// <returns>True si supporté, False sinon</returns>
    public static bool IsSupported(string trackerUrl)
    {
        if (string.IsNullOrWhiteSpace(trackerUrl))
        {
            return false;
        }

        return trackerUrl.StartsWith("udp://", StringComparison.OrdinalIgnoreCase) ||
               trackerUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trackerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }
}
