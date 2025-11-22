namespace Hypertube.BitTorrent.Models;

/// <summary>
/// Représente la réponse d'un tracker BitTorrent
/// </summary>
public class TrackerResponse
{
    /// <summary>
    /// Intervalle en secondes avant la prochaine requête au tracker
    /// </summary>
    public int Interval { get; set; }

    /// <summary>
    /// Intervalle minimum en secondes (optionnel)
    /// </summary>
    public int? MinInterval { get; set; }

    /// <summary>
    /// Tracker ID (optionnel, pour les requêtes suivantes)
    /// </summary>
    public string? TrackerId { get; set; }

    /// <summary>
    /// Nombre de seeders (peers qui ont le fichier complet)
    /// </summary>
    public int? Complete { get; set; }

    /// <summary>
    /// Nombre de leechers (peers qui téléchargent)
    /// </summary>
    public int? Incomplete { get; set; }

    /// <summary>
    /// Liste des peers
    /// </summary>
    public List<PeerInfo> Peers { get; set; } = new();

    /// <summary>
    /// Message d'erreur si la requête a échoué
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Message d'avertissement (optionnel)
    /// </summary>
    public string? WarningMessage { get; set; }

    /// <summary>
    /// La requête a-t-elle réussi ?
    /// </summary>
    public bool IsSuccess => string.IsNullOrEmpty(FailureReason);
}
