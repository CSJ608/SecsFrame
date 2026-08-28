namespace SecsFrame;

internal readonly record struct HsmsDataTransactionEvent
{
    private HsmsDataTransactionEvent(
        HsmsDataTransactionEventKind kind,
        HsmsTransportSessionId sessionId,
        HsmsSessionState state,
        HsmsIncomingDataMessage? dataMessage,
        HsmsFrame? frame,
        Exception? error)
    {
        if (!sessionId.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sessionId),
                sessionId,
                "The transport session identifier must be positive.");
        }

        Kind = kind;
        SessionId = sessionId;
        State = state;
        DataMessage = dataMessage;
        Frame = frame;
        Error = error;
    }

    public HsmsDataTransactionEventKind Kind { get; }

    public HsmsTransportSessionId SessionId { get; }

    public HsmsSessionState State { get; }

    public HsmsIncomingDataMessage? DataMessage { get; }

    public HsmsFrame? Frame { get; }

    public Exception? Error { get; }

    public static HsmsDataTransactionEvent StateChanged(
        HsmsTransportSessionId sessionId,
        HsmsSessionState state,
        Exception? error)
        => new(
            HsmsDataTransactionEventKind.StateChanged,
            sessionId,
            state,
            null,
            null,
            error);

    public static HsmsDataTransactionEvent DataMessageReceived(
        HsmsTransportSessionId sessionId,
        HsmsIncomingDataMessage dataMessage)
        => new(
            HsmsDataTransactionEventKind.DataMessageReceived,
            sessionId,
            HsmsSessionState.Selected,
            dataMessage,
            null,
            null);

    public static HsmsDataTransactionEvent ControlMessageReceived(
        HsmsTransportSessionId sessionId,
        HsmsSessionState state,
        HsmsFrame frame)
        => new(
            HsmsDataTransactionEventKind.ControlMessageReceived,
            sessionId,
            state,
            null,
            frame,
            null);

    public static HsmsDataTransactionEvent DataMessageDecodeFailed(
        HsmsTransportSessionId sessionId,
        HsmsFrame frame,
        Exception error)
        => new(
            HsmsDataTransactionEventKind.DataMessageDecodeFailed,
            sessionId,
            HsmsSessionState.Selected,
            null,
            frame,
            error);
}
