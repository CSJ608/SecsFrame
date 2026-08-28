namespace SecsFrame;

internal readonly record struct HsmsTransportEvent
{
    private HsmsTransportEvent(
        HsmsTransportEventKind kind,
        HsmsTransportSessionId sessionId,
        HsmsFrame? frame,
        Exception? error)
    {
        if (!sessionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(sessionId), sessionId, "The transport session identifier must be positive.");
        if (kind == HsmsTransportEventKind.FrameReceived && frame is null)
            throw new ArgumentNullException(nameof(frame));
        if (kind != HsmsTransportEventKind.FrameReceived && frame is not null)
            throw new ArgumentException("Only a frame-received event can carry an HSMS frame.", nameof(frame));
        if (kind != HsmsTransportEventKind.SessionClosed && error is not null)
            throw new ArgumentException("Only a session-closed event can carry an error.", nameof(error));

        Kind = kind;
        SessionId = sessionId;
        Frame = frame;
        Error = error;
    }

    public HsmsTransportEventKind Kind { get; }

    public HsmsTransportSessionId SessionId { get; }

    public HsmsFrame? Frame { get; }

    public Exception? Error { get; }

    public static HsmsTransportEvent SessionOpened(HsmsTransportSessionId sessionId)
        => new(HsmsTransportEventKind.SessionOpened, sessionId, null, null);

    public static HsmsTransportEvent FrameReceived(HsmsTransportSessionId sessionId, HsmsFrame frame)
        => new(HsmsTransportEventKind.FrameReceived, sessionId, frame, null);

    public static HsmsTransportEvent SessionClosed(
        HsmsTransportSessionId sessionId,
        Exception? error = null)
        => new(HsmsTransportEventKind.SessionClosed, sessionId, null, error);
}
