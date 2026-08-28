namespace SecsFrame;

internal sealed class SystemHsmsTransportTimerFactory : IHsmsTransportTimerFactory
{
    public static SystemHsmsTransportTimerFactory Instance { get; } = new();

    private SystemHsmsTransportTimerFactory()
    {
    }

    public IHsmsTransportTimer Create(Action callback)
        => new SystemHsmsTransportTimer(callback);

    private sealed class SystemHsmsTransportTimer : IHsmsTransportTimer
    {
        private readonly Timer _timer;

        public SystemHsmsTransportTimer(Action callback)
        {
            if (callback is null)
                throw new ArgumentNullException(nameof(callback));

            _timer = new Timer(
                static state => ((Action)state!)(),
                callback,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
        }

        public void Change(TimeSpan dueTime)
            => _timer.Change(dueTime, Timeout.InfiniteTimeSpan);

        public void Dispose()
            => _timer.Dispose();
    }
}
