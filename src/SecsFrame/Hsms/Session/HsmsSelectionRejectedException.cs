using System.IO;

namespace SecsFrame;

internal sealed class HsmsSelectionRejectedException : IOException
{
    public HsmsSelectionRejectedException(HsmsSelectStatus status)
        : base($"The peer rejected HSMS selection with status {(byte)status}.")
    {
        Status = status;
    }

    public HsmsSelectStatus Status { get; }
}
