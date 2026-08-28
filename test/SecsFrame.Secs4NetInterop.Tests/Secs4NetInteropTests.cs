using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using OfficialConnectionState = Secs4Net.ConnectionState;
using OfficialHsmsConnection = Secs4Net.HsmsConnection;
using OfficialItem = Secs4Net.Item;
using OfficialSecsGem = Secs4Net.SecsGem;
using OfficialSecsGemOptions = Secs4Net.SecsGemOptions;
using OfficialSecsMessage = Secs4Net.SecsMessage;

namespace SecsFrame.Secs4NetInterop.Tests;

public sealed class Secs4NetInteropTests
{
    private const ushort DeviceId = 10;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Official_package_interoperates_in_both_connection_modes(
        bool secs4NetIsActive)
    {
        var port = GetFreePort();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var logger = new RecordingSecs4NetLogger();
        var officialOptions = Options.Create(
            CreateOfficialOptions(port, secs4NetIsActive));
        await using var connection = new HsmsConnection(
            CreateSecsFrameOptions(port, secs4NetIsActive));
        await using var officialConnection = new OfficialHsmsConnection(
            officialOptions,
            logger);
        using var officialGem = new OfficialSecsGem(
            officialOptions,
            officialConnection,
            logger);
        var officialSelected = WaitUntilOfficialSelectedAsync(
            officialConnection,
            lifetime.Token);

        connection.Start();
        await using var events = connection
            .GetEventsAsync(lifetime.Token)
            .GetAsyncEnumerator();
        officialConnection.Start(lifetime.Token);
        await Task.WhenAll(
            connection.WaitUntilSelectedAsync(lifetime.Token),
            officialSelected).ConfigureAwait(true);

        await VerifyLinktestsAsync(
            connection,
            officialConnection,
            logger,
            lifetime.Token).ConfigureAwait(true);
        await VerifySecsFramePrimaryAsync(
            connection,
            officialGem,
            lifetime.Token).ConfigureAwait(true);
        await VerifyOfficialPrimaryAsync(
            connection,
            officialGem,
            events,
            lifetime.Token).ConfigureAwait(true);
    }

    private static async Task VerifyLinktestsAsync(
        HsmsConnection connection,
        OfficialHsmsConnection officialConnection,
        RecordingSecs4NetLogger logger,
        CancellationToken cancellationToken)
    {
        await connection.LinktestAsync(cancellationToken).ConfigureAwait(true);

        officialConnection.LinkTestEnabled = true;
        try
        {
            await logger.WaitForInfoAsync(
                "Receive Control message: LinkTestResponse",
                cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            officialConnection.LinkTestEnabled = false;
        }

        Assert.Equal(HsmsSessionState.Selected, connection.State);
        Assert.Equal(OfficialConnectionState.Selected, officialConnection.State);
    }

    private static async Task VerifySecsFramePrimaryAsync(
        HsmsConnection connection,
        OfficialSecsGem officialGem,
        CancellationToken cancellationToken)
    {
        var officialMessages = officialGem
            .GetPrimaryMessageAsync(cancellationToken)
            .GetAsyncEnumerator();
        await using var ownedMessages = officialMessages.ConfigureAwait(true);
        var send = connection.SendAsync(
            new SecsMessage(
                6,
                11,
                true,
                SecsItem.List(
                    SecsItem.Ascii("LOT-42"),
                    SecsItem.List(
                        SecsItem.U1(0, 1, byte.MaxValue),
                        SecsItem.I4(int.MinValue, int.MaxValue),
                        SecsItem.Boolean(false, true)))),
            cancellationToken);

        Assert.True(await officialMessages.MoveNextAsync().ConfigureAwait(true));
        var incoming = officialMessages.Current;
        using var primary = incoming.PrimaryMessage;
        AssertOfficialPrimary(primary);
        using var reply = new OfficialSecsMessage(6, 12, replyExpected: false)
        {
            SecsItem = OfficialItem.Boolean(true),
        };
        Assert.True(await incoming.TryReplyAsync(reply, cancellationToken)
            .ConfigureAwait(true));

        var secondary = await send.ConfigureAwait(true);
        Assert.NotNull(secondary);
        Assert.Equal(6, secondary.Message.Stream);
        Assert.Equal(12, secondary.Message.Function);
        Assert.Equal(SecsItem.Boolean(true), secondary.Message.RootItem);
    }

    private static async Task VerifyOfficialPrimaryAsync(
        HsmsConnection connection,
        OfficialSecsGem officialGem,
        IAsyncEnumerator<HsmsConnectionEvent> events,
        CancellationToken cancellationToken)
    {
        using var primary = new OfficialSecsMessage(1, 1)
        {
            SecsItem = OfficialItem.L(
                OfficialItem.A("EQ-01"),
                OfficialItem.L(
                    OfficialItem.U2(0, ushort.MaxValue),
                    OfficialItem.Boolean(true, false))),
        };
        var send = officialGem.SendAsync(primary, cancellationToken);
        var received = await NextDataMessageAsync(events).ConfigureAwait(true);
        var incoming = received.IncomingMessage!;

        Assert.Equal(DeviceId, incoming.DataMessage.SessionId);
        Assert.Equal(1, incoming.DataMessage.Message.Stream);
        Assert.Equal(1, incoming.DataMessage.Message.Function);
        Assert.Equal(
            SecsItem.List(
                SecsItem.Ascii("EQ-01"),
                SecsItem.List(
                    SecsItem.U2(0, ushort.MaxValue),
                    SecsItem.Boolean(true, false))),
            incoming.DataMessage.Message.RootItem);
        await connection.ReplyAsync(
            incoming,
            new SecsMessage(
                1,
                2,
                rootItem: SecsItem.Binary(0)),
            cancellationToken).ConfigureAwait(true);

        using var secondary = await send.ConfigureAwait(true);
        Assert.Equal(1, secondary.S);
        Assert.Equal(2, secondary.F);
        Assert.Equal(new byte[] { 0 }, secondary.SecsItem!.GetMemory<byte>().ToArray());
    }

    private static void AssertOfficialPrimary(OfficialSecsMessage primary)
    {
        Assert.Equal(6, primary.S);
        Assert.Equal(11, primary.F);
        Assert.True(primary.ReplyExpected);
        var root = primary.SecsItem!;
        Assert.Equal("LOT-42", root[0].GetString());
        Assert.Equal(
            new byte[] { 0, 1, byte.MaxValue },
            root[1][0].GetMemory<byte>().ToArray());
        Assert.Equal(
            new[] { int.MinValue, int.MaxValue },
            root[1][1].GetMemory<int>().ToArray());
        Assert.Equal(
            new[] { false, true },
            root[1][2].GetMemory<bool>().ToArray());
    }

    private static async Task<HsmsConnectionEvent> NextDataMessageAsync(
        IAsyncEnumerator<HsmsConnectionEvent> events)
    {
        while (await events.MoveNextAsync().ConfigureAwait(true))
        {
            if (events.Current.Kind == HsmsConnectionEventKind.DataMessageReceived)
                return events.Current;
        }

        Assert.Fail("The expected secs4net Primary was not received.");
        return null!;
    }

    private static async Task WaitUntilOfficialSelectedAsync(
        OfficialHsmsConnection connection,
        CancellationToken cancellationToken)
    {
        if (connection.State == OfficialConnectionState.Selected)
            return;

        var selected = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStateChanged(object? sender, OfficialConnectionState state)
        {
            if (state == OfficialConnectionState.Selected)
                selected.TrySetResult(true);
        }

        connection.ConnectionChanged += OnStateChanged;
        try
        {
            if (connection.State == OfficialConnectionState.Selected)
                return;

            await selected.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            connection.ConnectionChanged -= OnStateChanged;
        }
    }

    private static HsmsConnectionOptions CreateSecsFrameOptions(
        int port,
        bool secs4NetIsActive)
        => new(
            IPAddress.Loopback,
            port,
            secs4NetIsActive
                ? HsmsConnectionMode.Passive
                : HsmsConnectionMode.Active,
            DeviceId,
            t3: TimeSpan.FromSeconds(5),
            t5: TimeSpan.FromMilliseconds(25),
            t6: TimeSpan.FromSeconds(2),
            t7: TimeSpan.FromSeconds(5),
            t8: TimeSpan.FromSeconds(2));

    private static OfficialSecsGemOptions CreateOfficialOptions(
        int port,
        bool isActive)
        => new()
        {
            DeviceId = DeviceId,
            IsActive = isActive,
            IpAddress = IPAddress.Loopback.ToString(),
            Port = port,
            T3 = 5000,
            T5 = 25,
            T6 = 2000,
            T7 = 5000,
            T8 = 2000,
            LinkTestInterval = 100,
        };

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(
            IPAddress.Loopback,
            0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class RecordingSecs4NetLogger : Secs4Net.ISecsGemLogger
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>>
            _infoMessages = new(StringComparer.Ordinal);

        public void Info(string message)
            => GetSignal(message).TrySetResult(true);

        public Task WaitForInfoAsync(
            string message,
            CancellationToken cancellationToken)
            => GetSignal(message).Task.WaitAsync(cancellationToken);

        private TaskCompletionSource<bool> GetSignal(string message)
            => _infoMessages.GetOrAdd(
                message,
                static _ => new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously));
    }
}
