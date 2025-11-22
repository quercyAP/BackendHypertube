namespace Hypertube.BitTorrent.PieceManagement;

public class PieceBitfield
{
    private readonly bool[] _pieces;
    private readonly HashSet<int> _claimedPieces = new(); // Track pieces being downloaded
    private readonly object _lock = new();

    public PieceBitfield(int totalPieces)
    {
        if (totalPieces <= 0)
            throw new ArgumentException("Total pieces must be greater than 0", nameof(totalPieces));

        _pieces = new bool[totalPieces];
    }

    public bool HasPiece(int index)
    {
        if (index < 0 || index >= _pieces.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        lock (_lock)
        {
            return _pieces[index];
        }
    }

    public void SetPiece(int index, bool value)
    {
        if (index < 0 || index >= _pieces.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        lock (_lock)
        {
            _pieces[index] = value;

            if (value)
            {
                _claimedPieces.Remove(index);
            }
        }
    }

    public int CompletedPieces
    {
        get
        {
            lock (_lock)
            {
                return _pieces.Count(p => p);
            }
        }
    }

    public int TotalPieces => _pieces.Length;

    public double Progress
    {
        get
        {
            lock (_lock)
            {
                if (_pieces.Length == 0)
                    return 0;

                return (double)CompletedPieces / _pieces.Length * 100.0;
            }
        }
    }

    public List<int> GetMissingPieces()
    {
        lock (_lock)
        {
            return _pieces
                .Select((has, index) => new { has, index })
                .Where(x => !x.has)
                .Select(x => x.index)
                .ToList();
        }
    }

    public List<int> GetCompletedPieces()
    {
        lock (_lock)
        {
            return _pieces
                .Select((has, index) => new { has, index })
                .Where(x => x.has)
                .Select(x => x.index)
                .ToList();
        }
    }

    public bool IsComplete => CompletedPieces == TotalPieces;

    public bool TryClaimPiece(int index)
    {
        if (index < 0 || index >= _pieces.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        lock (_lock)
        {
            if (_pieces[index] || _claimedPieces.Contains(index))
                return false;

            _claimedPieces.Add(index);
            return true;
        }
    }

    public void ReleasePieceClaim(int index)
    {
        if (index < 0 || index >= _pieces.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        lock (_lock)
        {
            _claimedPieces.Remove(index);
        }
    }

    public bool IsPieceClaimed(int index)
    {
        if (index < 0 || index >= _pieces.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        lock (_lock)
        {
            return _claimedPieces.Contains(index);
        }
    }

    public override string ToString()
    {
        return $"PieceBitfield: {CompletedPieces}/{TotalPieces} pieces ({Progress:F2}%), {_claimedPieces.Count} claimed";
    }
}
