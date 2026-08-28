namespace SecsFrame;

/// <summary>Provides an incoming message to a dynamic Primary handler.</summary>
public sealed class HsmsPrimaryContext
{
    internal HsmsPrimaryContext(HsmsIncomingDataMessage incomingMessage)
    {
        IncomingMessage = incomingMessage ??
            throw new ArgumentNullException(nameof(incomingMessage));
    }

    /// <summary>Gets the one-time, transport-session-bound incoming token.</summary>
    public HsmsIncomingDataMessage IncomingMessage { get; }

    /// <summary>Gets the decoded HSMS data message.</summary>
    public HsmsDataMessage DataMessage => IncomingMessage.DataMessage;

    /// <summary>Gets the dynamic SECS-II message.</summary>
    public SecsMessage Message => DataMessage.Message;

    /// <summary>Gets the protocol Session ID.</summary>
    public ushort ProtocolSessionId => DataMessage.SessionId;

    /// <summary>Gets the transaction System Bytes.</summary>
    public uint SystemBytes => DataMessage.SystemBytes;

    /// <summary>Gets whether the incoming message requests a Secondary.</summary>
    public bool ReplyExpected => IncomingMessage.ReplyExpected;
}
