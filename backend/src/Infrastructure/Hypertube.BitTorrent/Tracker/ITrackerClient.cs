using Hypertube.BitTorrent.Models;

namespace Hypertube.BitTorrent.Tracker;

/// <summary>
/// Interface commune pour les clients tracker BitTorrent (HTTP et UDP)
/// </summary>
public interface ITrackerClient
{
    /// <summary>
    /// Protocole utilisé par ce client (HTTP ou UDP)
    /// </summary>
    string Protocol { get; }

    /// <summary>
    /// Envoie une requête announce au tracker pour obtenir la liste des peers
    /// </summary>
    /// <param name="torrent">Fichier torrent</param>
    /// <param name="port">Port d'écoute de notre client</param>
    /// <param name="uploaded">Nombre de bytes uploadés</param>
    /// <param name="downloaded">Nombre de bytes téléchargés</param>
    /// <param name="left">Nombre de bytes restants à télécharger</param>
    /// <param name="eventType">Type d'événement: "started", "stopped", "completed", ou "" pour none</param>
    /// <returns>Réponse du tracker contenant la liste des peers</returns>
    Task<TrackerResponse> AnnounceAsync(
        TorrentFile torrent,
        int port,
        long uploaded,
        long downloaded,
        long left,
        string eventType = "started");
}
