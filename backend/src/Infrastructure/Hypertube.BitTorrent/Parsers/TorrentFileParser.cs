using System.Security.Cryptography;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using Hypertube.BitTorrent.Models;

namespace Hypertube.BitTorrent.Parsers;

/// <summary>
/// Parser pour les fichiers .torrent
/// </summary>
public class TorrentFileParser
{
    private static readonly BencodeParser Parser = new();

    /// <summary>
    /// Parse un fichier .torrent depuis des bytes
    /// </summary>
    public static TorrentFile Parse(byte[] torrentBytes)
    {
        using var stream = new MemoryStream(torrentBytes);
        return Parse(stream);
    }

    /// <summary>
    /// Parse un fichier .torrent depuis un stream
    /// </summary>
    public static TorrentFile Parse(Stream stream)
    {
        // Parse le fichier torrent en dictionnaire Bencode
        var torrentDict = Parser.Parse<BDictionary>(stream);

        var torrentFile = new TorrentFile();

        // Extraire announce
        if (torrentDict.TryGetValue("announce", out var announceValue) && announceValue is BString announceString)
        {
            torrentFile.Announce = announceString.ToString();
        }

        // Extraire announce-list (optionnel)
        if (torrentDict.TryGetValue("announce-list", out var announceListValue) && announceListValue is BList announceList)
        {
            torrentFile.AnnounceList = new List<List<string>>();
            foreach (var tier in announceList)
            {
                if (tier is BList tierList)
                {
                    var tierUrls = tierList
                        .OfType<BString>()
                        .Select(s => s.ToString())
                        .ToList();
                    torrentFile.AnnounceList.Add(tierUrls);
                }
            }
        }

        // Extraire comment (optionnel)
        if (torrentDict.TryGetValue("comment", out var commentValue) && commentValue is BString commentString)
        {
            torrentFile.Comment = commentString.ToString();
        }

        // Extraire created by (optionnel)
        if (torrentDict.TryGetValue("created by", out var createdByValue) && createdByValue is BString createdByString)
        {
            torrentFile.CreatedBy = createdByString.ToString();
        }

        // Extraire creation date (optionnel)
        if (torrentDict.TryGetValue("creation date", out var creationDateValue) && creationDateValue is BNumber creationDateNumber)
        {
            torrentFile.CreationDate = creationDateNumber.Value;
        }

        // Extraire encoding (optionnel)
        if (torrentDict.TryGetValue("encoding", out var encodingValue) && encodingValue is BString encodingString)
        {
            torrentFile.Encoding = encodingString.ToString();
        }

        // Extraire le dictionnaire "info" et calculer l'InfoHash
        if (torrentDict.TryGetValue("info", out var infoValue) && infoValue is BDictionary infoDict)
        {
            torrentFile.Info = ParseInfo(infoDict);
            torrentFile.InfoHash = CalculateInfoHash(infoDict);
        }

        return torrentFile;
    }

    /// <summary>
    /// Parse le dictionnaire "info"
    /// </summary>
    private static TorrentInfo ParseInfo(BDictionary infoDict)
    {
        var info = new TorrentInfo();

        // Nom
        if (infoDict.TryGetValue("name", out var nameValue) && nameValue is BString nameString)
        {
            info.Name = nameString.ToString();
        }

        // Piece length
        if (infoDict.TryGetValue("piece length", out var pieceLengthValue) && pieceLengthValue is BNumber pieceLengthNumber)
        {
            info.PieceLength = pieceLengthNumber.Value;
        }

        // Pieces (hashes SHA1 concaténés)
        if (infoDict.TryGetValue("pieces", out var piecesValue) && piecesValue is BString piecesString)
        {
            info.Pieces = piecesString.Value.ToArray();
        }

        // Single-file torrent
        if (infoDict.TryGetValue("length", out var lengthValue) && lengthValue is BNumber lengthNumber)
        {
            info.Length = lengthNumber.Value;
        }
        // Multi-file torrent
        else if (infoDict.TryGetValue("files", out var filesValue) && filesValue is BList filesList)
        {
            info.Files = new List<TorrentFileInfo>();

            foreach (var fileItem in filesList)
            {
                if (fileItem is BDictionary fileDict)
                {
                    var fileInfo = new TorrentFileInfo();

                    // Length
                    if (fileDict.TryGetValue("length", out var fileLengthValue) && fileLengthValue is BNumber fileLengthNumber)
                    {
                        fileInfo.Length = fileLengthNumber.Value;
                    }

                    // Path
                    if (fileDict.TryGetValue("path", out var pathValue) && pathValue is BList pathList)
                    {
                        fileInfo.Path = pathList
                            .OfType<BString>()
                            .Select(s => s.ToString())
                            .ToList();
                    }

                    info.Files.Add(fileInfo);
                }
            }
        }

        return info;
    }

    /// <summary>
    /// Calcule l'InfoHash (SHA1 du dictionnaire "info" bencodé)
    /// </summary>
    private static byte[] CalculateInfoHash(BDictionary infoDict)
    {
        // Encoder le dictionnaire "info" en bytes
        using var stream = new MemoryStream();
        infoDict.EncodeTo(stream);
        var infoBytes = stream.ToArray();

        // Calculer le SHA1
        using var sha1 = SHA1.Create();
        return sha1.ComputeHash(infoBytes);
    }
}
