namespace SecsFrame;

internal sealed class HsmsIncompleteFrameMonitor : IDisposable
{
#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private readonly TimeSpan _timeout;
    private readonly Action _onTimeout;
    private readonly IHsmsTransportTimer _timer;
    private uint _declaredPayloadLength;
    private long _remainingPayloadBytes;
    private int _prefixBytesRead;
    private bool _disposed;

    public HsmsIncompleteFrameMonitor(
        TimeSpan timeout,
        Action onTimeout,
        IHsmsTransportTimerFactory? timerFactory = null)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The incomplete-frame timeout must be positive.");

        _timeout = timeout;
        _onTimeout = onTimeout ?? throw new ArgumentNullException(nameof(onTimeout));
        _timer = (timerFactory ?? SystemHsmsTransportTimerFactory.Instance).Create(OnTimeout);
    }

    public void Observe(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return;

        lock (_gate)
        {
            if (_disposed)
                return;

            var offset = 0;
            while (offset < bytes.Length)
            {
                while (_prefixBytesRead < HsmsFramer.LengthPrefixSize && offset < bytes.Length)
                {
                    _declaredPayloadLength = (_declaredPayloadLength << 8) | bytes[offset++];
                    _prefixBytesRead++;
                }

                if (_prefixBytesRead < HsmsFramer.LengthPrefixSize)
                    break;

                if (_remainingPayloadBytes == 0)
                    _remainingPayloadBytes = _declaredPayloadLength;

                if (_remainingPayloadBytes == 0)
                {
                    ResetFrameState();
                    continue;
                }

                var available = bytes.Length - offset;
                var consumed = (int)Math.Min(_remainingPayloadBytes, available);
                _remainingPayloadBytes -= consumed;
                offset += consumed;

                if (_remainingPayloadBytes == 0)
                    ResetFrameState();
            }

            _timer.Change(IsIncomplete ? _timeout : Timeout.InfiniteTimeSpan);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            ResetFrameState();
            _timer.Change(Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            ResetFrameState();
            _timer.Dispose();
        }
    }

    private bool IsIncomplete => _prefixBytesRead > 0;

    private void OnTimeout()
    {
        var notify = false;
        lock (_gate)
        {
            if (!_disposed && IsIncomplete)
            {
                ResetFrameState();
                _timer.Change(Timeout.InfiniteTimeSpan);
                notify = true;
            }
        }

        if (notify)
            _onTimeout();
    }

    private void ResetFrameState()
    {
        _declaredPayloadLength = 0;
        _remainingPayloadBytes = 0;
        _prefixBytesRead = 0;
    }
}
