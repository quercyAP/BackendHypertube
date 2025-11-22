namespace Hypertube.BitTorrent.PieceManagement;

public class SequentialPieceSelector : IPieceSelector
{
    public int? SelectNextPiece(PieceBitfield ourBitfield, PieceBitfield peerBitfield)
    {
        if (ourBitfield == null)
            throw new ArgumentNullException(nameof(ourBitfield));

        if (peerBitfield == null)
            throw new ArgumentNullException(nameof(peerBitfield));

        var missingPieces = ourBitfield.GetMissingPieces();

        foreach (var pieceIndex in missingPieces)
        {
            if (peerBitfield.HasPiece(pieceIndex) && !ourBitfield.IsPieceClaimed(pieceIndex))
            {
                return pieceIndex;
            }
        }

        return null;
    }
}
