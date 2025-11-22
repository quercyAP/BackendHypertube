using System.Text;

namespace Hypertube.BitTorrent.Utils;

/// <summary>
/// Générateur de Peer ID unique pour le client BitTorrent
/// Format: -HY0001- + 12 caractères aléatoires
/// </summary>
public static class PeerIdGenerator
{
    private const string Prefix = "-HY0001-";
    private const string Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private static readonly Random Random = new();

    /// <summary>
    /// Génère un Peer ID sous forme de bytes (20 bytes)
    /// </summary>
    /// <returns>Peer ID en bytes (20 bytes)</returns>
    public static byte[] GenerateBytes()
    {
        return Encoding.ASCII.GetBytes(GenerateString());
    }

    /// <summary>
    /// Génère un Peer ID sous forme de string (20 caractères)
    /// Format: -HY0001- + 12 caractères aléatoires
    /// </summary>
    /// <returns>Peer ID en string (20 caractères)</returns>
    public static string GenerateString()
    {
        var randomPart = new string(Enumerable.Range(0, 12)
            .Select(_ => Chars[Random.Next(Chars.Length)])
            .ToArray());

        return Prefix + randomPart;
    }
}
