namespace SecsFrame;

internal sealed class HsmsT8Monitor : IDisposable
{
#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private readonly TimeSpan _timeout;
    private readonly Action _onTimeout;
    private readonly IHsmsTransportTimerFactory _timerFactory;
    private IHsmsTransportTimer? _timer;
    private uint _declaredPayloadLength;
    private long _remainingPayloadBytes;
    private long _timerGeneration;
    private int _prefixBytesRead;
    private bool _disposed;

    public HsmsT8Monitor(
        TimeSpan timeout,
        Action onTimeout,
        IHsmsTransportTimerFactory? timerFactory = null)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "T8 must be positive.");
        }

        _timeout = timeout;
        _onTimeout =
            onTimeout ?? throw new ArgumentNullException(nameof(onTimeout));
        _timerFactory =
            timerFactory ?? SystemHsmsTransportTimerFactory.Instance;
    }

    public void Observe(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return;

        lock (_gate)
        {
            if (_disposed)
                return;

            ObserveFrames(bytes);
            if (IsIncomplete)
                RestartTimer();
            else
                StopTimer();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            ResetFrameState();
            StopTimer();
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
            StopTimer();
        }
    }

    private bool IsIncomplete => _prefixBytesRead > 0;

    private void ObserveFrames(ReadOnlySpan<byte> bytes)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            while (_prefixBytesRead < HsmsFramer.LengthPrefixSize &&
                offset < bytes.Length)
            {
                _declaredPayloadLength =
                    (_declaredPayloadLength << 8) | bytes[offset++];
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
            var consumed = (int)Math.Min(
                _remainingPayloadBytes,
                available);
            _remainingPayloadBytes -= consumed;
            offset += consumed;

            if (_remainingPayloadBytes == 0)
                ResetFrameState();
        }
    }

    private void RestartTimer()
    {
        StopTimer();
        var generation = _timerGeneration;
        _timer = _timerFactory.Create(
            () => OnTimeout(generation));
        _timer.Change(_timeout);
    }

    private void StopTimer()
    {
        _timerGeneration++;
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTimeout(long generation)
    {
        var notify = false;
        lock (_gate)
        {
            if (!_disposed &&
                generation == _timerGeneration &&
                IsIncomplete)
            {
                ResetFrameState();
                StopTimer();
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
