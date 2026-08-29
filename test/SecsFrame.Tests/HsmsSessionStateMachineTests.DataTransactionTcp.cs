using System.Net;
using System.Threading.Channels;
using StreamFrame;

namespace SecsFrame.Tests;

public sealed partial class HsmsSessionStateMachineTests
{
    private const uint ChurnSystemBytes = 0x01020304;

    [Fact]
    public async Task Active_and_passive_transactions_round_trip_over_real_tcp()
    {
        var (passiveManager, activeManager) = CreateTcpTransactions();
        await using var passive = passiveManager;
        await using var active = activeManager;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var passiveEvents =
            passive.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        await using var activeEvents =
            active.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();

        passive.Start(cancellation.Token);
        active.Start(cancellation.Token);
        _ = await NextSelectedTransactionEventAsync(passiveEvents)
            .ConfigureAwait(true);
        _ = await NextSelectedTransactionEventAsync(activeEvents)
            .ConfigureAwait(true);

        var send = active.SendAsync(
            10,
            new SecsMessage(
                6,
                11,
                true,
                SecsItem.List(
                    SecsItem.Ascii("LOT-42"),
                    SecsItem.List(SecsItem.U1(1), SecsItem.U1(2)))));
        var primary = await NextMatchingTransactionEventAsync(
            passiveEvents,
            item => item.Kind ==
                HsmsDataTransactionEventKind.DataMessageReceived)
            .ConfigureAwait(true);
        await passive.ReplyAsync(
            primary.DataMessage!,
            new SecsMessage(
                6,
                12,
                rootItem: SecsItem.Boolean(true)),
            cancellation.Token).ConfigureAwait(true);

        var secondary = await send.ConfigureAwait(true);
        Assert.NotNull(secondary);
        Assert.Equal(10, secondary.SessionId);
        Assert.Equal(6, secondary.Message.Stream);
        Assert.Equal(12, secondary.Message.Function);
        Assert.Equal(SecsItem.Boolean(true), secondary.Message.RootItem);
    }

    [Fact]
    public async Task Repeated_tcp_session_churn_isolates_transactions_and_stale_timers()
    {
        const int ChurnCount = 3;
        var t3Timers = new SignaledT3TimerFactory();
        var (passiveManager, activeManager) = CreateTcpTransactions(
            t3Timers,
            new FixedSystemBytesProvider(ChurnSystemBytes));
        await using var passive = passiveManager;
        await using var active = activeManager;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var passiveEvents =
            passive.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        await using var activeEvents =
            active.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();

        passive.Start(cancellation.Token);
        active.Start(cancellation.Token);
        var state = new TcpSessionState(
            await NextSelectedTransactionEventAsync(passiveEvents)
                .ConfigureAwait(true),
            await NextSelectedTransactionEventAsync(activeEvents)
                .ConfigureAwait(true));

        for (var cycle = 0; cycle < ChurnCount; cycle++)
        {
            var interrupted = await InterruptTcpTransactionAsync(
                passive,
                active,
                passiveEvents,
                activeEvents,
                t3Timers,
                state,
                cycle,
                cancellation.Token)
                .ConfigureAwait(true);
            await AssertReplacementTcpTransactionAsync(
                passive,
                active,
                passiveEvents,
                t3Timers,
                interrupted,
                cycle,
                cancellation.Token)
                .ConfigureAwait(true);
            state = interrupted.NextState;
        }

        Assert.Equal(HsmsSessionState.Selected, passive.State);
        Assert.Equal(HsmsSessionState.Selected, active.State);
    }

    private static async Task<InterruptedTcpTransaction> InterruptTcpTransactionAsync(
        HsmsDataTransactionManager passive,
        HsmsDataTransactionManager active,
        IAsyncEnumerator<HsmsDataTransactionEvent> passiveEvents,
        IAsyncEnumerator<HsmsDataTransactionEvent> activeEvents,
        SignaledT3TimerFactory t3Timers,
        TcpSessionState state,
        int cycle,
        CancellationToken cancellationToken)
    {
        var send = active.SendAsync(
            10,
            new SecsMessage(6, 11, true, SecsItem.U1((byte)cycle)),
            cancellationToken);
        var primaryEvent = await NextDataMessageEventAsync(passiveEvents)
            .ConfigureAwait(true);
        var primary = primaryEvent.DataMessage!;
        var timer = await t3Timers.WaitForArmedTimerAsync(cancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(state.Passive.SessionId, primaryEvent.SessionId);
        Assert.Equal(ChurnSystemBytes, primary.DataMessage.SystemBytes);
        Assert.True(timer.IsArmed);

        await active.SeparateAsync(cancellationToken).ConfigureAwait(true);
        var error = await Assert.ThrowsAsync<HsmsDataTransactionInterruptedException>(
            () => send).ConfigureAwait(true);
        Assert.Equal(HsmsSessionState.Disconnected, error.State);
        Assert.True(timer.IsDisposed);

        var nextState = new TcpSessionState(
            await NextSelectedTransactionEventAsync(passiveEvents)
                .ConfigureAwait(true),
            await NextSelectedTransactionEventAsync(activeEvents)
                .ConfigureAwait(true));
        Assert.True(nextState.Passive.SessionId.Value > state.Passive.SessionId.Value);
        Assert.True(nextState.Active.SessionId.Value > state.Active.SessionId.Value);

        return new InterruptedTcpTransaction(primary, timer, nextState);
    }

    private static async Task AssertReplacementTcpTransactionAsync(
        HsmsDataTransactionManager passive,
        HsmsDataTransactionManager active,
        IAsyncEnumerator<HsmsDataTransactionEvent> passiveEvents,
        SignaledT3TimerFactory t3Timers,
        InterruptedTcpTransaction interrupted,
        int cycle,
        CancellationToken cancellationToken)
    {
        var staleReply = await Assert.ThrowsAsync<
            HsmsDataTransactionInterruptedException>(
                () => passive.ReplyAsync(
                    interrupted.Primary,
                    new SecsMessage(6, 12),
                    cancellationToken))
            .ConfigureAwait(true);
        Assert.Equal(HsmsSessionState.Selected, staleReply.State);

        var send = active.SendAsync(
            10,
            new SecsMessage(6, 11, true, SecsItem.U1((byte)(cycle + 1))),
            cancellationToken);
        var primaryEvent = await NextDataMessageEventAsync(passiveEvents)
            .ConfigureAwait(true);
        var primary = primaryEvent.DataMessage!;
        var timer = await t3Timers.WaitForArmedTimerAsync(cancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(interrupted.NextState.Passive.SessionId, primaryEvent.SessionId);
        Assert.Equal(ChurnSystemBytes, primary.DataMessage.SystemBytes);

        interrupted.Timer.ForceFire();
        await passive.ReplyAsync(
            primary,
            new SecsMessage(
                6,
                12,
                rootItem: SecsItem.U1((byte)(cycle + 1))),
            cancellationToken).ConfigureAwait(true);
        var secondary = await send.ConfigureAwait(true);

        Assert.NotNull(secondary);
        Assert.Equal(ChurnSystemBytes, secondary.SystemBytes);
        Assert.Equal(
            SecsItem.U1((byte)(cycle + 1)),
            secondary.Message.RootItem);
        Assert.True(timer.IsDisposed);
    }

    private static (
        HsmsDataTransactionManager Passive,
        HsmsDataTransactionManager Active) CreateTcpTransactions(
            IHsmsTransportTimerFactory? activeT3TimerFactory = null,
            IHsmsSystemBytesProvider? activeSystemBytesProvider = null)
    {
        var port = GetFreePort();
        var options = new StreamConnectionOptions
        {
            AcceptRetryDelayMs = 10,
            ConnectRetryDelayMs = 10,
        };
        var passiveTransport = StreamFrameHsmsTransport.Create(
            IPAddress.Loopback,
            port,
            isActive: false,
            TcpTransportOptions,
            options);
        var activeTransport = StreamFrameHsmsTransport.Create(
            IPAddress.Loopback,
            port,
            isActive: true,
            TcpTransportOptions,
            options);
        var passiveSession = new HsmsSessionStateMachine(
            passiveTransport,
            new HsmsSessionOptions(HsmsConnectionMode.Passive, T6, T7));
        var activeSession = new HsmsSessionStateMachine(
            activeTransport,
            new HsmsSessionOptions(HsmsConnectionMode.Active, T6, T7));
        return (
            new HsmsDataTransactionManager(
                passiveSession,
                new HsmsDataTransactionOptions(T3)),
            new HsmsDataTransactionManager(
                activeSession,
                new HsmsDataTransactionOptions(T3),
                timerFactory: activeT3TimerFactory,
                systemBytesProvider: activeSystemBytesProvider));
    }

    private static Task<HsmsDataTransactionEvent> NextSelectedTransactionEventAsync(
        IAsyncEnumerator<HsmsDataTransactionEvent> events)
        => NextMatchingTransactionEventAsync(
            events,
            item => item.Kind ==
                HsmsDataTransactionEventKind.StateChanged &&
                item.State == HsmsSessionState.Selected);

    private static Task<HsmsDataTransactionEvent> NextDataMessageEventAsync(
        IAsyncEnumerator<HsmsDataTransactionEvent> events)
        => NextMatchingTransactionEventAsync(
            events,
            item => item.Kind ==
                HsmsDataTransactionEventKind.DataMessageReceived);

    private readonly record struct TcpSessionState(
        HsmsDataTransactionEvent Passive,
        HsmsDataTransactionEvent Active);

    private readonly record struct InterruptedTcpTransaction(
        HsmsIncomingDataMessage Primary,
        SignaledT3Timer Timer,
        TcpSessionState NextState);

    private sealed class SignaledT3TimerFactory : IHsmsTransportTimerFactory
    {
        private readonly Channel<SignaledT3Timer> _armedTimers =
            Channel.CreateUnbounded<SignaledT3Timer>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });

        public IHsmsTransportTimer Create(Action callback)
            => new SignaledT3Timer(
                callback,
                timer => _armedTimers.Writer.TryWrite(timer));

        public ValueTask<SignaledT3Timer> WaitForArmedTimerAsync(
            CancellationToken cancellationToken)
            => _armedTimers.Reader.ReadAsync(cancellationToken);
    }

    private sealed class SignaledT3Timer : IHsmsTransportTimer
    {
        private readonly Action _callback;
        private readonly Action<SignaledT3Timer> _onArmed;
#if NET9_0_OR_GREATER
        private readonly Lock _sync = new();
#else
        private readonly object _sync = new();
#endif
        private TimeSpan _dueTime = Timeout.InfiniteTimeSpan;

        public SignaledT3Timer(
            Action callback,
            Action<SignaledT3Timer> onArmed)
        {
            _callback = callback;
            _onArmed = onArmed;
        }

        public bool IsArmed
        {
            get
            {
                lock (_sync)
                    return _dueTime != Timeout.InfiniteTimeSpan;
            }
        }

        public bool IsDisposed { get; private set; }

        public void Change(TimeSpan dueTime)
        {
            lock (_sync)
                _dueTime = dueTime;

            _onArmed(this);
        }

        public void ForceFire()
            => _callback();

        public void Dispose()
        {
            lock (_sync)
            {
                _dueTime = Timeout.InfiniteTimeSpan;
                IsDisposed = true;
            }
        }
    }
}
