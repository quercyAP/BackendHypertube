using BencodeNET.Objects;
using BencodeNET.Parsing;

namespace Hypertube.BitTorrent.Bencode;

/// <summary>
/// Helper class pour faciliter l'utilisation de BencodeNET
/// </summary>
public static class BencodeHelper
{
    private static readonly BencodeParser Parser = new();

    /// <summary>
    /// Parse des bytes Bencode en objet IBObject
    /// </summary>
    public static IBObject Parse(byte[] data)
    {
        using var stream = new MemoryStream(data);
        return Parser.Parse(stream);
    }

    /// <summary>
    /// Parse un stream Bencode en objet IBObject
    /// </summary>
    public static IBObject Parse(Stream stream)
    {
        return Parser.Parse(stream);
    }

    /// <summary>
    /// Encode un objet IBObject en bytes
    /// </summary>
    public static byte[] Encode(IBObject obj)
    {
        using var stream = new MemoryStream();
        obj.EncodeTo(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Obtenir la valeur d'une clé depuis un dictionnaire Bencode
    /// </summary>
    public static T? GetValue<T>(BDictionary dictionary, string key) where T : IBObject
    {
        if (dictionary.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }

    /// <summary>
    /// Obtenir une string depuis un BencodeString
    /// </summary>
    public static string GetString(BString bString)
    {
        return bString.ToString();
    }

    /// <summary>
    /// Obtenir un long depuis un BencodeNumber
    /// </summary>
    public static long GetNumber(BNumber bNumber)
    {
        return bNumber.Value;
    }
}
