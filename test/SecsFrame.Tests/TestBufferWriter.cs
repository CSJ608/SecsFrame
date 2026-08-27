using StreamFrame;

namespace SecsFrame.Tests;

internal sealed class TestBufferWriter : IWrittenBufferWriter
{
    private byte[] _buffer = new byte[64];

    public int WrittenCount { get; private set; }

    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, WrittenCount);

    public Span<byte> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

    public void Advance(int count)
    {
        if (count < 0 || WrittenCount + count > _buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(count));

        WrittenCount += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(WrittenCount);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(WrittenCount);
    }

    private void EnsureCapacity(int sizeHint)
    {
        sizeHint = Math.Max(sizeHint, 1);
        if (_buffer.Length - WrittenCount >= sizeHint)
            return;

        Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, WrittenCount + sizeHint));
    }
}
