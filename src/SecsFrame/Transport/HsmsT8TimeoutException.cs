namespace SecsFrame;

internal sealed class HsmsT8TimeoutException : TimeoutException
{
    public HsmsT8TimeoutException(HsmsTransportSessionId sessionId)
        : base(
            $"T8 expired while transport session {sessionId.Value} was receiving an incomplete HSMS message.")
    {
        SessionId = sessionId;
    }

    public HsmsTransportSessionId SessionId { get; }
}
