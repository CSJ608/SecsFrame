using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace SecsFrame;

/// <summary>
/// Provides dynamic SECS-II messaging over one HSMS-SS connection.
/// </summary>
/// <remarks>
/// This type owns its transport, session state machine, and data transaction
/// manager. Call <see cref="Start"/> once and dispose the connection to stop it.
/// </remarks>
public sealed class HsmsConnection : IAsyncDisposable
{
    private readonly HsmsDataTransactionManager _transactions;
    private readonly Channel<HsmsConnectionEvent> _events;
#if NET9_0_OR_GREATER
    private readonly Lock _stateGate = new();
#else
    private readonly object _stateGate = new();
#endif
    private TaskCompletionSource<Exception?> _selectedSignal =
        CreateSelectionSignal();
    private Task? _eventPump;
    private int _state = (int)HsmsSessionState.Disconnected;
    private int _eventReaderClaimed;
    private int _controlMessageObservationReaderClaimed;
    private int _started;
    private int _disposed;

    /// <summary>Creates an HSMS connection without starting network activity.</summary>
    /// <param name="options">The explicit connection and timer settings.</param>
    public HsmsConnection(HsmsConnectionOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _transactions = CreateTransactions(options);
        _events = Channel.CreateUnbounded<HsmsConnectionEvent>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
    }

    /// <summary>Gets the immutable connection settings.</summary>
    public HsmsConnectionOptions Options { get; }

    /// <summary>Gets the latest observed HSMS session state.</summary>
    public HsmsSessionState State
        => (HsmsSessionState)Volatile.Read(ref _state);

    /// <summary>Starts network, session, transaction, and event processing.</summary>
    public void Start()
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The HSMS connection has already been started.");

        try
        {
            _transactions.Start(CancellationToken.None);
            _eventPump = PumpEventsAsync();
        }
        catch (Exception ex)
        {
            FailSelectionWaiters(ex);
            _events.Writer.TryComplete(ex);
            throw;
        }
    }

    /// <summary>
    /// Gets the single-consumer stream of state, incoming-message, control, and
    /// decoding events.
    /// </summary>
    /// <param name="cancellationToken">Cancels only this event-stream read.</param>
    /// <remarks>
    /// Only one event stream can be active at a time. A new stream can be
    /// acquired after the previous enumerator ends or is disposed. Use
    /// <see cref="WaitUntilSelectedAsync"/> for readiness checks without
    /// consuming state events.
    /// </remarks>
    public IAsyncEnumerable<HsmsConnectionEvent> GetEventsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfNotRunning();
        return ReadEventsAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the single-consumer stream of sent and received HSMS control-message
    /// header metadata.
    /// </summary>
    /// <param name="cancellationToken">Cancels only this observation-stream read.</param>
    /// <remarks>
    /// This stream is independent from <see cref="GetEventsAsync"/> and is available
    /// only when <see cref="HsmsConnectionOptions.EnableControlMessageObservation"/>
    /// is enabled. Sent observations are emitted after the entire control frame is
    /// written. Received observations are emitted before protocol handling. The
    /// stream contains no frame body, transport-session generation, raw bytes, or
    /// timestamp. Only one observation stream can be active at a time.
    /// </remarks>
    public IAsyncEnumerable<HsmsControlMessageObservation>
        GetControlMessageObservationsAsync(
            CancellationToken cancellationToken = default)
    {
        ThrowIfNotRunning();
        if (!Options.EnableControlMessageObservation)
        {
            throw new InvalidOperationException(
                "HSMS control-message observation is not enabled in the connection options.");
        }

        return ReadControlMessageObservationsAsync(cancellationToken);
    }

    /// <summary>Waits until this connection next reaches Selected state.</summary>
    /// <param name="cancellationToken">Cancels only this wait operation.</param>
    public Task WaitUntilSelectedAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfNotRunning();
        cancellationToken.ThrowIfCancellationRequested();

        Task<Exception?> selected;
        lock (_stateGate)
        {
            if ((HsmsSessionState)_state == HsmsSessionState.Selected)
                return Task.CompletedTask;

            selected = _selectedSignal.Task;
        }

        return AwaitSelectionAsync(selected, cancellationToken);
    }

    /// <summary>
    /// Sends a dynamic Primary message with the configured protocol Session ID.
    /// </summary>
    /// <param name="primary">The message to send.</param>
    /// <param name="cancellationToken">Cancels this send or open transaction.</param>
    /// <returns>
    /// The matching Secondary when the W-Bit is set; otherwise <see langword="null"/>
    /// after the entire frame is written.
    /// </returns>
    public Task<HsmsDataMessage?> SendAsync(
        SecsMessage primary,
        CancellationToken cancellationToken = default)
    {
        if (primary is null)
            throw new ArgumentNullException(nameof(primary));

        ThrowIfNotRunning();
        return _transactions.SendAsync(
            Options.SessionId,
            primary,
            cancellationToken);
    }

    /// <summary>Replies once to an incoming Primary on its original session.</summary>
    /// <param name="incoming">The incoming message token from the event stream.</param>
    /// <param name="secondary">The Secondary message, with W-Bit clear.</param>
    /// <param name="cancellationToken">Cancels this reply operation.</param>
    public Task ReplyAsync(
        HsmsIncomingDataMessage incoming,
        SecsMessage secondary,
        CancellationToken cancellationToken = default)
    {
        if (incoming is null)
            throw new ArgumentNullException(nameof(incoming));
        if (secondary is null)
            throw new ArgumentNullException(nameof(secondary));

        ThrowIfNotRunning();
        return _transactions.ReplyAsync(
            incoming,
            secondary,
            cancellationToken);
    }

    /// <summary>Sends Linktest Request and waits for its matching response.</summary>
    /// <param name="cancellationToken">Cancels this control command.</param>
    public Task LinktestAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfNotRunning();
        return _transactions.LinktestAsync(cancellationToken);
    }

    /// <summary>Sends Deselect Request and waits for its matching response.</summary>
    /// <param name="cancellationToken">Cancels this control command.</param>
    public Task DeselectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfNotRunning();
        return _transactions.DeselectAsync(cancellationToken);
    }

    /// <summary>Sends Separate Request and closes the selected TCP session.</summary>
    /// <param name="cancellationToken">Cancels this control command.</param>
    public Task SeparateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfNotRunning();
        return _transactions.SeparateAsync(cancellationToken);
    }

    /// <summary>Stops the connection and completes pending operations and events.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        FailSelectionWaiters(new ObjectDisposedException(nameof(HsmsConnection)));
        await _transactions.DisposeAsync().ConfigureAwait(false);
        if (_eventPump is not null)
            await _eventPump.ConfigureAwait(false);
        else
            _events.Writer.TryComplete();
    }

    private static HsmsDataTransactionManager CreateTransactions(
        HsmsConnectionOptions options)
    {
        var isActive = options.ConnectionMode == HsmsConnectionMode.Active;
        var transport = StreamFrameHsmsTransport.Create(
            options.IpAddress,
            options.Port,
            isActive,
            new HsmsTransportOptions(options.T5, options.T8));
        var session = new HsmsSessionStateMachine(
            transport,
            new HsmsSessionOptions(
                options.ConnectionMode,
                options.T6,
                options.T7,
                options.EnableControlMessageObservation));
        return new HsmsDataTransactionManager(
            session,
            new HsmsDataTransactionOptions(options.T3));
    }

    private async Task PumpEventsAsync()
    {
        Exception? completionError = null;
        var events = _transactions.GetEventsAsync(CancellationToken.None)
            .GetAsyncEnumerator();
        try
        {
            while (await events.MoveNextAsync().ConfigureAwait(false))
            {
                var transactionEvent = events.Current;
                if (transactionEvent.Kind ==
                    HsmsDataTransactionEventKind.StateChanged)
                {
                    ApplyState(transactionEvent.State);
                }

                if (!_events.Writer.TryWrite(MapEvent(transactionEvent)))
                    break;
            }

            if (Volatile.Read(ref _disposed) == 0)
            {
                completionError = new IOException(
                    "The internal HSMS event stream ended before the connection was disposed.");
                FailSelectionWaiters(completionError);
            }
        }
        catch (Exception ex)
        {
            completionError = ex;
            FailSelectionWaiters(ex);
        }
        finally
        {
            await events.DisposeAsync().ConfigureAwait(false);
            _events.Writer.TryComplete(completionError);
        }
    }

    private void ApplyState(HsmsSessionState state)
    {
        lock (_stateGate)
        {
            Volatile.Write(ref _state, (int)state);
            if (Volatile.Read(ref _disposed) != 0)
                return;

            if (state == HsmsSessionState.Selected)
            {
                _selectedSignal.TrySetResult(null);
            }
            else if (_selectedSignal.Task.IsCompleted)
            {
                _selectedSignal = CreateSelectionSignal();
            }
        }
    }

    private void FailSelectionWaiters(Exception error)
    {
        lock (_stateGate)
        {
            Volatile.Write(
                ref _state,
                (int)HsmsSessionState.Disconnected);
            if (_selectedSignal.Task.IsCompleted)
                _selectedSignal = CreateSelectionSignal();

            _selectedSignal.TrySetResult(error);
        }
    }

    private async IAsyncEnumerable<HsmsConnectionEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _eventReaderClaimed, 1) != 0)
        {
            throw new InvalidOperationException(
                "The HSMS connection event stream already has a consumer.");
        }

        try
        {
            var reader = _events.Reader;
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var connectionEvent))
                    yield return connectionEvent;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _eventReaderClaimed, 0);
        }
    }

    private async IAsyncEnumerable<HsmsControlMessageObservation>
        ReadControlMessageObservationsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(
            ref _controlMessageObservationReaderClaimed,
            1) != 0)
        {
            throw new InvalidOperationException(
                "The HSMS control-message observation stream already has a consumer.");
        }

        try
        {
            await foreach (var observation in _transactions
                .GetControlMessageObservationsAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return observation;
            }
        }
        finally
        {
            Interlocked.Exchange(
                ref _controlMessageObservationReaderClaimed,
                0);
        }
    }

    private static HsmsConnectionEvent MapEvent(
        HsmsDataTransactionEvent transactionEvent)
        => transactionEvent.Kind switch
        {
            HsmsDataTransactionEventKind.StateChanged =>
                HsmsConnectionEvent.StateChanged(
                    transactionEvent.State,
                    transactionEvent.Error),
            HsmsDataTransactionEventKind.DataMessageReceived =>
                HsmsConnectionEvent.DataMessageReceived(
                    transactionEvent.DataMessage!),
            HsmsDataTransactionEventKind.ControlMessageReceived =>
                HsmsConnectionEvent.ControlMessageReceived(
                    transactionEvent.State,
                    transactionEvent.Frame!),
            HsmsDataTransactionEventKind.DataMessageDecodeFailed =>
                HsmsConnectionEvent.DataMessageDecodeFailed(
                    transactionEvent.Frame!,
                    transactionEvent.Error!),
            _ => throw new ArgumentOutOfRangeException(
                nameof(transactionEvent),
                transactionEvent.Kind,
                "Unknown HSMS data transaction event."),
        };

    private static async Task AwaitSelectionAsync(
        Task<Exception?> operation,
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            var error = await operation.ConfigureAwait(false);
            if (error is not null)
                throw error;

            return;
        }

        var cancellation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state =>
            {
                var pair = (CancellationState)state!;
                pair.Signal.TrySetCanceled(pair.Token);
            },
            new CancellationState(cancellation, cancellationToken));
        var completed = await Task.WhenAny(operation, cancellation.Task)
            .ConfigureAwait(false);
        await completed.ConfigureAwait(false);
        var selectionError = await operation.ConfigureAwait(false);
        if (selectionError is not null)
            throw selectionError;
    }

    private void ThrowIfNotRunning()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _started) == 0)
            throw new InvalidOperationException("The HSMS connection has not been started.");
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(HsmsConnection));
    }

    private static TaskCompletionSource<Exception?> CreateSelectionSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class CancellationState
    {
        public CancellationState(
            TaskCompletionSource<bool> signal,
            CancellationToken token)
        {
            Signal = signal;
            Token = token;
        }

        public TaskCompletionSource<bool> Signal { get; }

        public CancellationToken Token { get; }
    }
}
