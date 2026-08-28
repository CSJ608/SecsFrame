namespace SecsFrame;

/// <summary>An immutable event emitted by <see cref="HsmsConnection"/>.</summary>
public sealed class HsmsConnectionEvent
{
    private HsmsConnectionEvent(
        HsmsConnectionEventKind kind,
        HsmsSessionState state,
        HsmsIncomingDataMessage? incomingMessage,
        HsmsFrame? frame,
        Exception? error)
    {
        Kind = kind;
        State = state;
        IncomingMessage = incomingMessage;
        Frame = frame;
        Error = error;
    }

    /// <summary>Gets the event kind.</summary>
    public HsmsConnectionEventKind Kind { get; }

    /// <summary>Gets the session state when the event was produced.</summary>
    public HsmsSessionState State { get; }

    /// <summary>Gets the incoming data message, when applicable.</summary>
    public HsmsIncomingDataMessage? IncomingMessage { get; }

    /// <summary>Gets the control or undecodable data frame, when applicable.</summary>
    public HsmsFrame? Frame { get; }

    /// <summary>Gets the state or decoding error, when applicable.</summary>
    public Exception? Error { get; }

    internal static HsmsConnectionEvent StateChanged(
        HsmsSessionState state,
        Exception? error)
        => new(
            HsmsConnectionEventKind.StateChanged,
            state,
            null,
            null,
            error);

    internal static HsmsConnectionEvent DataMessageReceived(
        HsmsIncomingDataMessage incomingMessage)
        => new(
            HsmsConnectionEventKind.DataMessageReceived,
            HsmsSessionState.Selected,
            incomingMessage,
            null,
            null);

    internal static HsmsConnectionEvent ControlMessageReceived(
        HsmsSessionState state,
        HsmsFrame frame)
        => new(
            HsmsConnectionEventKind.ControlMessageReceived,
            state,
            null,
            frame,
            null);

    internal static HsmsConnectionEvent DataMessageDecodeFailed(
        HsmsFrame frame,
        Exception error)
        => new(
            HsmsConnectionEventKind.DataMessageDecodeFailed,
            HsmsSessionState.Selected,
            null,
            frame,
            error);
}
