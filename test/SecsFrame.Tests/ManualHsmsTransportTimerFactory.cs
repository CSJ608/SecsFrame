namespace SecsFrame.Tests;

internal sealed class ManualHsmsTransportTimerFactory : IHsmsTransportTimerFactory
{
    public ManualHsmsTransportTimer? Timer { get; private set; }

    public IHsmsTransportTimer Create(Action callback)
    {
        if (Timer is not null)
            throw new InvalidOperationException("Only one timer is expected.");

        Timer = new ManualHsmsTransportTimer(callback);
        return Timer;
    }

    internal sealed class ManualHsmsTransportTimer : IHsmsTransportTimer
    {
        private readonly Action _callback;

        public ManualHsmsTransportTimer(Action callback)
        {
            _callback = callback;
        }

        public TimeSpan DueTime { get; private set; } = Timeout.InfiniteTimeSpan;

        public int ChangeCount { get; private set; }

        public bool IsArmed => DueTime != Timeout.InfiniteTimeSpan;

        public void Change(TimeSpan dueTime)
        {
            DueTime = dueTime;
            ChangeCount++;
        }

        public void Fire()
        {
            if (!IsArmed)
                return;

            DueTime = Timeout.InfiniteTimeSpan;
            _callback();
        }

        public void Dispose()
        {
            DueTime = Timeout.InfiniteTimeSpan;
        }
    }
}
