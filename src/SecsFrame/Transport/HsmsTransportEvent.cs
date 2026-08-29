namespace SecsFrame;

internal readonly record struct HsmsTransportEvent
{
    private HsmsTransportEvent(
        HsmsTransportEventKind kind,
        HsmsTransportSessionId sessionId,
        HsmsFrame? frame,
        Exception? error,
        HsmsTransportFaultKind? faultKind,
        ReadOnlyMemory<byte> snapshot,
        long observedByteCount,
        bool isTruncated)
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
        if (!isFault && !snapshot.IsEmpty)
            throw new ArgumentException("Only a transport-fault event can carry a prefix snapshot.", nameof(snapshot));
        if (snapshot.Length > HsmsTransportFaultObservation.MaxSnapshotBytes)
            throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.Length, "The transport-fault prefix snapshot exceeds the supported limit.");
        if (isFault && observedByteCount < snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(observedByteCount), observedByteCount, "The observed byte count cannot be smaller than the retained snapshot.");
        if (isFault && isTruncated != (snapshot.Length < observedByteCount))
            throw new ArgumentException("The truncation flag must equal whether the retained snapshot is shorter than the observed byte count.", nameof(isTruncated));
        if (!isFault && (observedByteCount != 0 || isTruncated))
            throw new ArgumentException("Only a transport-fault event can carry snapshot completeness metadata.", nameof(observedByteCount));

        Kind = kind;
        SessionId = sessionId;
        Frame = frame;
        Error = error;
        FaultKind = faultKind;
        Snapshot = snapshot;
        ObservedByteCount = observedByteCount;
        IsTruncated = isTruncated;
    }

    public HsmsTransportEventKind Kind { get; }

    public HsmsTransportSessionId SessionId { get; }

    public HsmsFrame? Frame { get; }

    public Exception? Error { get; }

    public HsmsTransportFaultKind? FaultKind { get; }

    public ReadOnlyMemory<byte> Snapshot { get; }

    public long ObservedByteCount { get; }

    public bool IsTruncated { get; }

    public static HsmsTransportEvent SessionOpened(HsmsTransportSessionId sessionId)
        => new(HsmsTransportEventKind.SessionOpened, sessionId, null, null, null, default, 0, false);

    public static HsmsTransportEvent FrameReceived(HsmsTransportSessionId sessionId, HsmsFrame frame)
        => new(HsmsTransportEventKind.FrameReceived, sessionId, frame, null, null, default, 0, false);

    public static HsmsTransportEvent TransportFaultObserved(
        HsmsTransportSessionId sessionId,
        HsmsTransportFaultKind faultKind,
        ReadOnlySpan<byte> snapshot,
        long observedByteCount,
        bool isTruncated)
        => new(
            HsmsTransportEventKind.TransportFaultObserved,
            sessionId,
            null,
            null,
            faultKind,
            snapshot.ToArray(),
            observedByteCount,
            isTruncated);

    public static HsmsTransportEvent SessionClosed(
        HsmsTransportSessionId sessionId,
        Exception? error = null)
        => new(HsmsTransportEventKind.SessionClosed, sessionId, null, error, null, default, 0, false);
}
