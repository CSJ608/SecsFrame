namespace SecsFrame;

internal interface IHsmsTransportTimer : IDisposable
{
    void Change(TimeSpan dueTime);
}
