using System.Threading.Channels;

namespace SecsFrame.Soak;

internal sealed class HsmsEndpointProbe : IAsyncDisposable
{
    private readonly HsmsConnection _connection;
    private readonly Channel<HsmsIncomingDataMessage> _incoming =
        Channel.CreateUnbounded<HsmsIncomingDataMessage>();
    private readonly CancellationTokenSource _eventCancellation = new();
    private readonly object _selectionGate = new();
    private TaskCompletionSource<long> _nextSelection = CreateSelectionSignal();
    private readonly Task _eventPump;
    private long _selectionGeneration;

    private HsmsEndpointProbe(HsmsConnectionOptions options)
    {
        _connection = new HsmsConnection(options);
        _connection.Start();
        _eventPump = PumpEventsAsync();
    }

    public HsmsConnection Connection => _connection;

    public long SelectionGeneration
    {
        get
        {
            lock (_selectionGate)
                return _selectionGeneration;
        }
    }

    public static HsmsEndpointProbe Start(HsmsConnectionOptions options)
        => new(options);

    public ValueTask<HsmsIncomingDataMessage> NextIncomingAsync(
        CancellationToken cancellationToken)
        => _incoming.Reader.ReadAsync(cancellationToken);

    public async Task<long> WaitForSelectionAfterAsync(
        long previousGeneration,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            Task<long> signal;
            lock (_selectionGate)
            {
                if (_selectionGeneration > previousGeneration)
                    return _selectionGeneration;
                signal = _nextSelection.Task;
            }

            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _eventCancellation.Cancel();
        await _connection.DisposeAsync().ConfigureAwait(false);
        try
        {
            await _eventPump.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _eventCancellation.Dispose();
    }

    private async Task PumpEventsAsync()
    {
        Exception? completionError = null;
        try
        {
            await foreach (var item in _connection
                .GetEventsAsync(_eventCancellation.Token)
                .ConfigureAwait(false))
            {
                ProcessEvent(item);
            }
        }
        catch (OperationCanceledException)
            when (_eventCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            completionError = ex;
            throw;
        }
        finally
        {
            _incoming.Writer.TryComplete(completionError);
        }
    }

    private void ProcessEvent(HsmsConnectionEvent item)
    {
        if (item.Kind == HsmsConnectionEventKind.DataMessageReceived)
        {
            _incoming.Writer.TryWrite(item.IncomingMessage!);
            return;
        }

        if (item.Kind != HsmsConnectionEventKind.StateChanged ||
            item.State != HsmsSessionState.Selected)
        {
            return;
        }

        TaskCompletionSource<long> signal;
        long generation;
        lock (_selectionGate)
        {
            generation = ++_selectionGeneration;
            signal = _nextSelection;
            _nextSelection = CreateSelectionSignal();
        }
        signal.TrySetResult(generation);
    }

    private static TaskCompletionSource<long> CreateSelectionSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
