using System.Net;
using SecsFrame.Gem;

namespace SecsFrame.Tests;

public sealed class GemFoundationTcpTests
{
    [Fact]
    public async Task Host_and_equipment_complete_foundational_dialogues_over_tcp()
    {
        await using var context = new GemTcpContext();
        await context.StartAsync().ConfigureAwait(true);

        await AssertCommunicationAndOnlineAsync(context).ConfigureAwait(true);
        await AssertDynamicDataAsync(context).ConfigureAwait(true);
        await AssertClockAsync(context).ConfigureAwait(true);
        Assert.Equal(
            context.HostServices.Identity,
            await context.EquipmentServices.EstablishCommunicationAsync(
                context.Token).ConfigureAwait(true));
        await context.HostServices.RequestOfflineAsync(context.Token)
            .ConfigureAwait(true);
        await WaitUntilAsync(
            () => context.EquipmentServices.OnlineState == GemOnlineState.Offline,
            context.Token).ConfigureAwait(true);
        Assert.Equal(GemOnlineState.Offline, context.HostServices.OnlineState);
        Assert.Equal(GemOnlineState.Offline, context.EquipmentServices.OnlineState);
        await context.Host.LinktestAsync(context.Token).ConfigureAwait(true);
    }

    private static async Task AssertCommunicationAndOnlineAsync(
        GemTcpContext context)
    {
        var equipmentIdentity = await context.HostServices
            .EstablishCommunicationAsync(context.Token).ConfigureAwait(true);
        await WaitUntilAsync(
            () => context.EquipmentServices.CommunicationState ==
                GemCommunicationState.Communicating,
            context.Token).ConfigureAwait(true);
        Assert.Equal(new GemIdentity("EQ-01", "1.5"), equipmentIdentity);
        Assert.Equal(
            GemCommunicationState.Communicating,
            context.HostServices.CommunicationState);
        Assert.Equal(
            GemCommunicationState.Communicating,
            context.EquipmentServices.CommunicationState);
        Assert.Equal(
            context.HostServices.Identity,
            context.EquipmentServices.PeerIdentity);
        Assert.Equal(
            equipmentIdentity,
            await context.HostServices.AreYouOnlineAsync(context.Token)
                .ConfigureAwait(true));

        await context.HostServices.RequestOnlineAsync(context.Token)
            .ConfigureAwait(true);
        await WaitUntilAsync(
            () => context.EquipmentServices.OnlineState == GemOnlineState.Online,
            context.Token).ConfigureAwait(true);
        Assert.Equal(GemOnlineState.Online, context.HostServices.OnlineState);
        Assert.Equal(GemOnlineState.Online, context.EquipmentServices.OnlineState);
    }

    private static async Task AssertDynamicDataAsync(GemTcpContext context)
    {
        var variables = await context.HostServices.ReadStatusVariablesAsync(
            new[] { SecsItem.Ascii("TEMP"), SecsItem.U4(1001) },
            context.Token).ConfigureAwait(true);
        Assert.Equal(
            new[] { SecsItem.F8(23.5), SecsItem.Ascii("READY") },
            variables);

        var constants = await context.HostServices.ReadEquipmentConstantsAsync(
            new[] { SecsItem.U2(2001) },
            context.Token).ConfigureAwait(true);
        Assert.Equal(
            SecsItem.List(SecsItem.U4(10), SecsItem.Boolean(true)),
            Assert.Single(constants));
    }

    private static async Task AssertClockAsync(GemTcpContext context)
    {
        Assert.Equal(
            context.InitialTime.ToUniversalTime(),
            await context.HostServices.GetClockAsync(context.Token)
                .ConfigureAwait(true));
        context.Clock.AcceptSet = false;
        var rejected = await Assert.ThrowsAsync<GemRequestRejectedException>(
            () => context.HostServices.SetClockAsync(
                context.InitialTime.AddHours(1),
                context.Token)).ConfigureAwait(true);
        Assert.Equal(GemOperation.SetClock, rejected.Operation);
        Assert.Equal((byte)1, rejected.Acknowledgement);

        context.Clock.AcceptSet = true;
        var replacementTime = context.InitialTime.AddHours(2);
        await context.HostServices.SetClockAsync(replacementTime, context.Token)
            .ConfigureAwait(true);
        Assert.Equal(replacementTime.ToUniversalTime(), context.Clock.Value);
    }

    private static async Task PumpAsync(
        SecsEndpoint endpoint,
        Func<HsmsConnectionEvent, CancellationToken, ValueTask<bool>> dispatch,
        CancellationToken cancellationToken)
    {
        var events = endpoint
            .GetEventsAsync(cancellationToken)
            .GetAsyncEnumerator();
        try
        {
            while (await events.MoveNextAsync().ConfigureAwait(false))
            {
                _ = await dispatch(events.Current, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await events.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken)
                .ConfigureAwait(false);
        }

        Assert.True(condition(), "The expected peer state was not observed.");
    }

    private static HsmsConnectionOptions CreateOptions(
        int port,
        HsmsConnectionMode mode)
        => new(
            IPAddress.Loopback,
            port,
            mode,
            sessionId: 10,
            t3: TimeSpan.FromSeconds(5),
            t5: TimeSpan.FromMilliseconds(10),
            t6: TimeSpan.FromSeconds(5),
            t7: TimeSpan.FromSeconds(10),
            t8: TimeSpan.FromSeconds(5));

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class GemTcpContext : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime =
            new(TimeSpan.FromSeconds(20));
        private readonly GemValueRegistration _variable1;
        private readonly GemValueRegistration _variable2;
        private readonly GemValueRegistration _constant;
        private Task _hostPump = Task.CompletedTask;
        private Task _equipmentPump = Task.CompletedTask;

        internal GemTcpContext()
        {
            var port = GetFreePort();
            Host = new SecsHost(CreateOptions(port, HsmsConnectionMode.Active));
            Equipment = new SecsEquipment(
                CreateOptions(port, HsmsConnectionMode.Passive));
            HostServices = new GemHostServices(
                Host,
                new GemIdentity("HOST-01", "2.0"));
            InitialTime = new DateTimeOffset(
                2026,
                8,
                28,
                9,
                10,
                11,
                TimeSpan.FromHours(8)).AddMilliseconds(120);
            Clock = new TestGemClock(InitialTime);
            EquipmentServices = new GemEquipmentServices(
                Equipment,
                new GemIdentity("EQ-01", "1.5"),
                Clock);
            _variable1 = EquipmentServices.RegisterStatusVariable(
                SecsItem.U4(1001),
                static _ => new ValueTask<SecsItem>(SecsItem.Ascii("READY")));
            _variable2 = EquipmentServices.RegisterStatusVariable(
                SecsItem.Ascii("TEMP"),
                static _ => new ValueTask<SecsItem>(SecsItem.F8(23.5)));
            _constant = EquipmentServices.RegisterEquipmentConstant(
                SecsItem.U2(2001),
                static _ => new ValueTask<SecsItem>(
                    SecsItem.List(SecsItem.U4(10), SecsItem.Boolean(true))));
        }

        internal SecsHost Host { get; }

        internal SecsEquipment Equipment { get; }

        internal GemHostServices HostServices { get; }

        internal GemEquipmentServices EquipmentServices { get; }

        internal TestGemClock Clock { get; }

        internal DateTimeOffset InitialTime { get; }

        internal CancellationToken Token => _lifetime.Token;

        internal async Task StartAsync()
        {
            Equipment.Start();
            Host.Start();
            _hostPump = PumpAsync(Host, HostServices.TryDispatchAsync, Token);
            _equipmentPump = PumpAsync(
                Equipment,
                EquipmentServices.TryDispatchAsync,
                Token);
            await Task.WhenAll(
                Host.WaitUntilSelectedAsync(Token),
                Equipment.WaitUntilSelectedAsync(Token)).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            await Task.WhenAll(_hostPump, _equipmentPump).ConfigureAwait(false);
            _constant.Dispose();
            _variable2.Dispose();
            _variable1.Dispose();
            EquipmentServices.Dispose();
            HostServices.Dispose();
            await Equipment.DisposeAsync().ConfigureAwait(false);
            await Host.DisposeAsync().ConfigureAwait(false);
            _lifetime.Dispose();
        }
    }

    private sealed class TestGemClock : IGemClock
    {
        internal TestGemClock(DateTimeOffset value)
        {
            Value = value;
        }

        internal bool AcceptSet { get; set; } = true;

        internal DateTimeOffset Value { get; private set; }

        public ValueTask<DateTimeOffset> GetCurrentTimeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<DateTimeOffset>(Value);
        }

        public ValueTask<bool> SetCurrentTimeAsync(
            DateTimeOffset value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AcceptSet)
                Value = value;
            return new ValueTask<bool>(AcceptSet);
        }
    }
}
