namespace SecsFrame;

internal sealed class HsmsSessionTimeoutException : TimeoutException
{
    public HsmsSessionTimeoutException(
        HsmsTimer timer,
        HsmsOperation operation)
        : base($"The HSMS {timer} timer expired.")
    {
        if (timer != HsmsTimer.T6 && timer != HsmsTimer.T7)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timer),
                timer,
                "The session timer must be T6 or T7.");
        }

        Timer = timer;
        Operation = operation;
    }

    public HsmsTimer Timer { get; }

    public HsmsOperation Operation { get; }

    public string TimerName => Timer.ToString();
}
