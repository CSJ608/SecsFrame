namespace SecsFrame.Tests;

public sealed partial class HsmsSessionStateMachineTests
{
    [Fact]
    public async Task Enabled_observation_captures_select_and_linktest_around_state_transitions()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Active,
            new ManualTimerFactory(),
            new FixedSystemBytesProvider(0x01020304),
            enableControlMessageObservation: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var observations = machine
            .GetControlMessageObservationsAsync(cancellation.Token)
            .GetAsyncEnumerator();
        machine.Start(cancellation.Token);

        await AssertActiveSelectObservationsAsync(
            transport,
            machine,
            observations).ConfigureAwait(true);

        var linktest = machine.LinktestAsync(cancellation.Token);
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        transport.CompleteSend(1);
        var linktestSent = await NextControlObservationAsync(
            observations,
            item => item.Direction == HsmsControlMessageDirection.Sent)
            .ConfigureAwait(true);

        Assert.Equal(HsmsSessionState.Selected, linktestSent.State);
        Assert.Equal(HsmsMessageType.LinktestRequest, linktestSent.Header.MessageType);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.LinktestResponse,
                    linktestSent.Header.SystemBytes)));
        var linktestReceived = await NextControlObservationAsync(
            observations,
            item => item.Direction == HsmsControlMessageDirection.Received)
            .ConfigureAwait(true);

        Assert.Equal(HsmsSessionState.Selected, linktestReceived.State);
        Assert.Equal(HsmsMessageType.LinktestResponse, linktestReceived.Header.MessageType);
        await linktest.ConfigureAwait(true);
    }

    private static async Task AssertActiveSelectObservationsAsync(
        FakeHsmsTransport transport,
        HsmsSessionStateMachine machine,
        IAsyncEnumerator<HsmsControlMessageObservation> observations)
    {
        transport.Open(new HsmsTransportSessionId(1));
        await WaitUntilAsync(() => transport.SendCount == 1).ConfigureAwait(true);
        transport.CompleteSend(0);
        var selectSent = await NextControlObservationAsync(
            observations,
            item => item.Direction == HsmsControlMessageDirection.Sent)
            .ConfigureAwait(true);

        Assert.Equal(HsmsSessionState.Selecting, selectSent.State);
        Assert.Equal(HsmsMessageType.SelectRequest, selectSent.Header.MessageType);
        Assert.Equal(0x01020304U, selectSent.Header.SystemBytes);

        transport.Receive(
            new HsmsFrame(
                HsmsMessageHeader.CreateControl(
                    HsmsMessageType.SelectResponse,
                    0x01020304)));
        var selectReceived = await NextControlObservationAsync(
            observations,
            item => item.Direction == HsmsControlMessageDirection.Received)
            .ConfigureAwait(true);

        Assert.Equal(HsmsSessionState.Selecting, selectReceived.State);
        Assert.Equal(HsmsMessageType.SelectResponse, selectReceived.Header.MessageType);
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Selected)
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task Enabled_observation_preserves_unknown_control_header_and_generated_reject()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory(),
            enableControlMessageObservation: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var observations = machine
            .GetControlMessageObservationsAsync(cancellation.Token)
            .GetAsyncEnumerator();
        machine.Start(cancellation.Token);
        await SelectPassiveAsync(transport, machine).ConfigureAwait(true);
        var unknown = new HsmsMessageHeader
        {
            SessionId = ushort.MaxValue,
            HeaderByte2 = 0,
            HeaderByte3 = 0,
            PresentationType = 0,
            MessageType = (HsmsMessageType)0x7F,
            SystemBytes = 0x10203040,
        };

        transport.Receive(new HsmsFrame(unknown));
        var received = await NextControlObservationAsync(
            observations,
            item => (byte)item.Header.MessageType == 0x7F)
            .ConfigureAwait(true);
        await WaitUntilAsync(() => transport.SendCount == 2).ConfigureAwait(true);
        transport.CompleteSend(1);
        var sent = await NextControlObservationAsync(
            observations,
            item => item.Direction == HsmsControlMessageDirection.Sent &&
                item.Header.MessageType == HsmsMessageType.RejectRequest)
            .ConfigureAwait(true);

        Assert.Equal(HsmsControlMessageDirection.Received, received.Direction);
        Assert.Equal(HsmsSessionState.Selected, received.State);
        Assert.Equal(unknown, received.Header);
        Assert.Equal(HsmsSessionState.Selected, sent.State);
        Assert.Equal((byte)0x7F, sent.Header.HeaderByte2);
        Assert.Equal((byte)HsmsRejectReason.UnsupportedSessionType, sent.Header.HeaderByte3);
        Assert.Equal(unknown.SystemBytes, sent.Header.SystemBytes);
    }
}
