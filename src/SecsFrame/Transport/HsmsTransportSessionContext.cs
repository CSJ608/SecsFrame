namespace SecsFrame;

internal sealed class HsmsTransportSessionContext
{
#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private long _lastSessionId;
    private HsmsTransportSessionId _current;

    public HsmsTransportSessionId Open()
    {
        lock (_gate)
        {
            if (_current.IsValid)
                throw new InvalidOperationException($"Transport session {_current.Value} is already active.");
            if (_lastSessionId == long.MaxValue)
                throw new InvalidOperationException("The transport session identifier space is exhausted.");

            _current = new HsmsTransportSessionId(++_lastSessionId);
            return _current;
        }
    }

    public bool TryClose(out HsmsTransportSessionId sessionId)
    {
        lock (_gate)
        {
            sessionId = _current;
            if (!sessionId.IsValid)
                return false;

            _current = default;
            return true;
        }
    }

    public bool IsCurrent(HsmsTransportSessionId sessionId)
    {
        lock (_gate)
            return sessionId.IsValid && sessionId == _current;
    }

    public HsmsTransportSessionId GetCurrent()
    {
        lock (_gate)
        {
            if (!_current.IsValid)
                throw new HsmsTransportSessionExpiredException(default, "No transport session is active.");

            return _current;
        }
    }
}
