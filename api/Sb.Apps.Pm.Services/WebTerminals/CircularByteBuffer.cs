namespace Sb.Apps.Pm.Services.WebTerminals;

/// <summary>
/// 线程安全环形字节缓冲区。用于 PTY 输出的 in-memory 缓存,
/// 网页重新 attach 时 replay 最近 Capacity 字节的输出。
/// </summary>
public sealed class CircularByteBuffer
{
    private readonly byte[] _buf;
    private int _writePos;
    private long _totalWritten;
    private readonly object _lock = new();

    public int Capacity => _buf.Length;

    public long TotalWritten
    {
        get { lock (_lock) return _totalWritten; }
    }

    public CircularByteBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buf = new byte[capacity];
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return;
        lock (_lock)
        {
            _totalWritten += data.Length;
            if (data.Length >= _buf.Length)
            {
                data[^_buf.Length..].CopyTo(_buf);
                _writePos = 0;
                return;
            }
            int firstChunk = Math.Min(data.Length, _buf.Length - _writePos);
            data[..firstChunk].CopyTo(_buf.AsSpan(_writePos));
            int remaining = data.Length - firstChunk;
            if (remaining > 0)
                data.Slice(firstChunk, remaining).CopyTo(_buf);
            _writePos = (_writePos + data.Length) % _buf.Length;
        }
    }

    /// <summary>取当前 buffer 内容(按写入顺序),最多返回 Capacity 字节。</summary>
    public byte[] Snapshot()
    {
        lock (_lock)
        {
            int validLen = (int)Math.Min(_totalWritten, _buf.Length);
            byte[] result = new byte[validLen];
            if (_totalWritten <= _buf.Length)
            {
                Array.Copy(_buf, 0, result, 0, validLen);
            }
            else
            {
                int firstLen = _buf.Length - _writePos;
                Array.Copy(_buf, _writePos, result, 0, firstLen);
                Array.Copy(_buf, 0, result, firstLen, _writePos);
            }
            return result;
        }
    }
}
