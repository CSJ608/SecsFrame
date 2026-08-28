namespace SecsFrame.Tests;

public sealed partial class HsmsSessionStateMachineTests
{
    [Fact]
    public async Task Local_linktest_starts_T6_after_write_and_completes_on_matching_response()
    {
        var transport = new FakeHsmsTransport();
        var timers = new ManualTimerFactory();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            timers,
            new FixedSystemBytesProvider(0x10203040));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        await SelectPassiveAsync(transport, machine).ConfigureAwait(true);

        var linktest = machine.LinktestAsync(cancellation.Token);
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);

        AssertControlFrame(
            transport.GetSentFrame(1),
            HsmsMessageType.LinktestRequest,
            0x10203040,
            0);
        Assert.Single(timers.Timers);
        Assert.False(linktest.IsCompleted);

        transport.CompleteSend(1);
        await WaitUntilAsync(() => timers.Timers.Count == 2).ConfigureAwait(true);
        Assert.Equal(T6, timers.Timers[1].DueTime);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.LinktestResponse,
                    0x10203040)));
        await linktest.ConfigureAwait(true);

        Assert.False(timers.Timers[1].IsArmed);
        Assert.Equal(HsmsSessionState.Selected, machine.State);
    }

    [Fact]
    public async Task Linktest_T6_expiry_closes_session_and_fails_command()
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
        var linktest = machine.LinktestAsync(cancellation.Token);
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        transport.CompleteSend(1);
        await WaitUntilAsync(() => timers.Timers.Count == 2).ConfigureAwait(true);

        timers.Timers[1].Fire();
        var error = await Assert.ThrowsAsync<HsmsSessionTimeoutException>(
            async () => await linktest.ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal("T6", error.TimerName);
        Assert.Equal(HsmsTimer.T6, error.Timer);
        Assert.Equal(HsmsOperation.Linktest, error.Operation);
        Assert.Equal(HsmsSessionState.Disconnected, machine.State);
    }

    [Fact]
    public async Task Unexpected_linktest_response_is_rejected_while_original_request_remains_open()
    {
        var transport = new FakeHsmsTransport();
        var timers = new ManualTimerFactory();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            timers,
            new FixedSystemBytesProvider(20));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        await SelectPassiveAsync(transport, machine).ConfigureAwait(true);
        var linktest = machine.LinktestAsync(cancellation.Token);
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        transport.CompleteSend(1);
        await WaitUntilAsync(() => timers.Timers.Count == 2).ConfigureAwait(true);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.LinktestResponse,
                    21)));
        await WaitUntilAsync(() => transport.SendCount == 3).ConfigureAwait(true);

        AssertControlFrame(
            transport.GetSentFrame(2),
            HsmsMessageType.RejectRequest,
            21,
            (byte)HsmsRejectReason.TransactionNotOpen,
            (byte)HsmsMessageType.LinktestResponse);
        Assert.False(linktest.IsCompleted);
        Assert.True(timers.Timers[1].IsArmed);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.LinktestResponse,
                    20)));
        await linktest.ConfigureAwait(true);
        Assert.False(timers.Timers[1].IsArmed);
    }

    [Fact]
    public async Task Linktest_request_is_answered_in_connected_and_selected_states()
    {
        var connectedTransport = new FakeHsmsTransport();
        await using (var connectedMachine = CreateMachine(
            connectedTransport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory()))
        {
            using var cancellation =
                new CancellationTokenSource(TimeSpan.FromSeconds(5));
            connectedMachine.Start(cancellation.Token);
            connectedTransport.Open(new HsmsTransportSessionId(1));
            connectedTransport.Receive(
                new HsmsFrame(
                    HsmsMessageHeader.CreateControl(
                        HsmsMessageType.LinktestRequest,
                        1)));
            await WaitUntilAsync(() => connectedTransport.SendCount == 1)
                .ConfigureAwait(true);
            AssertControlFrame(
                connectedTransport.GetSentFrame(0),
                HsmsMessageType.LinktestResponse,
                1,
                0);
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
        selectedMachine.Start(selectedCancellation.Token);
        await SelectPassiveAsync(selectedTransport, selectedMachine)
            .ConfigureAwait(true);

        selectedTransport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.LinktestRequest,
                    2)));
        await WaitUntilAsync(() => selectedTransport.SendCount == 2)
            .ConfigureAwait(true);

        AssertControlFrame(
            selectedTransport.GetSentFrame(1),
            HsmsMessageType.LinktestResponse,
            2,
            0);
        Assert.Equal(HsmsSessionState.Selected, selectedMachine.State);
    }

    [Fact]
    public async Task Local_deselect_returns_to_connected_and_restarts_T7()
    {
        var transport = new FakeHsmsTransport();
        var timers = new ManualTimerFactory();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            timers,
            new FixedSystemBytesProvider(0x55667788));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        await SelectPassiveAsync(transport, machine).ConfigureAwait(true);

        var deselect = machine.DeselectAsync(cancellation.Token);
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        AssertControlFrame(
            transport.GetSentFrame(1),
            HsmsMessageType.DeselectRequest,
            0x55667788,
            0);
        transport.CompleteSend(1);
        await WaitUntilAsync(() => timers.Timers.Count == 2).ConfigureAwait(true);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.DeselectResponse,
                    0x55667788,
                    (byte)HsmsDeselectStatus.Success)));
        await deselect.ConfigureAwait(true);
        await WaitUntilAsync(() => timers.Timers.Count == 3).ConfigureAwait(true);

        Assert.Equal(HsmsSessionState.Connected, machine.State);
        Assert.False(timers.Timers[1].IsArmed);
        Assert.Equal(T7, timers.Timers[2].DueTime);
    }

    [Fact]
    public async Task Peer_deselect_changes_state_only_after_success_response_is_written()
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
                    HsmsMessageType.DeselectRequest,
                    9)));
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);

        AssertControlFrame(
            transport.GetSentFrame(1),
            HsmsMessageType.DeselectResponse,
            9,
            (byte)HsmsDeselectStatus.Success);
        Assert.Equal(HsmsSessionState.Selected, machine.State);

        transport.CompleteSend(1);
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Connected)
            .ConfigureAwait(true);
        Assert.Equal(T7, timers.Timers[^1].DueTime);
    }

    [Fact]
    public async Task Peer_deselect_interrupts_open_linktest_and_cancels_its_T6()
    {
        var transport = new FakeHsmsTransport();
        var timers = new ManualTimerFactory();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            timers,
            new FixedSystemBytesProvider(80));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        await SelectPassiveAsync(transport, machine).ConfigureAwait(true);
        var linktest = machine.LinktestAsync(cancellation.Token);
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        transport.CompleteSend(1);
        await WaitUntilAsync(() => timers.Timers.Count == 2).ConfigureAwait(true);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.DeselectRequest,
                    81)));
        await WaitUntilAsync(() => transport.SendCount == 3).ConfigureAwait(true);
        transport.CompleteSend(2);
        var interrupted = await Assert.ThrowsAsync<
            HsmsControlTransactionInterruptedException>(
            async () => await linktest.ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(HsmsOperation.Linktest, interrupted.Operation);
        Assert.Equal(HsmsSessionState.Connected, interrupted.State);
        Assert.Equal(HsmsSessionState.Connected, machine.State);
        Assert.False(timers.Timers[1].IsArmed);
        Assert.Equal(T7, timers.Timers[^1].DueTime);
    }

    [Fact]
    public async Task Deselect_request_while_connected_returns_not_selected()
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

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.DeselectRequest,
                    12)));
        await WaitUntilAsync(() => transport.SendCount == 1).ConfigureAwait(true);

        AssertControlFrame(
            transport.GetSentFrame(0),
            HsmsMessageType.DeselectResponse,
            12,
            (byte)HsmsDeselectStatus.NotSelected);
        transport.CompleteSend(0);
        Assert.Equal(HsmsSessionState.Connected, machine.State);
    }

    [Fact]
    public async Task Rejected_deselect_fails_command_and_keeps_selected_state()
    {
        var transport = new FakeHsmsTransport();
        var timers = new ManualTimerFactory();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            timers,
            new FixedSystemBytesProvider(30));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        await SelectPassiveAsync(transport, machine).ConfigureAwait(true);
        var deselect = machine.DeselectAsync(cancellation.Token);
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        transport.CompleteSend(1);
        await WaitUntilAsync(() => timers.Timers.Count == 2).ConfigureAwait(true);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.DeselectResponse,
                    30,
                    (byte)HsmsDeselectStatus.NotSelected)));
        var error = await Assert.ThrowsAsync<HsmsDeselectRejectedException>(
            async () => await deselect.ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(HsmsDeselectStatus.NotSelected, error.Status);
        Assert.Equal(HsmsSessionState.Selected, machine.State);
        Assert.False(timers.Timers[1].IsArmed);
    }

    [Fact]
    public async Task Unsupported_SType_and_PType_send_specific_reject_requests()
    {
        var unsupportedTypeTransport = new FakeHsmsTransport();
        await using (var unsupportedTypeMachine = CreateMachine(
            unsupportedTypeTransport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory()))
        {
            using var cancellation =
                new CancellationTokenSource(TimeSpan.FromSeconds(5));
            unsupportedTypeMachine.Start(cancellation.Token);
            unsupportedTypeTransport.Open(new HsmsTransportSessionId(1));
            unsupportedTypeTransport.Receive(
                new HsmsFrame(
                    HsmsMessageHeader.CreateControl(
                        (HsmsMessageType)0x7F,
                        40)));
            await WaitUntilAsync(() => unsupportedTypeTransport.SendCount == 1)
                .ConfigureAwait(true);

            AssertControlFrame(
                unsupportedTypeTransport.GetSentFrame(0),
                HsmsMessageType.RejectRequest,
                40,
                (byte)HsmsRejectReason.UnsupportedSessionType,
                0x7F);
            Assert.Equal(0, unsupportedTypeTransport.CloseCount);
        }

        var unsupportedPTypeTransport = new FakeHsmsTransport();
        var unsupportedPTypeMachine = CreateMachine(
            unsupportedPTypeTransport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory());
        await using var unsupportedPTypeMachineScope =
            unsupportedPTypeMachine.ConfigureAwait(true);
        using var unsupportedPTypeCancellation =
            new CancellationTokenSource(TimeSpan.FromSeconds(5));
        unsupportedPTypeMachine.Start(unsupportedPTypeCancellation.Token);
        unsupportedPTypeTransport.Open(new HsmsTransportSessionId(1));
        var unsupportedPTypeHeader =
            HsmsMessageHeader.CreateControl(
                HsmsMessageType.LinktestRequest,
                41) with
            {
                PresentationType = 1,
            };

        unsupportedPTypeTransport.Receive(
            new HsmsFrame(unsupportedPTypeHeader));
        await WaitUntilAsync(() => unsupportedPTypeTransport.SendCount == 1)
            .ConfigureAwait(true);

        AssertControlFrame(
            unsupportedPTypeTransport.GetSentFrame(0),
            HsmsMessageType.RejectRequest,
            41,
            (byte)HsmsRejectReason.UnsupportedPresentationType,
            (byte)HsmsMessageType.LinktestRequest);
        Assert.Equal(0, unsupportedPTypeTransport.CloseCount);
    }

    [Fact]
    public async Task Matching_reject_fails_linktest_and_cancels_T6()
    {
        var transport = new FakeHsmsTransport();
        var timers = new ManualTimerFactory();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            timers,
            new FixedSystemBytesProvider(50));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        await SelectPassiveAsync(transport, machine).ConfigureAwait(true);
        var linktest = machine.LinktestAsync(cancellation.Token);
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        transport.CompleteSend(1);
        await WaitUntilAsync(() => timers.Timers.Count == 2).ConfigureAwait(true);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateReject(
                    50,
                    (byte)HsmsMessageType.LinktestRequest,
                    (byte)HsmsRejectReason.EntityNotSelected)));
        var error = await Assert.ThrowsAsync<HsmsControlRejectedException>(
            async () => await linktest.ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal((byte)HsmsMessageType.LinktestRequest, error.RejectedMessageType);
        Assert.Equal(HsmsRejectReason.EntityNotSelected, error.Reason);
        Assert.False(timers.Timers[1].IsArmed);
        Assert.Equal(HsmsSessionState.Selected, machine.State);
    }

    [Fact]
    public async Task Unclaimed_reject_is_forwarded_for_future_transaction_handling()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = machine.GetEventsAsync(cancellation.Token).GetAsyncEnumerator();
        machine.Start(cancellation.Token);
        await SelectPassiveAsync(transport, machine).ConfigureAwait(true);
        var reject = new HsmsFrame(
            HsmsMessageHeader.CreateReject(
                70,
                (byte)HsmsMessageType.DataMessage,
                (byte)HsmsRejectReason.TransactionNotOpen));

        transport.Receive(reject);
        var received = await NextMatchingAsync(
            events,
            static sessionEvent =>
                sessionEvent.Kind == HsmsSessionEventKind.ControlMessageReceived)
            .ConfigureAwait(true);

        Assert.Same(reject, received.Frame);
        Assert.Equal(HsmsSessionState.Selected, received.State);
        Assert.Equal(0, transport.CloseCount);
        await events.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Only_one_local_control_command_can_be_open()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory(),
            new FixedSystemBytesProvider(60));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        await SelectPassiveAsync(transport, machine).ConfigureAwait(true);

        var linktest = machine.LinktestAsync(cancellation.Token);
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        var deselect = machine.DeselectAsync(cancellation.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await deselect.ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(2, transport.SendCount);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.LinktestResponse,
                    60)));
        await linktest.ConfigureAwait(true);
    }

    [Fact]
    public async Task Local_control_commands_require_selected_state()
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

        var linktest = machine.LinktestAsync(cancellation.Token);
        var deselect = machine.DeselectAsync(cancellation.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await linktest.ConfigureAwait(true)).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await deselect.ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(0, transport.SendCount);
    }

    [Theory]
    [InlineData(HsmsMessageType.DeselectRequest)]
    [InlineData(HsmsMessageType.LinktestRequest)]
    [InlineData(HsmsMessageType.LinktestResponse)]
    public async Task Reserved_status_on_zero_status_control_message_closes_session(
        HsmsMessageType messageType)
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

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    messageType,
                    90,
                    status: 1)));
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Disconnected)
            .ConfigureAwait(true);

        Assert.Equal(1, transport.CloseCount);
        Assert.IsType<HsmsProtocolException>(transport.LastCloseError);
        Assert.Equal(HsmsSessionState.Disconnected, machine.State);
    }
}
