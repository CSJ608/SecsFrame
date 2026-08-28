using System.IO;

namespace SecsFrame;

internal sealed class HsmsControlRejectedException : IOException
{
    public HsmsControlRejectedException(
        byte rejectedMessageType,
        HsmsRejectReason reason)
        : base(
            $"The peer rejected HSMS control message SType {rejectedMessageType} " +
            $"with reason {(byte)reason}.")
    {
        RejectedMessageType = rejectedMessageType;
        Reason = reason;
    }

    public byte RejectedMessageType { get; }

    public HsmsRejectReason Reason { get; }
}
