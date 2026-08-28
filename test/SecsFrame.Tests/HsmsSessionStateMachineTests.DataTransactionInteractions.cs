namespace SecsFrame.Tests;

public sealed partial class HsmsSessionStateMachineTests
{
    [Fact]
    public async Task Incoming_primary_reply_copies_session_and_system_bytes_once()
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

        var primary = new HsmsDataMessage(
            9,
            0xAABBCCDD,
            new SecsMessage(
                6,
                11,
                true,
                SecsItem.List(SecsItem.Ascii("LOT-001"), SecsItem.U4(7))));
        transport.Receive(new HsmsDataMessageCodec().EncodeFrame(primary));
        var received = await NextMatchingTransactionEventAsync(
            events,
            item => item.Kind ==
                HsmsDataTransactionEventKind.DataMessageReceived)
            .ConfigureAwait(true);

        var reply = manager.ReplyAsync(
            received.DataMessage!,
            new SecsMessage(6, 12, rootItem: SecsItem.Boolean(true)));
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        Assert.False(reply.IsCompleted);
        var encodedReply = new HsmsDataMessageCodec().Decode(
            transport.GetSentFrame(1));
        Assert.Equal(primary.SessionId, encodedReply.SessionId);
        Assert.Equal(primary.SystemBytes, encodedReply.SystemBytes);
        Assert.False(encodedReply.Message.ReplyExpected);

        transport.CompleteSend(1);
        await reply.ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.ReplyAsync(
                received.DataMessage!,
                new SecsMessage(6, 12))).ConfigureAwait(true);
        Assert.Empty(t3Timers.Timers);
    }

    [Fact]
    public async Task Deselect_interrupts_T3_waiters_when_session_returns_to_connected()
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

        var deselect = manager.DeselectAsync();
        await WaitUntilAsync(() => transport.SendCount == 3).ConfigureAwait(true);
        var request = transport.GetSentFrame(2);
        transport.CompleteSend(2);
        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.DeselectResponse,
                    request.Header.SystemBytes)));

        await deselect.ConfigureAwait(true);
        var error = await Assert.ThrowsAsync<HsmsDataTransactionInterruptedException>(
            () => send).ConfigureAwait(true);
        Assert.Equal(HsmsSessionState.Connected, error.State);
        Assert.False(t3Timers.Timers[0].IsArmed);
    }

    [Fact]
    public async Task Concurrent_transactions_use_unique_keys_and_independent_T3_timers()
    {
        var transport = new FakeHsmsTransport();
        var t3Timers = new ManualTimerFactory();
        var (session, transactions) = CreateTransactions(
            transport,
            t3Timers,
            new SequenceSystemBytesProvider(5, 5, 6));
        await using var manager = transactions;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        manager.Start(cancellation.Token);
        await using var events = manager.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        await SelectTransactionsPassiveAsync(transport, session, events)
            .ConfigureAwait(true);

        var first = manager.SendAsync(1, new SecsMessage(1, 1, true));
        var second = manager.SendAsync(2, new SecsMessage(3, 5, true));
        await WaitUntilAsync(() => transport.SendCount == 3).ConfigureAwait(true);
        var firstFrame = transport.GetSentFrame(1);
        var secondFrame = transport.GetSentFrame(2);
        Assert.Equal(5u, firstFrame.Header.SystemBytes);
        Assert.Equal(6u, secondFrame.Header.SystemBytes);

        transport.CompleteSend(1);
        await WaitUntilAsync(() => t3Timers.Timers.Count == 1)
            .ConfigureAwait(true);
        transport.CompleteSend(2);
        await WaitUntilAsync(() => t3Timers.Timers.Count == 2)
            .ConfigureAwait(true);
        transport.Receive(
            new HsmsDataMessageCodec().EncodeFrame(
                new HsmsDataMessage(
                    2,
                    6,
                    new SecsMessage(3, 6))));

        var secondReply = await second.ConfigureAwait(true);
        Assert.Equal(6u, secondReply!.SystemBytes);
        Assert.True(t3Timers.Timers[0].IsArmed);
        Assert.False(t3Timers.Timers[1].IsArmed);
        t3Timers.Timers[0].Fire();
        await Assert.ThrowsAsync<HsmsDataTransactionTimeoutException>(
            () => first).ConfigureAwait(true);
    }

    [Fact]
    public async Task Matching_system_bytes_with_a_different_protocol_session_is_unmatched_data()
    {
        var transport = new FakeHsmsTransport();
        var t3Timers = new ManualTimerFactory();
        var (session, transactions) = CreateTransactions(
            transport,
            t3Timers,
            new SequenceSystemBytesProvider(77));
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
        transport.Receive(
            new HsmsDataMessageCodec().EncodeFrame(
                new HsmsDataMessage(2, 77, new SecsMessage(1, 2))));

        var unmatched = await NextMatchingTransactionEventAsync(
            events,
            item => item.Kind ==
                HsmsDataTransactionEventKind.DataMessageReceived)
            .ConfigureAwait(true);
        Assert.Equal(2, unmatched.DataMessage!.DataMessage.SessionId);
        Assert.False(send.IsCompleted);

        transport.Receive(
            new HsmsDataMessageCodec().EncodeFrame(
                new HsmsDataMessage(1, 77, new SecsMessage(1, 2))));
        Assert.Equal(77u, (await send.ConfigureAwait(true))!.SystemBytes);
    }
}
