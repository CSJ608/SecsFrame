using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using StreamFrame;

namespace SecsFrame.Tests;

public sealed class HsmsSessionStateMachineTests
{
    private static readonly TimeSpan T6 = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan T7 = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Active_session_selects_only_after_matching_response()
    {
        var transport = new FakeHsmsTransport();
        var timers = new ManualTimerFactory();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Active,
            timers,
            new FixedSystemBytesProvider(0x01020304));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);

        transport.Open(new HsmsTransportSessionId(1));
        await WaitUntilAsync(() => transport.SendCount == 1).ConfigureAwait(true);

        var selectRequest = transport.GetSentFrame(0);
        Assert.Equal(HsmsSessionState.Selecting, machine.State);
        AssertControlFrame(
            selectRequest,
            HsmsMessageType.SelectRequest,
            0x01020304,
            0);
        Assert.Single(timers.Timers);
        Assert.Equal(T7, timers.Timers[0].DueTime);

        transport.CompleteSend(0);
        await WaitUntilAsync(() => timers.Timers.Count == 2).ConfigureAwait(true);
        Assert.Equal(T6, timers.Timers[1].DueTime);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.SelectResponse,
                    0x01020304)));
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Selected)
            .ConfigureAwait(true);

        Assert.False(timers.Timers[0].IsArmed);
        Assert.False(timers.Timers[1].IsArmed);
        Assert.Equal(0, transport.CloseCount);
    }

    [Fact]
    public async Task T6_starts_after_select_request_write_confirmation()
    {
        var transport = new FakeHsmsTransport();
        var timers = new ManualTimerFactory();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Active,
            timers);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        transport.Open(new HsmsTransportSessionId(1));
        await WaitUntilAsync(() => transport.SendCount == 1).ConfigureAwait(true);

        timers.Timers[0].Fire();
        await WaitUntilAsync(() => transport.CloseCount == 1).ConfigureAwait(true);

        var t7Error = Assert.IsType<HsmsSessionTimeoutException>(transport.LastCloseError);
        Assert.Equal("T7", t7Error.TimerName);
        Assert.Single(timers.Timers);
    }

    [Fact]
    public async Task T6_expiry_closes_selecting_session()
    {
        var transport = new FakeHsmsTransport();
        var timers = new ManualTimerFactory();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Active,
            timers);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        transport.Open(new HsmsTransportSessionId(1));
        await WaitUntilAsync(() => transport.SendCount == 1).ConfigureAwait(true);
        transport.CompleteSend(0);
        await WaitUntilAsync(() => timers.Timers.Count == 2).ConfigureAwait(true);

        timers.Timers[1].Fire();
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Disconnected)
            .ConfigureAwait(true);

        var error = Assert.IsType<HsmsSessionTimeoutException>(transport.LastCloseError);
        Assert.Equal("T6", error.TimerName);
    }

    [Fact]
    public async Task Passive_session_replies_to_select_request_before_becoming_selected()
    {
        var transport = new FakeHsmsTransport();
        var timers = new ManualTimerFactory();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            timers);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        transport.Open(new HsmsTransportSessionId(1));
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Connected)
            .ConfigureAwait(true);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.SelectRequest,
                    0x11223344)));
        await WaitUntilAsync(() => transport.SendCount == 1).ConfigureAwait(true);

        Assert.Equal(HsmsSessionState.Connected, machine.State);
        AssertControlFrame(
            transport.GetSentFrame(0),
            HsmsMessageType.SelectResponse,
            0x11223344,
            (byte)HsmsSelectStatus.Success);

        transport.CompleteSend(0);
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Selected)
            .ConfigureAwait(true);
        Assert.False(timers.Timers[0].IsArmed);
    }

    [Fact]
    public async Task Simultaneous_select_is_completed_without_an_extra_session()
    {
        var transport = new FakeHsmsTransport();
        var timers = new ManualTimerFactory();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Active,
            timers,
            new FixedSystemBytesProvider(0x10203040));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        transport.Open(new HsmsTransportSessionId(1));
        await WaitUntilAsync(() => transport.SendCount == 1).ConfigureAwait(true);
        transport.CompleteSend(0);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.SelectRequest,
                    0xAABBCCDD)));
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        transport.CompleteSend(1);
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Selected)
            .ConfigureAwait(true);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.SelectResponse,
                    0x10203040)));
        await WaitUntilAsync(() => timers.Timers.All(static timer => !timer.IsArmed))
            .ConfigureAwait(true);

        Assert.Equal(HsmsSessionState.Selected, machine.State);
        Assert.Equal(0, transport.CloseCount);
        AssertControlFrame(
            transport.GetSentFrame(1),
            HsmsMessageType.SelectResponse,
            0xAABBCCDD,
            (byte)HsmsSelectStatus.Success);
    }

    [Fact]
    public async Task Select_request_for_selected_session_returns_already_selected()
    {
        var transport = new FakeHsmsTransport();
        var timers = new ManualTimerFactory();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            timers);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        await SelectPassiveAsync(transport, machine).ConfigureAwait(true);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.SelectRequest,
                    0x55667788)));
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);

        AssertControlFrame(
            transport.GetSentFrame(1),
            HsmsMessageType.SelectResponse,
            0x55667788,
            (byte)HsmsSelectStatus.AlreadySelected);
        transport.CompleteSend(1);
        Assert.Equal(HsmsSessionState.Selected, machine.State);
    }

    [Fact]
    public async Task Rejected_select_returns_to_connected_until_T7_expires()
    {
        var transport = new FakeHsmsTransport();
        var timers = new ManualTimerFactory();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Active,
            timers,
            new FixedSystemBytesProvider(7));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = machine.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        machine.Start(cancellation.Token);
        transport.Open(new HsmsTransportSessionId(1));
        await WaitUntilAsync(() => transport.SendCount == 1).ConfigureAwait(true);
        transport.CompleteSend(0);
        await WaitUntilAsync(() => timers.Timers.Count == 2).ConfigureAwait(true);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.SelectResponse,
                    7,
                    (byte)HsmsSelectStatus.NotReady)));
        var rejected = await NextMatchingAsync(
            events,
            static sessionEvent => sessionEvent.Error is HsmsSelectionRejectedException)
            .ConfigureAwait(true);

        var error = Assert.IsType<HsmsSelectionRejectedException>(rejected.Error);
        Assert.Equal(HsmsSelectStatus.NotReady, error.Status);
        Assert.Equal(HsmsSessionState.Connected, machine.State);
        Assert.True(timers.Timers[0].IsArmed);
        Assert.False(timers.Timers[1].IsArmed);

        timers.Timers[0].Fire();
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Disconnected)
            .ConfigureAwait(true);
        Assert.Equal("T7", Assert.IsType<HsmsSessionTimeoutException>(
            transport.LastCloseError).TimerName);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Unexpected_select_response_closes_session_as_protocol_error()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Active,
            new ManualTimerFactory(),
            new FixedSystemBytesProvider(10));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        transport.Open(new HsmsTransportSessionId(1));
        await WaitUntilAsync(() => transport.SendCount == 1).ConfigureAwait(true);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.SelectResponse,
                    11)));
        await WaitUntilAsync(() => transport.CloseCount == 1).ConfigureAwait(true);

        Assert.IsType<HsmsProtocolException>(transport.LastCloseError);
        Assert.Equal(HsmsSessionState.Disconnected, machine.State);
    }

    [Fact]
    public async Task Data_is_rejected_before_selection_and_forwarded_after_selection()
    {
        var rejectedTransport = new FakeHsmsTransport();
        await using (var rejectedMachine = CreateMachine(
            rejectedTransport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory()))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            rejectedMachine.Start(cancellation.Token);
            rejectedTransport.Open(new HsmsTransportSessionId(1));
            rejectedTransport.Receive(CreateDataFrame());
            await WaitUntilAsync(() => rejectedTransport.CloseCount == 1)
                .ConfigureAwait(true);
            Assert.IsType<HsmsProtocolException>(rejectedTransport.LastCloseError);
        }

        var selectedTransport = new FakeHsmsTransport();
        var selectedMachine = CreateMachine(
            selectedTransport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory());
        await using var selectedMachineScope =
            selectedMachine.ConfigureAwait(true);
        using var selectedCancellation =
            new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = selectedMachine.GetEventsAsync(selectedCancellation.Token)
            .GetAsyncEnumerator();
        selectedMachine.Start(selectedCancellation.Token);
        await SelectPassiveAsync(selectedTransport, selectedMachine)
            .ConfigureAwait(true);
        var dataFrame = CreateDataFrame();

        selectedTransport.Receive(dataFrame);
        var received = await NextMatchingAsync(
            events,
            static sessionEvent =>
                sessionEvent.Kind == HsmsSessionEventKind.DataMessageReceived)
            .ConfigureAwait(true);

        Assert.Same(dataFrame, received.Frame);
        Assert.Equal(HsmsSessionState.Selected, selectedMachine.State);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Invalid_control_header_closes_session_as_protocol_error()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        transport.Open(new HsmsTransportSessionId(1));
        var invalidHeader =
            HsmsMessageHeader.CreateControl(HsmsMessageType.SelectRequest, 1) with
            {
                SessionId = 1,
            };

        transport.Receive(new HsmsFrame(invalidHeader));
        await WaitUntilAsync(() => transport.CloseCount == 1).ConfigureAwait(true);

        Assert.IsType<HsmsProtocolException>(transport.LastCloseError);
    }

    [Fact]
    public async Task Local_separate_waits_for_write_then_closes_session()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory(),
            new FixedSystemBytesProvider(0x12345678));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        await SelectPassiveAsync(transport, machine).ConfigureAwait(true);

        var separate = machine.SeparateAsync(cancellation.Token);
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);

        Assert.False(separate.IsCompleted);
        AssertControlFrame(
            transport.GetSentFrame(1),
            HsmsMessageType.SeparateRequest,
            0x12345678,
            0);

        transport.CompleteSend(1);
        await separate.ConfigureAwait(true);
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Disconnected)
            .ConfigureAwait(true);
        Assert.Null(transport.LastCloseError);
    }

    [Fact]
    public async Task Incoming_separate_closes_selected_session()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        await SelectPassiveAsync(transport, machine).ConfigureAwait(true);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.SeparateRequest,
                    99)));
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Disconnected)
            .ConfigureAwait(true);

        Assert.Null(transport.LastCloseError);
    }

    [Fact]
    public async Task Old_timer_callback_cannot_close_replacement_session()
    {
        var transport = new FakeHsmsTransport();
        var timers = new ManualTimerFactory();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            timers);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        transport.Open(new HsmsTransportSessionId(1));
        await WaitUntilAsync(() => timers.Timers.Count == 1).ConfigureAwait(true);
        var oldTimer = timers.Timers[0];
        transport.CloseCurrent();
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Disconnected)
            .ConfigureAwait(true);
        transport.Open(new HsmsTransportSessionId(2));
        await WaitUntilAsync(() => timers.Timers.Count == 2).ConfigureAwait(true);

        oldTimer.ForceFire();
        timers.Timers[1].Fire();
        await WaitUntilAsync(() => transport.CloseCount == 2).ConfigureAwait(true);

        Assert.Equal(new HsmsTransportSessionId(2), transport.LastClosedSessionId);
        Assert.Equal("T7", Assert.IsType<HsmsSessionTimeoutException>(
            transport.LastCloseError).TimerName);
    }

    [Fact]
    public async Task Transport_event_stream_completion_disconnects_current_session()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = machine.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        machine.Start(cancellation.Token);
        transport.Open(new HsmsTransportSessionId(1));
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Connected)
            .ConfigureAwait(true);

        transport.CompleteEventStream();
        var disconnected = await NextMatchingAsync(
            events,
            static sessionEvent =>
                sessionEvent.State == HsmsSessionState.Disconnected)
            .ConfigureAwait(true);

        Assert.IsType<IOException>(disconnected.Error);
        Assert.Equal(HsmsSessionState.Disconnected, machine.State);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Active_and_passive_streamframe_transports_select_over_tcp()
    {
        var port = GetFreePort();
        var connectionOptions = new StreamConnectionOptions
        {
            AcceptRetryDelayMs = 10,
            ConnectRetryDelayMs = 10,
        };
        var passiveTransport = StreamFrameHsmsTransport.Create(
            IPAddress.Loopback,
            port,
            isActive: false,
            T6,
            connectionOptions);
        var activeTransport = StreamFrameHsmsTransport.Create(
            IPAddress.Loopback,
            port,
            isActive: true,
            T6,
            connectionOptions);
        await using var passive = new HsmsSessionStateMachine(
            passiveTransport,
            new HsmsSessionOptions(HsmsConnectionMode.Passive, T6, T7));
        await using var active = new HsmsSessionStateMachine(
            activeTransport,
            new HsmsSessionOptions(HsmsConnectionMode.Active, T6, T7));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        passive.Start(cancellation.Token);
        active.Start(cancellation.Token);
        await WaitUntilAsync(
            () => passive.State == HsmsSessionState.Selected &&
                active.State == HsmsSessionState.Selected,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true);

        Assert.Equal(HsmsSessionState.Selected, passive.State);
        Assert.Equal(HsmsSessionState.Selected, active.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nonpositive_session_timeouts_are_rejected(int seconds)
    {
        var timeout = TimeSpan.FromSeconds(seconds);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HsmsSessionOptions(
                HsmsConnectionMode.Active,
                timeout,
                T7));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HsmsSessionOptions(
                HsmsConnectionMode.Active,
                T6,
                timeout));
    }

    [Fact]
    public void Undefined_connection_mode_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HsmsSessionOptions(
                (HsmsConnectionMode)int.MaxValue,
                T6,
                T7));
    }

    private static HsmsSessionStateMachine CreateMachine(
        FakeHsmsTransport transport,
        HsmsConnectionMode mode,
        ManualTimerFactory timers,
        IHsmsSystemBytesProvider? systemBytesProvider = null)
        => new(
            transport,
            new HsmsSessionOptions(mode, T6, T7),
            timers,
            systemBytesProvider);

    private static async Task SelectPassiveAsync(
        FakeHsmsTransport transport,
        HsmsSessionStateMachine machine)
    {
        transport.Open(new HsmsTransportSessionId(1));
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Connected)
            .ConfigureAwait(true);
        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.SelectRequest,
                    1)));
        await WaitUntilAsync(() => transport.SendCount == 1).ConfigureAwait(true);
        transport.CompleteSend(0);
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Selected)
            .ConfigureAwait(true);
    }

    private static void AssertControlFrame(
        HsmsFrame frame,
        HsmsMessageType messageType,
        uint systemBytes,
        byte status)
    {
        Assert.Equal(ushort.MaxValue, frame.Header.SessionId);
        Assert.Equal(0, frame.Header.HeaderByte2);
        Assert.Equal(status, frame.Header.HeaderByte3);
        Assert.Equal(0, frame.Header.PresentationType);
        Assert.Equal(messageType, frame.Header.MessageType);
        Assert.Equal(systemBytes, frame.Header.SystemBytes);
        Assert.True(frame.Body.IsEmpty);
    }

    private static HsmsFrame CreateDataFrame()
        => new(
            HsmsMessageHeader.CreateData(
                sessionId: 1,
                stream: 1,
                function: 1,
                replyExpected: false,
                systemBytes: 1));

    private static async Task<HsmsSessionEvent> NextMatchingAsync(
        IAsyncEnumerator<HsmsSessionEvent> events,
        Func<HsmsSessionEvent, bool> predicate)
    {
        while (await events.MoveNextAsync().ConfigureAwait(true))
        {
            if (predicate(events.Current))
                return events.Current;
        }

        Assert.Fail("The expected HSMS session event was not received.");
        return default;
    }

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        TimeSpan? timeout = null)
    {
        using var cancellation = new CancellationTokenSource(
            timeout ?? TimeSpan.FromSeconds(5));
        while (!predicate())
            await Task.Delay(1, cancellation.Token).ConfigureAwait(true);
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FixedSystemBytesProvider : IHsmsSystemBytesProvider
    {
        private readonly uint _value;

        public FixedSystemBytesProvider(uint value)
        {
            _value = value;
        }

        public uint Next() => _value;
    }

    private sealed class ManualTimerFactory : IHsmsTransportTimerFactory
    {
#if NET9_0_OR_GREATER
        private readonly Lock _sync = new();
#else
        private readonly object _sync = new();
#endif
        private readonly List<ManualTimer> _timers = new();

        public IReadOnlyList<ManualTimer> Timers
        {
            get
            {
                lock (_sync)
                    return _timers.ToArray();
            }
        }

        public IHsmsTransportTimer Create(Action callback)
        {
            var timer = new ManualTimer(callback);
            lock (_sync)
                _timers.Add(timer);

            return timer;
        }
    }

    private sealed class ManualTimer : IHsmsTransportTimer
    {
        private readonly Action _callback;
#if NET9_0_OR_GREATER
        private readonly Lock _sync = new();
#else
        private readonly object _sync = new();
#endif
        private TimeSpan _dueTime = Timeout.InfiniteTimeSpan;

        public ManualTimer(Action callback)
        {
            _callback = callback;
        }

        public TimeSpan DueTime
        {
            get
            {
                lock (_sync)
                    return _dueTime;
            }
        }

        public bool IsArmed => DueTime != Timeout.InfiniteTimeSpan;

        public void Change(TimeSpan dueTime)
        {
            lock (_sync)
                _dueTime = dueTime;
        }

        public void Fire()
        {
            lock (_sync)
            {
                if (_dueTime == Timeout.InfiniteTimeSpan)
                    return;

                _dueTime = Timeout.InfiniteTimeSpan;
            }

            _callback();
        }

        public void ForceFire() => _callback();

        public void Dispose()
        {
            lock (_sync)
                _dueTime = Timeout.InfiniteTimeSpan;
        }
    }

    private sealed class FakeHsmsTransport : IHsmsTransport
    {
#if NET9_0_OR_GREATER
        private readonly Lock _sync = new();
#else
        private readonly object _sync = new();
#endif
        private readonly Channel<HsmsTransportEvent> _events =
            Channel.CreateUnbounded<HsmsTransportEvent>();
        private readonly List<PendingSend> _sends = new();
        private HsmsTransportSessionId _currentSessionId;
        private HsmsTransportSessionId _lastClosedSessionId;
        private Exception? _lastCloseError;
        private int _closeCount;

        public int SendCount
        {
            get
            {
                lock (_sync)
                    return _sends.Count;
            }
        }

        public int CloseCount => Volatile.Read(ref _closeCount);

        public HsmsTransportSessionId LastClosedSessionId
        {
            get
            {
                lock (_sync)
                    return _lastClosedSessionId;
            }
        }

        public Exception? LastCloseError
        {
            get
            {
                lock (_sync)
                    return _lastCloseError;
            }
        }

        public void Start(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        public async IAsyncEnumerable<HsmsTransportEvent> GetEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (await _events.Reader.WaitToReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                while (_events.Reader.TryRead(out var transportEvent))
                    yield return transportEvent;
            }
        }

        public async ValueTask SendAsync(
            HsmsTransportSessionId sessionId,
            HsmsFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = new PendingSend(sessionId, frame);
            lock (_sync)
            {
                if (sessionId != _currentSessionId)
                    throw new HsmsTransportSessionExpiredException(sessionId);

                _sends.Add(pending);
            }

            using (cancellationToken.Register(
                static state =>
                    ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
                pending.Completion))
            {
                await pending.Completion.Task.ConfigureAwait(false);
            }
        }

        public bool TryCloseSession(
            HsmsTransportSessionId sessionId,
            Exception? error = null)
        {
            lock (_sync)
            {
                if (sessionId != _currentSessionId)
                    return false;

                _currentSessionId = default;
                _lastClosedSessionId = sessionId;
                _lastCloseError = error;
                _closeCount++;
            }

            _events.Writer.TryWrite(
                HsmsTransportEvent.SessionClosed(sessionId, error));
            return true;
        }

        public ValueTask DisposeAsync()
        {
            PendingSend[] sends;
            lock (_sync)
                sends = _sends.ToArray();

            foreach (var send in sends)
                send.Completion.TrySetCanceled();

            _events.Writer.TryComplete();
            return default;
        }

        public void Open(HsmsTransportSessionId sessionId)
        {
            lock (_sync)
                _currentSessionId = sessionId;

            _events.Writer.TryWrite(HsmsTransportEvent.SessionOpened(sessionId));
        }

        public void Receive(HsmsFrame frame)
        {
            HsmsTransportSessionId sessionId;
            lock (_sync)
                sessionId = _currentSessionId;

            _events.Writer.TryWrite(
                HsmsTransportEvent.FrameReceived(sessionId, frame));
        }

        public void CompleteSend(int index)
        {
            PendingSend pending;
            lock (_sync)
                pending = _sends[index];

            pending.Completion.TrySetResult(true);
        }

        public HsmsFrame GetSentFrame(int index)
        {
            lock (_sync)
                return _sends[index].Frame;
        }

        public void CloseCurrent()
        {
            HsmsTransportSessionId sessionId;
            lock (_sync)
                sessionId = _currentSessionId;

            TryCloseSession(sessionId);
        }

        public void CompleteEventStream() => _events.Writer.TryComplete();

        private sealed class PendingSend
        {
            public PendingSend(
                HsmsTransportSessionId sessionId,
                HsmsFrame frame)
            {
                SessionId = sessionId;
                Frame = frame;
                Completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public HsmsTransportSessionId SessionId { get; }

            public HsmsFrame Frame { get; }

            public TaskCompletionSource<bool> Completion { get; }
        }
    }
}
