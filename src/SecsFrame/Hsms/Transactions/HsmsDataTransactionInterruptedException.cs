namespace SecsFrame;

internal sealed class HsmsDataTransactionInterruptedException : IOException
{
    public HsmsDataTransactionInterruptedException(HsmsSessionState state)
        : base($"The HSMS data transaction was interrupted because the session entered {state}.")
    {
        State = state;
    }

    public HsmsSessionState State { get; }
}
