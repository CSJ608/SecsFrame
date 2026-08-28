namespace SecsFrame;

// Internal port: the temporary StreamFrame adapter and the future native
// session-aware StreamFrame adapter must provide exactly these semantics.
internal interface IHsmsTransport : IAsyncDisposable
{
    void Start(CancellationToken cancellationToken);

    IAsyncEnumerable<HsmsTransportEvent> GetEventsAsync(CancellationToken cancellationToken);

    ValueTask SendAsync(
        HsmsTransportSessionId sessionId,
        HsmsFrame frame,
        CancellationToken cancellationToken);

    bool TryCloseSession(
        HsmsTransportSessionId sessionId,
        Exception? error = null);
}
