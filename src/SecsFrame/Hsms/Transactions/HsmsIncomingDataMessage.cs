namespace SecsFrame;

/// <summary>
/// An incoming HSMS data message bound to the transport session on which it
/// was received.
/// </summary>
public sealed class HsmsIncomingDataMessage
{
    private int _replyStarted;

    internal HsmsIncomingDataMessage(
        object replyOwner,
        HsmsTransportSessionId transportSessionId,
        HsmsDataMessage dataMessage)
    {
        ReplyOwner = replyOwner ?? throw new ArgumentNullException(nameof(replyOwner));
        if (!transportSessionId.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transportSessionId),
                transportSessionId,
                "The transport session identifier must be positive.");
        }

        TransportSessionId = transportSessionId;
        DataMessage = dataMessage ?? throw new ArgumentNullException(nameof(dataMessage));
    }

    /// <summary>Gets the decoded HSMS data message.</summary>
    public HsmsDataMessage DataMessage { get; }

    /// <summary>Gets whether the message requests a secondary reply.</summary>
    public bool ReplyExpected => DataMessage.Message.ReplyExpected;

    private object ReplyOwner { get; }

    internal HsmsTransportSessionId TransportSessionId { get; }

    internal bool IsOwnedBy(object owner)
        => ReferenceEquals(ReplyOwner, owner);

    internal bool TryBeginReply()
        => Interlocked.CompareExchange(ref _replyStarted, 1, 0) == 0;
}
