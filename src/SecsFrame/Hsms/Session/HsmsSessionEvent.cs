namespace SecsFrame;

internal readonly record struct HsmsSessionEvent
{
    private HsmsSessionEvent(
        HsmsSessionEventKind kind,
        HsmsTransportSessionId sessionId,
        HsmsSessionState state,
        HsmsFrame? frame,
        Exception? error)
    {
        if (!sessionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(sessionId), sessionId, "The transport session identifier must be positive.");
        var carriesFrame =
            kind == HsmsSessionEventKind.DataMessageReceived ||
            kind == HsmsSessionEventKind.ControlMessageReceived;
        if (carriesFrame && frame is null)
            throw new ArgumentNullException(nameof(frame));
        if (!carriesFrame && frame is not null)
            throw new ArgumentException("A state change cannot carry an HSMS frame.", nameof(frame));
        if (carriesFrame && error is not null)
            throw new ArgumentException("A received frame cannot carry a state error.", nameof(error));

        Kind = kind;
        SessionId = sessionId;
        State = state;
        Frame = frame;
        Error = error;
    }

    public HsmsSessionEventKind Kind { get; }

    public HsmsTransportSessionId SessionId { get; }

    public HsmsSessionState State { get; }

    public HsmsFrame? Frame { get; }

    public Exception? Error { get; }

    public static HsmsSessionEvent StateChanged(
        HsmsTransportSessionId sessionId,
        HsmsSessionState state,
        Exception? error = null)
        => new(HsmsSessionEventKind.StateChanged, sessionId, state, null, error);

    public static HsmsSessionEvent DataMessageReceived(
        HsmsTransportSessionId sessionId,
        HsmsFrame frame)
        => new(HsmsSessionEventKind.DataMessageReceived, sessionId, HsmsSessionState.Selected, frame, null);

    public static HsmsSessionEvent ControlMessageReceived(
        HsmsTransportSessionId sessionId,
        HsmsSessionState state,
        HsmsFrame frame)
        => new(HsmsSessionEventKind.ControlMessageReceived, sessionId, state, frame, null);
}
