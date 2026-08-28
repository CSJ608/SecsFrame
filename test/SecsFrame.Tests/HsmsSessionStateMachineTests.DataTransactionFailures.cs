namespace SecsFrame.Tests;

public sealed partial class HsmsSessionStateMachineTests
{
    [Fact]
    public async Task Data_reject_fails_the_matching_transaction_and_is_observable()
    {
        var transport = new FakeHsmsTransport();
        var t3Timers = new ManualTimerFactory();
        var (session, transactions) = CreateTransactions(
            transport,
            t3Timers,
            new SequenceSystemBytesProvider(0x55667788));
        await using var manager = transactions;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        manager.Start(cancellation.Token);
        await using var events = manager.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        await SelectTransactionsPassiveAsync(transport, session, events)
            .ConfigureAwait(true);

        var send = manager.SendAsync(2, new SecsMessage(1, 1, true));
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        transport.CompleteSend(1);
        await WaitUntilAsync(() => t3Timers.Timers.Count == 1)
            .ConfigureAwait(true);
        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateReject(
                    0x55667788,
                    (byte)HsmsMessageType.DataMessage,
                    (byte)HsmsRejectReason.EntityNotSelected)));

        var error = await Assert.ThrowsAsync<HsmsDataMessageRejectedException>(
            () => send).ConfigureAwait(true);
        var control = await NextMatchingTransactionEventAsync(
            events,
            item => item.Kind ==
                HsmsDataTransactionEventKind.ControlMessageReceived)
            .ConfigureAwait(true);
        Assert.Equal(HsmsRejectReason.EntityNotSelected, error.Reason);
        Assert.Equal(0x55667788u, control.Frame!.Header.SystemBytes);
        Assert.False(t3Timers.Timers[0].IsArmed);
    }

    [Fact]
    public async Task Disconnect_interrupts_all_open_data_transactions()
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
        transport.CloseCurrent();

        var error = await Assert.ThrowsAsync<HsmsDataTransactionInterruptedException>(
            () => send).ConfigureAwait(true);
        Assert.Equal(HsmsSessionState.Disconnected, error.State);
        Assert.False(t3Timers.Timers[0].IsArmed);
    }

    [Fact]
    public async Task Malformed_matching_secondary_fails_transaction_and_reports_decode_error()
    {
        var transport = new FakeHsmsTransport();
        var t3Timers = new ManualTimerFactory();
        var (session, transactions) = CreateTransactions(
            transport,
            t3Timers,
            new SequenceSystemBytesProvider(0x10203040));
        await using var manager = transactions;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        manager.Start(cancellation.Token);
        await using var events = manager.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        await SelectTransactionsPassiveAsync(transport, session, events)
            .ConfigureAwait(true);

        var send = manager.SendAsync(1, new SecsMessage(3, 5, true));
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        transport.CompleteSend(1);
        await WaitUntilAsync(() => t3Timers.Timers.Count == 1)
            .ConfigureAwait(true);
        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateData(
                    1,
                    3,
                    6,
                    false,
                    0x10203040),
                new byte[] { 0x20 }));

        await Assert.ThrowsAsync<SecsProtocolException>(
            () => send).ConfigureAwait(true);
        var decodeFailure = await NextMatchingTransactionEventAsync(
            events,
            item => item.Kind ==
                HsmsDataTransactionEventKind.DataMessageDecodeFailed)
            .ConfigureAwait(true);
        Assert.IsType<SecsProtocolException>(decodeFailure.Error);
        Assert.False(t3Timers.Timers[0].IsArmed);
    }

    [Fact]
    public async Task Cancellation_stops_waiting_and_late_secondary_remains_observable()
    {
        var transport = new FakeHsmsTransport();
        var t3Timers = new ManualTimerFactory();
        var (session, transactions) = CreateTransactions(transport, t3Timers);
        await using var manager = transactions;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var sendCancellation = new CancellationTokenSource();
        manager.Start(lifetime.Token);
        await using var events = manager.GetEventsAsync(lifetime.Token).GetAsyncEnumerator();
        await SelectTransactionsPassiveAsync(transport, session, events)
            .ConfigureAwait(true);

        var send = manager.SendAsync(
            1,
            new SecsMessage(1, 1, true),
            sendCancellation.Token);
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        var primary = transport.GetSentFrame(1);
        transport.CompleteSend(1);
        await WaitUntilAsync(() => t3Timers.Timers.Count == 1)
            .ConfigureAwait(true);
        sendCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => send).ConfigureAwait(true);

        transport.Receive(
            new HsmsDataMessageCodec().EncodeFrame(
                new HsmsDataMessage(
                    1,
                    primary.Header.SystemBytes,
                    new SecsMessage(1, 2))));
        var late = await NextMatchingTransactionEventAsync(
            events,
            item => item.Kind ==
                HsmsDataTransactionEventKind.DataMessageReceived)
            .ConfigureAwait(true);
        Assert.Equal(primary.Header.SystemBytes, late.DataMessage!.DataMessage.SystemBytes);
        Assert.False(t3Timers.Timers[0].IsArmed);
    }
}
