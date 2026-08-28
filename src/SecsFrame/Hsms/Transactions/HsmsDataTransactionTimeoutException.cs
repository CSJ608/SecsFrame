namespace SecsFrame;

internal sealed class HsmsDataTransactionTimeoutException : TimeoutException
{
    public HsmsDataTransactionTimeoutException(HsmsDataMessage primary)
        : base(
            $"T3 expired while waiting for the secondary to S{primary.Message.Stream}F{primary.Message.Function} " +
            $"with System Bytes 0x{primary.SystemBytes:X8}.")
    {
        Primary = primary;
    }

    public HsmsDataMessage Primary { get; }
}
