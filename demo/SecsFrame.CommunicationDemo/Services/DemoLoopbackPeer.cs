namespace SecsFrame.CommunicationDemo.Services;

internal sealed class DemoLoopbackPeer : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly HsmsConnection _connection;
    private readonly Task _eventPump;

    private DemoLoopbackPeer(HsmsConnectionOptions options)
    {
        _connection = new HsmsConnection(options);
        _connection.Start();
        _eventPump = PumpEventsAsync();
    }

    public event EventHandler<LoopbackPeerFailedEventArgs>? Failed;

    public static DemoLoopbackPeer Start(HsmsConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ConnectionMode != HsmsConnectionMode.Passive)
        {
            throw new ArgumentException(
                "The demo loopback peer must use Passive mode.",
                nameof(options));
        }

        return new DemoLoopbackPeer(options);
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        await _connection.DisposeAsync().ConfigureAwait(false);
        try
        {
            await _eventPump.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _cancellation.Dispose();
    }

    private async Task PumpEventsAsync()
    {
        try
        {
            await foreach (var item in _connection
                .GetEventsAsync(_cancellation.Token)
                .ConfigureAwait(false))
            {
                if (item.Kind != HsmsConnectionEventKind.DataMessageReceived)
                    continue;

                var incoming = item.IncomingMessage!;
                if (!incoming.ReplyExpected)
                    continue;

                var primary = incoming.DataMessage.Message;
                var function = primary.Function == byte.MaxValue
                    ? primary.Function
                    : (byte)(primary.Function + 1);
                await _connection.ReplyAsync(
                    incoming,
                    new SecsMessage(
                        primary.Stream,
                        function,
                        rootItem: primary.RootItem),
                    _cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Failed?.Invoke(this, new LoopbackPeerFailedEventArgs(error));
        }
    }
}
