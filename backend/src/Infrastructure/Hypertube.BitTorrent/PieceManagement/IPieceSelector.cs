namespace Hypertube.BitTorrent.PieceManagement;

public interface IPieceSelector
{
    int? SelectNextPiece(PieceBitfield ourBitfield, PieceBitfield peerBitfield);
}
