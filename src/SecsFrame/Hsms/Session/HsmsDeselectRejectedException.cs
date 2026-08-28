using System.IO;

namespace SecsFrame;

internal sealed class HsmsDeselectRejectedException : IOException
{
    public HsmsDeselectRejectedException(HsmsDeselectStatus status)
        : base($"The peer rejected HSMS deselection with status {(byte)status}.")
    {
        Status = status;
    }

    public HsmsDeselectStatus Status { get; }
}
