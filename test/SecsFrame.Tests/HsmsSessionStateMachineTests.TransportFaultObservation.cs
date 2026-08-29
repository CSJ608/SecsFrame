namespace SecsFrame.Tests;

public sealed partial class HsmsSessionStateMachineTests
{
    [Fact]
    public async Task Enabled_T8_observation_filters_stale_transport_sessions()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory(),
            enableTransportFaultObservation: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observations = machine
            .GetTransportFaultObservationsAsync(cancellation.Token)
            .GetAsyncEnumerator();
        await using var observationScope = observations.ConfigureAwait(true);
        machine.Start(cancellation.Token);
        var firstSession = new HsmsTransportSessionId(1);
        var secondSession = new HsmsTransportSessionId(2);

        transport.Open(firstSession);
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Connected)
            .ConfigureAwait(true);
        transport.CloseCurrent();
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Disconnected)
            .ConfigureAwait(true);
        transport.Open(secondSession);
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Connected)
            .ConfigureAwait(true);

        transport.ObserveT8(firstSession, new byte[] { 0x01 });
        transport.ObserveT8(secondSession, new byte[] { 0x02 });
        var observation = await NextTransportFaultObservationAsync(observations)
            .ConfigureAwait(true);

        Assert.Equal(secondSession.Value, observation.TransportSessionId);
        Assert.Equal(HsmsSessionState.Connected, observation.State);
        Assert.Equal(
            HsmsTransportFaultKind.IncompleteFrameTimeout,
            observation.Kind);
        Assert.Equal(new byte[] { 0x02 }, observation.Snapshot.ToArray());
    }

    [Fact]
    public async Task T8_observation_queue_drops_oldest_without_blocking_state_machine()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory(),
            enableTransportFaultObservation: true,
            transportFaultObservationCapacity: 2);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        machine.Start(cancellation.Token);
        var sessionId = new HsmsTransportSessionId(1);
        transport.Open(sessionId);
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Connected)
            .ConfigureAwait(true);

        transport.ObserveT8(sessionId, new byte[] { 0x01 });
        transport.ObserveT8(sessionId, new byte[] { 0x02 });
        transport.ObserveT8(sessionId, new byte[] { 0x03 });
        transport.CloseCurrent();
        await WaitUntilAsync(() => machine.State == HsmsSessionState.Disconnected)
            .ConfigureAwait(true);
        var observations = machine
            .GetTransportFaultObservationsAsync(cancellation.Token)
            .GetAsyncEnumerator();
        await using var observationScope = observations.ConfigureAwait(true);

        var second = await NextTransportFaultObservationAsync(observations)
            .ConfigureAwait(true);
        var third = await NextTransportFaultObservationAsync(observations)
            .ConfigureAwait(true);

        Assert.Equal(new byte[] { 0x02 }, second.Snapshot.ToArray());
        Assert.Equal(new byte[] { 0x03 }, third.Snapshot.ToArray());
        Assert.Equal(HsmsSessionState.Disconnected, machine.State);
    }

    [Fact]
    public async Task T8_observation_is_disabled_by_default()
    {
        var transport = new FakeHsmsTransport();
        await using var machine = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine
                .GetTransportFaultObservationsAsync(CancellationToken.None)
                .GetAsyncEnumerator()
                .MoveNextAsync()
                .AsTask()
                .ConfigureAwait(true)).ConfigureAwait(true);
    }

    private static async Task<HsmsTransportFaultObservation>
        NextTransportFaultObservationAsync(
            IAsyncEnumerator<HsmsTransportFaultObservation> observations)
    {
        if (await observations.MoveNextAsync().ConfigureAwait(true))
            return observations.Current;

        Assert.Fail("The expected HSMS transport-fault observation was not received.");
        return null!;
    }
}
