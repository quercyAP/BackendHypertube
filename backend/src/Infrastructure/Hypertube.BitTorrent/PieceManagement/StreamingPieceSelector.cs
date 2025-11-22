namespace Hypertube.BitTorrent.PieceManagement;

public class StreamingPieceSelector : IPieceSelector
{
    private int _streamingPosition = 0;
    private readonly int _totalPieces;

    private const int InitialBufferPieces = 50;
    private const int BufferAheadPieces = 100;
    private const double BufferReadyThreshold = 0.8; 

    public StreamingPieceSelector(int totalPieces)
    {
        if (totalPieces <= 0)
            throw new ArgumentException("Total pieces must be greater than 0", nameof(totalPieces));

        _totalPieces = totalPieces;
    }

    public int? SelectNextPiece(PieceBitfield ourBitfield, PieceBitfield peerBitfield)
    {
        if (ourBitfield == null)
            throw new ArgumentNullException(nameof(ourBitfield));

        if (peerBitfield == null)
            throw new ArgumentNullException(nameof(peerBitfield));

        var initialBufferEnd = Math.Min(InitialBufferPieces, _totalPieces);
        var initialBuffer = GetMissingPiecesInRange(0, initialBufferEnd, ourBitfield, peerBitfield);
        if (initialBuffer.Count > 0)
        {
            return initialBuffer[0];
        }

        var bufferStart = _streamingPosition;
        var bufferEnd = Math.Min(_streamingPosition + BufferAheadPieces, _totalPieces);
        var bufferPieces = GetMissingPiecesInRange(bufferStart, bufferEnd, ourBitfield, peerBitfield);
        if (bufferPieces.Count > 0)
        {
            return bufferPieces[0];
        }

        if (_streamingPosition > 0)
        {
            var gapPieces = GetMissingPiecesInRange(0, _streamingPosition, ourBitfield, peerBitfield);
            if (gapPieces.Count > 0)
            {
                return gapPieces[0];
            }
        }

        if (bufferEnd < _totalPieces)
        {
            var remaining = GetMissingPiecesInRange(bufferEnd, _totalPieces, ourBitfield, peerBitfield);
            if (remaining.Count > 0)
            {
                return remaining[0];
            }
        }

        return null;
    }

    public void UpdateStreamingPosition(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= _totalPieces)
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), $"Piece index must be between 0 and {_totalPieces - 1}");

        _streamingPosition = pieceIndex;
    }

    public bool HasEnoughBufferForStreaming(PieceBitfield bitfield)
    {
        if (bitfield == null)
            throw new ArgumentNullException(nameof(bitfield));

        int bufferSize = Math.Min(InitialBufferPieces, _totalPieces);
        int completed = CountCompletedPiecesInRange(0, bufferSize, bitfield);

        return completed >= bufferSize * BufferReadyThreshold;
    }

    public double GetBufferSeconds(PieceBitfield bitfield, long pieceLength, double videoBitrate)
    {
        if (bitfield == null)
            throw new ArgumentNullException(nameof(bitfield));

        if (pieceLength <= 0)
            throw new ArgumentException("Piece length must be greater than 0", nameof(pieceLength));

        if (videoBitrate <= 0)
            throw new ArgumentException("Video bitrate must be greater than 0", nameof(videoBitrate));

        var bufferEnd = Math.Min(_streamingPosition + BufferAheadPieces, _totalPieces);
        var completedInBuffer = CountCompletedPiecesInRange(_streamingPosition, bufferEnd, bitfield);

        long bufferedBytes = completedInBuffer * pieceLength;
        double bufferedBits = bufferedBytes * 8.0;
        double bufferSeconds = bufferedBits / videoBitrate;

        return bufferSeconds;
    }

    public int CurrentStreamingPosition => _streamingPosition;

    private List<int> GetMissingPiecesInRange(
        int start,
        int end,
        PieceBitfield ourBitfield,
        PieceBitfield peerBitfield)
    {
        return Enumerable.Range(start, end - start)
            .Where(i => !ourBitfield.HasPiece(i) && peerBitfield.HasPiece(i))
            .ToList();
    }

    private int CountCompletedPiecesInRange(int start, int end, PieceBitfield bitfield)
    {
        return Enumerable.Range(start, end - start)
            .Count(i => bitfield.HasPiece(i));
    }
}
