namespace SecsFrame;

internal interface IHsmsTransportTimerFactory
{
    IHsmsTransportTimer Create(Action callback);
}
