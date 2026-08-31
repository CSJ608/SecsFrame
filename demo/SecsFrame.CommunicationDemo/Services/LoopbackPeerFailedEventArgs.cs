namespace SecsFrame.CommunicationDemo.Services;

internal sealed class LoopbackPeerFailedEventArgs : EventArgs
{
    public LoopbackPeerFailedEventArgs(Exception error)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public Exception Error { get; }
}
