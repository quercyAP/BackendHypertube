using Hypertube.BitTorrent.Models;
using Microsoft.Win32.SafeHandles;

namespace Hypertube.BitTorrent.FileIO;

public class TorrentFileWriter : IDisposable
{
    private readonly SafeFileHandle _fileHandle;
    private readonly TorrentInfo _torrentInfo;
    private readonly string _filePath;
    private bool _disposed;

    public TorrentFileWriter(string downloadDirectory, TorrentInfo torrentInfo)
    {
        if (string.IsNullOrWhiteSpace(downloadDirectory))
            throw new ArgumentException("Download directory cannot be null or empty", nameof(downloadDirectory));

        _torrentInfo = torrentInfo ?? throw new ArgumentNullException(nameof(torrentInfo));

        Directory.CreateDirectory(downloadDirectory);

        // Store single-file torrents using the original name under the directory provided.
        _filePath = Path.Combine(downloadDirectory, torrentInfo.Name);

        var fileStream = new FileStream(
            _filePath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.ReadWrite,
            bufferSize: 1024 * 1024
        );

        fileStream.SetLength(torrentInfo.TotalLength);

        _fileHandle = fileStream.SafeFileHandle;
    }

    public async Task WritePieceAsync(int pieceIndex, byte[] pieceData)
    {
        if (pieceIndex < 0 || pieceIndex >= _torrentInfo.PieceCount)
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), $"Piece index must be between 0 and {_torrentInfo.PieceCount - 1}");

        if (pieceData == null)
            throw new ArgumentNullException(nameof(pieceData));

        var expectedLength = CalculatePieceLength(pieceIndex);
        if (pieceData.Length != expectedLength)
            throw new ArgumentException($"Piece data length ({pieceData.Length}) does not match expected length ({expectedLength})", nameof(pieceData));

        ObjectDisposedException.ThrowIf(_disposed, this);

        long offset = (long)pieceIndex * _torrentInfo.PieceLength;

        await RandomAccess.WriteAsync(_fileHandle, pieceData.AsMemory(), offset);
    }

    public async Task<byte[]> ReadPieceBlockAsync(int pieceIndex, int begin, int length)
    {
        if (pieceIndex < 0 || pieceIndex >= _torrentInfo.PieceCount)
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), $"Piece index must be between 0 and {_torrentInfo.PieceCount - 1}");

        long pieceLength = CalculatePieceLength(pieceIndex);
        if (begin < 0 || begin >= pieceLength)
            throw new ArgumentOutOfRangeException(nameof(begin), $"Begin offset must be between 0 and {pieceLength - 1} for piece {pieceIndex}");

        if (length <= 0 || begin + length > pieceLength)
            throw new ArgumentOutOfRangeException(nameof(length), $"Block length {length} at offset {begin} exceeds piece boundary {pieceLength}");

        ObjectDisposedException.ThrowIf(_disposed, this);

        long offset = (long)pieceIndex * _torrentInfo.PieceLength + begin;

        var buffer = new byte[length];
        int bytesRead = await RandomAccess.ReadAsync(_fileHandle, buffer.AsMemory(), offset);

        if (bytesRead != length)
            throw new IOException($"Expected to read {length} bytes, but read {bytesRead}");

        return buffer;
    }

    public async Task<byte[]> ReadPieceAsync(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= _torrentInfo.PieceCount)
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), $"Piece index must be between 0 and {_torrentInfo.PieceCount - 1}");

        ObjectDisposedException.ThrowIf(_disposed, this);

        long offset = (long)pieceIndex * _torrentInfo.PieceLength;
        int length = (int)CalculatePieceLength(pieceIndex);

        var buffer = new byte[length];
        int totalRead = await RandomAccess.ReadAsync(_fileHandle, buffer.AsMemory(), offset);

        if (totalRead != length)
            throw new EndOfStreamException($"Expected to read {length} bytes but got {totalRead} for piece {pieceIndex}");

        return buffer;
    }

    public long CalculatePieceLength(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= _torrentInfo.PieceCount)
            throw new ArgumentOutOfRangeException(nameof(pieceIndex));

        if (pieceIndex < _torrentInfo.PieceCount - 1)
        {
            return _torrentInfo.PieceLength;
        }

        var remainder = _torrentInfo.TotalLength % _torrentInfo.PieceLength;
        return remainder > 0 ? remainder : _torrentInfo.PieceLength;
    }

    public string FilePath => _filePath;

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            RandomAccess.FlushToDisk(_fileHandle);
        }
        catch
        {
            // Ignore flush errors during disposal
        }

        _fileHandle?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
