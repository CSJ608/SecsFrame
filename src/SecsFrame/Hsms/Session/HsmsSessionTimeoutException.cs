namespace SecsFrame;

internal sealed class HsmsSessionTimeoutException : TimeoutException
{
    public HsmsSessionTimeoutException(string timerName)
        : base($"The HSMS {timerName} timer expired.")
    {
        TimerName = timerName;
    }

    public string TimerName { get; }
}
