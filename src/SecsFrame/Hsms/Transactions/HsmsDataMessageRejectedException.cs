namespace SecsFrame;

internal sealed class HsmsDataMessageRejectedException : IOException
{
    public HsmsDataMessageRejectedException(HsmsRejectReason reason)
        : base($"The peer rejected an HSMS data message with reason {(byte)reason}.")
    {
        Reason = reason;
    }

    public HsmsRejectReason Reason { get; }
}
