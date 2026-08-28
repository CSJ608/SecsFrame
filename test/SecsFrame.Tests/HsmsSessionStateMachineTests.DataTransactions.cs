namespace SecsFrame.Tests;

public sealed partial class HsmsSessionStateMachineTests
{
    private static readonly TimeSpan T3 = TimeSpan.FromSeconds(45);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nonpositive_T3_is_rejected(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HsmsDataTransactionOptions(
                TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public async Task Data_send_rejects_control_frames_and_nonzero_PType()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        var nonzeroPType = HsmsMessageHeader.CreateData(1, 1, 1, false, 1) with
        {
            PresentationType = 1,
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => machine.SendDataAsync(
                new HsmsFrame(
                    HsmsMessageHeader.CreateControl(
                        HsmsMessageType.LinktestRequest,
                        1)))).ConfigureAwait(true);
        await Assert.ThrowsAsync<HsmsProtocolException>(
            () => machine.SendDataAsync(new HsmsFrame(nonzeroPType)))
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task Data_send_requires_a_selected_session()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        transport.Open(new HsmsTransportSessionId(1));
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Connected)
            .ConfigureAwait(true);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => machine.SendDataAsync(CreateDataFrame()))
            .ConfigureAwait(true);

        Assert.Contains("selected", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, transport.SendCount);
    }

    [Fact]
    public async Task Data_send_completes_only_after_the_full_frame_is_written()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        await SelectPassiveAsync(transport, machine).ConfigureAwait(true);

        var frame = CreateDataFrame();
        var send = machine.SendDataAsync(frame);
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);

        Assert.False(send.IsCompleted);
        Assert.Same(frame, transport.GetSentFrame(1));

        transport.CompleteSend(1);
        await send.ConfigureAwait(true);
    }

    [Fact]
    public async Task Pending_data_send_is_not_replayed_on_a_replacement_session()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        await SelectPassiveAsync(transport, machine).ConfigureAwait(true);

        var send = machine.SendDataAsync(CreateDataFrame());
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        transport.CloseCurrent();

        await Assert.ThrowsAsync<HsmsTransportSessionExpiredException>(
            () => send).ConfigureAwait(true);
        transport.Open(new HsmsTransportSessionId(2));
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Connected)
            .ConfigureAwait(true);
        Assert.Equal(2, transport.SendCount);
    }

    [Fact]
    public async Task No_reply_primary_completes_after_write_without_starting_T3()
    {
        var transport = new FakeHsmsTransport();
        var t3Timers = new ManualTimerFactory();
        var (session, transactions) = CreateTransactions(
            transport,
            t3Timers,
            new SequenceSystemBytesProvider(0x01020304));
        await using var manager = transactions;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        manager.Start(cancellation.Token);
        await using var events = manager.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        await SelectTransactionsPassiveAsync(transport, session, events)
            .ConfigureAwait(true);

        var send = manager.SendAsync(
            7,
            new SecsMessage(6, 11, rootItem: SecsItem.Ascii("READY")));
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);

        Assert.False(send.IsCompleted);
        Assert.Empty(t3Timers.Timers);
        Assert.Equal(0x01020304u, transport.GetSentFrame(1).Header.SystemBytes);

        transport.CompleteSend(1);
        Assert.Null(await send.ConfigureAwait(true));
        Assert.Empty(t3Timers.Timers);
    }

    [Fact]
    public async Task Reply_expected_primary_starts_T3_after_write_and_round_trips_secondary()
    {
        var transport = new FakeHsmsTransport();
        var t3Timers = new ManualTimerFactory();
        var (session, transactions) = CreateTransactions(
            transport,
            t3Timers,
            new SequenceSystemBytesProvider(0x11223344));
        await using var manager = transactions;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        manager.Start(cancellation.Token);
        await using var events = manager.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        await SelectTransactionsPassiveAsync(transport, session, events)
            .ConfigureAwait(true);

        var send = manager.SendAsync(
            3,
            new SecsMessage(1, 1, true, SecsItem.List(SecsItem.Ascii("MDLN"))));
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        Assert.Empty(t3Timers.Timers);

        var primary = transport.GetSentFrame(1);
        transport.CompleteSend(1);
        await WaitUntilAsync(
            () => t3Timers.Timers.Count == 1 && t3Timers.Timers[0].IsArmed)
            .ConfigureAwait(true);

        var secondary = new HsmsDataMessage(
            3,
            primary.Header.SystemBytes,
            new SecsMessage(
                1,
                2,
                rootItem: SecsItem.List(
                    SecsItem.Ascii("MODEL"),
                    SecsItem.U4(uint.MaxValue))));
        transport.Receive(new HsmsDataMessageCodec().EncodeFrame(secondary));

        var actual = await send.ConfigureAwait(true);
        Assert.NotNull(actual);
        AssertDataMessageEqual(secondary, actual);
        Assert.False(t3Timers.Timers[0].IsArmed);
    }

    [Fact]
    public async Task T3_timeout_ends_only_the_transaction()
    {
        var transport = new FakeHsmsTransport();
        var t3Timers = new ManualTimerFactory();
        var (session, transactions) = CreateTransactions(transport, t3Timers);
        await using var manager = transactions;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        manager.Start(cancellation.Token);
        await using var events = manager.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        await SelectTransactionsPassiveAsync(transport, session, events)
            .ConfigureAwait(true);

        var send = manager.SendAsync(1, new SecsMessage(1, 1, true));
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        transport.CompleteSend(1);
        await WaitUntilAsync(() => t3Timers.Timers.Count == 1)
            .ConfigureAwait(true);
        Assert.Equal(T3, t3Timers.Timers[0].DueTime);
        t3Timers.Timers[0].Fire();

        var error = await Assert.ThrowsAsync<HsmsDataTransactionTimeoutException>(
            () => send).ConfigureAwait(true);
        Assert.False(t3Timers.Timers[0].IsArmed);
        Assert.Equal(HsmsSessionState.Selected, manager.State);
        Assert.Equal(0, transport.CloseCount);
        Assert.Equal(1, error.Primary.SessionId);
    }
}
