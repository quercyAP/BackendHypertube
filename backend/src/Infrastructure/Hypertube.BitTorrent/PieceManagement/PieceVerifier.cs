using System.Security.Cryptography;

namespace Hypertube.BitTorrent.PieceManagement;

public class PieceVerifier
{
    private readonly byte[] _piecesHashes;

    public PieceVerifier(byte[] piecesHashes)
    {
        if (piecesHashes == null)
            throw new ArgumentNullException(nameof(piecesHashes));

        if (piecesHashes.Length == 0 || piecesHashes.Length % 20 != 0)
            throw new ArgumentException("Pieces hashes must be a non-empty array with length multiple of 20", nameof(piecesHashes));

        _piecesHashes = piecesHashes;
    }

    public int TotalPieces => _piecesHashes.Length / 20;

    public bool VerifyPiece(int pieceIndex, byte[] pieceData)
    {
        if (pieceIndex < 0 || pieceIndex >= TotalPieces)
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), $"Piece index must be between 0 and {TotalPieces - 1}");

        if (pieceData == null)
            throw new ArgumentNullException(nameof(pieceData));

        using var sha1 = SHA1.Create();
        byte[] actualHash = sha1.ComputeHash(pieceData);

        var expectedHash = GetExpectedHash(pieceIndex);

        return actualHash.SequenceEqual(expectedHash);
    }

    public byte[] GetExpectedHash(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= TotalPieces)
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), $"Piece index must be between 0 and {TotalPieces - 1}");

        var hash = new byte[20];
        Array.Copy(_piecesHashes, pieceIndex * 20, hash, 0, 20);
        return hash;
    }

    public string GetExpectedHashHex(int pieceIndex)
    {
        var hash = GetExpectedHash(pieceIndex);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
