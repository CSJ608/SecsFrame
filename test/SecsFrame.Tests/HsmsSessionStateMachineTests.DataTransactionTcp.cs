using System.Net;
using StreamFrame;

namespace SecsFrame.Tests;

public sealed partial class HsmsSessionStateMachineTests
{
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

    private static (
        HsmsDataTransactionManager Passive,
        HsmsDataTransactionManager Active) CreateTcpTransactions()
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
                new HsmsDataTransactionOptions(T3)));
    }

    private static Task<HsmsDataTransactionEvent> NextSelectedTransactionEventAsync(
        IAsyncEnumerator<HsmsDataTransactionEvent> events)
        => NextMatchingTransactionEventAsync(
            events,
            item => item.Kind ==
                HsmsDataTransactionEventKind.StateChanged &&
                item.State == HsmsSessionState.Selected);
}
