namespace SecsFrame;

internal sealed class HsmsIncomingDataMessage
{
    private int _replyStarted;

    public HsmsIncomingDataMessage(
        HsmsTransportSessionId transportSessionId,
        HsmsDataMessage dataMessage)
    {
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

    public HsmsDataMessage DataMessage { get; }

    internal HsmsTransportSessionId TransportSessionId { get; }

    internal bool TryBeginReply()
        => Interlocked.CompareExchange(ref _replyStarted, 1, 0) == 0;
}
