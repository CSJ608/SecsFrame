namespace SecsFrame;

internal readonly record struct HsmsTransportEvent
{
    private HsmsTransportEvent(
        HsmsTransportEventKind kind,
        HsmsTransportSessionId sessionId,
        HsmsFrame? frame,
        Exception? error,
        HsmsTransportFaultKind? faultKind,
        ReadOnlyMemory<byte> snapshot)
    {
        if (!sessionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(sessionId), sessionId, "The transport session identifier must be positive.");
        if (kind == HsmsTransportEventKind.FrameReceived && frame is null)
            throw new ArgumentNullException(nameof(frame));
        if (kind != HsmsTransportEventKind.FrameReceived && frame is not null)
            throw new ArgumentException("Only a frame-received event can carry an HSMS frame.", nameof(frame));
        if (kind != HsmsTransportEventKind.SessionClosed && error is not null)
            throw new ArgumentException("Only a session-closed event can carry an error.", nameof(error));
        var isFault = kind == HsmsTransportEventKind.TransportFaultObserved;
        if (isFault && faultKind is null)
            throw new ArgumentNullException(nameof(faultKind));
        if (!isFault && faultKind is not null)
            throw new ArgumentException("Only a transport-fault event can carry a fault kind.", nameof(faultKind));
        if (isFault && snapshot.IsEmpty)
            throw new ArgumentException("A transport-fault event requires a nonempty prefix snapshot.", nameof(snapshot));
        if (!isFault && !snapshot.IsEmpty)
            throw new ArgumentException("Only a transport-fault event can carry a prefix snapshot.", nameof(snapshot));
        if (snapshot.Length > HsmsTransportFaultObservation.MaxSnapshotBytes)
            throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.Length, "The transport-fault prefix snapshot exceeds the supported limit.");

        Kind = kind;
        SessionId = sessionId;
        Frame = frame;
        Error = error;
        FaultKind = faultKind;
        Snapshot = snapshot;
    }

    public HsmsTransportEventKind Kind { get; }

    public HsmsTransportSessionId SessionId { get; }

    public HsmsFrame? Frame { get; }

    public Exception? Error { get; }

    public HsmsTransportFaultKind? FaultKind { get; }

    public ReadOnlyMemory<byte> Snapshot { get; }

    public static HsmsTransportEvent SessionOpened(HsmsTransportSessionId sessionId)
        => new(HsmsTransportEventKind.SessionOpened, sessionId, null, null, null, default);

    public static HsmsTransportEvent FrameReceived(HsmsTransportSessionId sessionId, HsmsFrame frame)
        => new(HsmsTransportEventKind.FrameReceived, sessionId, frame, null, null, default);

    public static HsmsTransportEvent TransportFaultObserved(
        HsmsTransportSessionId sessionId,
        HsmsTransportFaultKind faultKind,
        ReadOnlySpan<byte> snapshot)
        => new(
            HsmsTransportEventKind.TransportFaultObserved,
            sessionId,
            null,
            null,
            faultKind,
            snapshot.ToArray());

    public static HsmsTransportEvent SessionClosed(
        HsmsTransportSessionId sessionId,
        Exception? error = null)
        => new(HsmsTransportEventKind.SessionClosed, sessionId, null, error, null, default);
}
