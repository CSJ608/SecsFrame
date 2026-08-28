using System.IO;

namespace SecsFrame;

internal sealed class HsmsControlTransactionInterruptedException : IOException
{
    public HsmsControlTransactionInterruptedException(
        HsmsOperation operation,
        HsmsSessionState state)
        : base(
            $"The HSMS {operation} control transaction was interrupted " +
            $"because the session entered {state}.")
    {
        Operation = operation;
        State = state;
    }

    public HsmsOperation Operation { get; }

    public HsmsSessionState State { get; }
}
