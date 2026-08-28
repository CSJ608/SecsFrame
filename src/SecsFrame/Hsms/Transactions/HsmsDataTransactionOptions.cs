namespace SecsFrame;

internal sealed class HsmsDataTransactionOptions
{
    public HsmsDataTransactionOptions(TimeSpan replyTimeout)
    {
        if (replyTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replyTimeout),
                replyTimeout,
                "The data reply timeout must be positive.");
        }

        ReplyTimeout = replyTimeout;
    }

    public TimeSpan ReplyTimeout { get; }
}
