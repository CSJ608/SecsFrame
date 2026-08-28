namespace SecsFrame.Tests;

internal sealed class ManualHsmsTransportTimerFactory : IHsmsTransportTimerFactory
{
    private readonly List<ManualHsmsTransportTimer> _timers = new();

    public IReadOnlyList<ManualHsmsTransportTimer> Timers => _timers;

    public ManualHsmsTransportTimer? Timer => _timers.Count == 0
        ? null
        : _timers[^1];

    public IHsmsTransportTimer Create(Action callback)
    {
        var timer = new ManualHsmsTransportTimer(callback);
        _timers.Add(timer);
        return timer;
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

        public bool IsDisposed { get; private set; }

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

        public void ForceFire()
            => _callback();

        public void Dispose()
        {
            DueTime = Timeout.InfiniteTimeSpan;
            IsDisposed = true;
        }
    }
}
