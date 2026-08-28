namespace SecsFrame;

internal sealed class IncrementingHsmsSystemBytesProvider : IHsmsSystemBytesProvider
{
    private int _value;

    public uint Next()
        => unchecked((uint)Interlocked.Increment(ref _value));
}
