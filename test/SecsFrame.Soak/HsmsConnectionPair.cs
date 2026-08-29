using System.Net;
using System.Net.Sockets;

namespace SecsFrame.Soak;

internal sealed class HsmsConnectionPair : IAsyncDisposable
{
    private const ushort ProtocolSessionId = 10;
    private readonly int _port;
    private HsmsEndpointProbe _passive;
    private HsmsEndpointProbe _active;

    private HsmsConnectionPair(int port)
    {
        _port = port;
        _passive = CreateEndpoint(HsmsConnectionMode.Passive);
        _active = CreateEndpoint(HsmsConnectionMode.Active);
    }

    public static async Task<HsmsConnectionPair> CreateAsync(
        CancellationToken cancellationToken)
    {
        var pair = new HsmsConnectionPair(GetFreePort());
        try
        {
            await pair.WaitForRecoveryAsync(
                new RecoveryTargets(0, 0),
                cancellationToken).ConfigureAwait(false);
            return pair;
        }
        catch
        {
            await pair.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CycleResult> RunCycleAsync(
        int cycle,
        SoakFaultMode faultMode,
        CancellationToken cancellationToken)
    {
        var pending = _active.Connection.SendAsync(
            CreatePrimary(cycle, marker: 0),
            cancellationToken);
        var interrupted = await _passive.NextIncomingAsync(cancellationToken)
            .ConfigureAwait(false);
        ValidateMessage(interrupted.DataMessage, cycle, marker: 0);
        var targets = await InjectFaultAsync(faultMode, cancellationToken)
            .ConfigureAwait(false);
        var interruption = await ObserveInterruptionAsync(
            pending,
            cancellationToken).ConfigureAwait(false);

        await WaitForRecoveryAsync(targets, cancellationToken)
            .ConfigureAwait(false);
        var recoveredSystemBytes = await RoundTripAsync(
            cycle,
            cancellationToken).ConfigureAwait(false);
        return new CycleResult(
            interrupted.DataMessage.SystemBytes,
            interruption,
            recoveredSystemBytes);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _active.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await _passive.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<RecoveryTargets> InjectFaultAsync(
        SoakFaultMode faultMode,
        CancellationToken cancellationToken)
    {
        var targets = new RecoveryTargets(
            _passive.SelectionGeneration,
            _active.SelectionGeneration);
        switch (faultMode)
        {
            case SoakFaultMode.ActiveSeparate:
                await _active.Connection.SeparateAsync(cancellationToken)
                    .ConfigureAwait(false);
                break;
            case SoakFaultMode.PassiveSeparate:
                await _passive.Connection.SeparateAsync(cancellationToken)
                    .ConfigureAwait(false);
                break;
            case SoakFaultMode.RestartActiveEndpoint:
                await _active.DisposeAsync().ConfigureAwait(false);
                _active = CreateEndpoint(HsmsConnectionMode.Active);
                targets = targets with { Active = 0 };
                break;
            case SoakFaultMode.RestartPassiveEndpoint:
                await _passive.DisposeAsync().ConfigureAwait(false);
                _passive = CreateEndpoint(HsmsConnectionMode.Passive);
                targets = targets with { Passive = 0 };
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(faultMode),
                    faultMode,
                    "Unknown soak fault mode.");
        }

        return targets;
    }

    private async Task WaitForRecoveryAsync(
        RecoveryTargets targets,
        CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            _passive.WaitForSelectionAfterAsync(
                targets.Passive,
                cancellationToken),
            _active.WaitForSelectionAfterAsync(
                targets.Active,
                cancellationToken)).ConfigureAwait(false);
    }

    private async Task<uint> RoundTripAsync(
        int cycle,
        CancellationToken cancellationToken)
    {
        var expected = CreateBody(cycle, marker: 1);
        var send = _active.Connection.SendAsync(
            CreatePrimary(cycle, marker: 1),
            cancellationToken);
        var incoming = await _passive.NextIncomingAsync(cancellationToken)
            .ConfigureAwait(false);
        ValidateMessage(incoming.DataMessage, cycle, marker: 1);
        await _passive.Connection.ReplyAsync(
            incoming,
            new SecsMessage(6, 12, rootItem: expected),
            cancellationToken).ConfigureAwait(false);
        var secondary = await send.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!Equals(secondary?.Message.RootItem, expected))
            throw new InvalidOperationException("The replacement transaction returned an unexpected secondary.");
        return secondary.SystemBytes;
    }

    private HsmsEndpointProbe CreateEndpoint(HsmsConnectionMode mode)
        => HsmsEndpointProbe.Start(new HsmsConnectionOptions(
            IPAddress.Loopback,
            _port,
            mode,
            ProtocolSessionId,
            t3: TimeSpan.FromSeconds(3),
            t5: TimeSpan.FromMilliseconds(50),
            t6: TimeSpan.FromSeconds(3),
            t7: TimeSpan.FromSeconds(5),
            t8: TimeSpan.FromSeconds(3)));

    private static async Task<string> ObserveInterruptionAsync(
        Task<HsmsDataMessage?> pending,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("The pending transaction survived the injected session fault.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
            when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return ex.GetType().FullName ?? ex.GetType().Name;
        }
    }

    private static SecsMessage CreatePrimary(int cycle, byte marker)
        => new(6, 11, true, CreateBody(cycle, marker));

    private static SecsItem CreateBody(int cycle, byte marker)
        => SecsItem.List(SecsItem.U4((uint)cycle), SecsItem.U1(marker));

    private static void ValidateMessage(
        HsmsDataMessage message,
        int cycle,
        byte marker)
    {
        if (message.SessionId != ProtocolSessionId ||
            !Equals(message.Message.RootItem, CreateBody(cycle, marker)))
        {
            throw new InvalidOperationException("The received primary did not match the current soak cycle.");
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed record RecoveryTargets(long Passive, long Active);

    internal sealed record CycleResult(
        uint InterruptedSystemBytes,
        string InterruptionException,
        uint RecoveredSystemBytes);
}
