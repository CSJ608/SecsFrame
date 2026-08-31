using System.Globalization;
using System.Net;
using System.Net.Sockets;
using SecsFrame.Gem;
using SecsFrame.GuidedDemo.Models;
using SecsFrame.Sml;
using SecsFrame.Trace;

namespace SecsFrame.GuidedDemo.Services;

internal sealed class GuidedDemoSession : IAsyncDisposable
{
    private const ushort ProtocolSessionId = 10;
    private const byte DiagnosticStream = 99;
    private const byte DiagnosticFunction = 1;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _selectionGate = new();
    private readonly List<GuidedStepResult> _results = new();
    private readonly SmlMessageCodec _sml = new();
    private TaskCompletionSource<long> _nextActiveSelection =
        CreateSelectionSignal();
    private CancellationTokenSource? _sessionCancellation;
    private SecsHost? _host;
    private SecsEquipment? _equipment;
    private GemHostServices? _hostGem;
    private GemEquipmentServices? _equipmentGem;
    private Task? _activePump;
    private Task? _passivePump;
    private SecsMessage? _sampleMessage;
    private SecsTraceRecord? _transactionTrace;
    private long _activeSelectionGeneration;
    private int _disposed;

    public event EventHandler? Changed;

    public IReadOnlyList<GuidedDemoStep> Steps => GuidedDemoStep.All;

    public IReadOnlyList<GuidedStepResult> Results => _results.ToArray();

    public GuidedStepResult? CurrentResult =>
        _results.Count == 0 ? null : _results[^1];

    public int CurrentStepIndex { get; private set; } = -1;

    public bool IsStarted => CurrentStepIndex >= 0;

    public bool IsBusy { get; private set; }

    public bool IsComplete { get; private set; }

    public string? Error { get; private set; }

    public HsmsSessionState State { get; private set; } =
        HsmsSessionState.Disconnected;

    public int ProgressPercent =>
        _results.Count * 100 / GuidedDemoStep.All.Count;

    public Task StartAsync() => RunStepAsync(reset: true);

    public Task NextAsync() => RunStepAsync(reset: false);

    public Task RestartAsync() => RunStepAsync(reset: true);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposePairAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }

    private async Task RunStepAsync(bool reset)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            SetBusy(true);
            if (reset)
                await ResetAsync().ConfigureAwait(false);
            if (IsComplete)
                return;

            await ExecuteNextStepAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Error = error.Message;
            NotifyChanged();
        }
        finally
        {
            SetBusy(false);
            _operationGate.Release();
        }
    }

    private async Task ResetAsync()
    {
        await DisposePairAsync().ConfigureAwait(false);
        _results.Clear();
        _sampleMessage = null;
        _transactionTrace = null;
        CurrentStepIndex = -1;
        IsComplete = false;
        Error = null;
        State = HsmsSessionState.Disconnected;
        lock (_selectionGate)
        {
            _activeSelectionGeneration = 0;
            _nextActiveSelection = CreateSelectionSignal();
        }
        NotifyChanged();
    }

    private async Task ExecuteNextStepAsync()
    {
        var index = _results.Count;
        if (index >= GuidedDemoStep.All.Count)
        {
            IsComplete = true;
            return;
        }

        CurrentStepIndex = index;
        Error = null;
        NotifyChanged();
        var result = index switch
        {
            0 => await EstablishSessionAsync().ConfigureAwait(false),
            1 => BuildMessage(),
            2 => await CompleteTransactionAsync().ConfigureAwait(false),
            3 => await RunLinktestAsync().ConfigureAwait(false),
            4 => await RecoverSessionAsync().ConfigureAwait(false),
            5 => await EstablishGemCommunicationAsync().ConfigureAwait(false),
            6 => await ReadDynamicStatusVariableAsync().ConfigureAwait(false),
            7 => BuildRedactedTrace(),
            8 => await CaptureT3DiagnosticAsync().ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                "The guided demo has no action for the current step."),
        };
        _results.Add(result);
        IsComplete = _results.Count == GuidedDemoStep.All.Count;
        NotifyChanged();
    }

    private async Task<GuidedStepResult> EstablishSessionAsync()
    {
        var port = GetFreePort();
        var cancellation = new CancellationTokenSource();
        _sessionCancellation = cancellation;
        _equipment = new SecsEquipment(
            CreateOptions(port, HsmsConnectionMode.Passive));
        _host = new SecsHost(
            CreateOptions(port, HsmsConnectionMode.Active));
        _equipment.Start();
        _passivePump = PumpPassiveEventsAsync(
            _equipment,
            cancellation.Token);
        _host.Start();
        _activePump = PumpActiveEventsAsync(
            _host,
            cancellation.Token);

        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellation.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        await Task.WhenAll(
            _equipment.WaitUntilSelectedAsync(timeout.Token),
            WaitForActiveSelectionAfterAsync(0, timeout.Token))
            .ConfigureAwait(false);

        return new GuidedStepResult(
            1,
            "真实会话已建立",
            "Active 与 Passive 通过本机 TCP 完成 Select。",
            new[]
            {
                new DemoEvidence("端点", $"127.0.0.1:{port}"),
                new DemoEvidence("拓扑", "Active -> Passive"),
                new DemoEvidence("Session ID", ProtocolSessionId.ToString()),
                new DemoEvidence("状态", State.ToString()),
            },
            null);
    }

    private GuidedStepResult BuildMessage()
    {
        _sampleMessage = new SecsMessage(
            6,
            11,
            replyExpected: true,
            rootItem: SecsItem.List(
                SecsItem.Ascii("DEMO-LOT-01"),
                SecsItem.U4(1001),
                SecsItem.List(
                    SecsItem.Boolean(true),
                    SecsItem.F8(23.5))));
        var text = _sml.Encode(_sampleMessage);
        return new GuidedStepResult(
            2,
            "动态消息已构造",
            "不可变 Item 树被写成确定性 SML 调试文本。",
            new[]
            {
                new DemoEvidence("消息", "S6F11 W"),
                new DemoEvidence("根 Item", "List [3]"),
                new DemoEvidence("嵌套深度", "3"),
                new DemoEvidence("文本长度", $"{text.Length} 字符"),
            },
            text);
    }

    private async Task<GuidedStepResult> CompleteTransactionAsync()
    {
        var active = RequireActive();
        var primary = _sampleMessage ??
            throw new InvalidOperationException("演示消息尚未构造。");
        var secondary = await active.SendAsync(primary)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException("演示事务没有返回 Secondary。");
        if (!Equals(primary.RootItem, secondary.Message.RootItem))
        {
            throw new InvalidOperationException(
                "演示回环返回的 Item Body 与 Primary 不一致。");
        }
        _transactionTrace = new SecsTraceRecord(
            DateTimeOffset.UtcNow,
            SecsTraceDirection.Sent,
            primary,
            ProtocolSessionId,
            secondary.SystemBytes);

        return new GuidedStepResult(
            3,
            "数据事务已完成",
            "W-Bit Primary 与匹配 Secondary 使用同一 System Bytes。",
            new[]
            {
                new DemoEvidence(
                    "回复",
                    $"S{secondary.Message.Stream}F{secondary.Message.Function}"),
                new DemoEvidence(
                    "System Bytes",
                    $"0x{secondary.SystemBytes:X8}"),
                new DemoEvidence("Body", "结构相等"),
                new DemoEvidence("会话", active.State.ToString()),
            },
            _sml.Encode(secondary.Message));
    }

    private async Task<GuidedStepResult> RunLinktestAsync()
    {
        var active = RequireActive();
        await active.LinktestAsync().ConfigureAwait(false);
        return new GuidedStepResult(
            4,
            "链路检查已完成",
            "Linktest Request 与匹配响应在 Selected 会话中完成。",
            new[]
            {
                new DemoEvidence("命令", "Linktest"),
                new DemoEvidence("结果", "响应已匹配"),
                new DemoEvidence("状态", active.State.ToString()),
                new DemoEvidence("数据 Body", "无"),
            },
            null);
    }

    private async Task<GuidedStepResult> RecoverSessionAsync()
    {
        var active = RequireActive();
        long previousGeneration;
        lock (_selectionGate)
            previousGeneration = _activeSelectionGeneration;

        await active.SeparateAsync().ConfigureAwait(false);
        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(
                _sessionCancellation?.Token ?? CancellationToken.None);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var recoveredGeneration = await WaitForActiveSelectionAfterAsync(
            previousGeneration,
            timeout.Token).ConfigureAwait(false);
        return new GuidedStepResult(
            5,
            "替换会话已恢复",
            "Separate 关闭旧会话，Active 重连后进入新的 Selected 代次。",
            new[]
            {
                new DemoEvidence(
                    "旧代次",
                    previousGeneration.ToString(
                        CultureInfo.InvariantCulture)),
                new DemoEvidence(
                    "新代次",
                    recoveredGeneration.ToString(
                        CultureInfo.InvariantCulture)),
                new DemoEvidence("状态", State.ToString()),
                new DemoEvidence("后台重放", "无"),
            },
            null);
    }

    private async Task<GuidedStepResult> EstablishGemCommunicationAsync()
    {
        var host = RequireActive();
        var equipment = _equipment ??
            throw new InvalidOperationException("Equipment 端点尚未建立。");
        if (_hostGem is not null || _equipmentGem is not null)
            throw new InvalidOperationException("GEM 服务已经建立。");

        var equipmentGem = new GemEquipmentServices(
            equipment,
            new GemIdentity("DEMO-EQUIPMENT", "1.0"),
            new DemoClock());
        var hostGem = new GemHostServices(
            host,
            new GemIdentity("DEMO-HOST", "1.0"));
        _equipmentGem = equipmentGem;
        _hostGem = hostGem;

        GemIdentity peer;
        try
        {
            peer = await hostGem.EstablishCommunicationAsync()
                .ConfigureAwait(false);
        }
        catch
        {
            _hostGem = null;
            _equipmentGem = null;
            hostGem.Dispose();
            equipmentGem.Dispose();
            throw;
        }

        var pair = hostGem.Profile.EstablishCommunication;
        return new GuidedStepResult(
            6,
            "GEM 通讯已建立",
            "Host 与 Equipment 通过现有 profile 完成一次身份交换。",
            new[]
            {
                new DemoEvidence(
                    "消息对",
                    $"S{pair.Stream}F{pair.PrimaryFunction} / " +
                        $"S{pair.Stream}F{pair.SecondaryFunction}"),
                new DemoEvidence("对端型号", peer.Model),
                new DemoEvidence(
                    "Host 状态",
                    hostGem.CommunicationState.ToString()),
                new DemoEvidence(
                    "Equipment 状态",
                    equipmentGem.CommunicationState.ToString()),
            },
            null);
    }

    private async Task<GuidedStepResult> ReadDynamicStatusVariableAsync()
    {
        var hostGem = _hostGem ??
            throw new InvalidOperationException("Host GEM 服务尚未建立。");
        var equipmentGem = _equipmentGem ??
            throw new InvalidOperationException("Equipment GEM 服务尚未建立。");
        var identifier = SecsItem.U4(1001);
        var providerReads = 0;
        using var registration = equipmentGem.RegisterStatusVariable(
            identifier,
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref providerReads);
                return ValueTask.FromResult(SecsItem.U4(73));
            });

        var values = await hostGem.ReadStatusVariablesAsync(
            new[] { identifier }).ConfigureAwait(false);
        if (values.Count != 1 || !values[0].Equals(SecsItem.U4(73)))
            throw new InvalidOperationException("动态状态变量值未按请求返回。");

        var pair = hostGem.Profile.ReadStatusVariables;
        var resultMessage = new SecsMessage(
            pair.Stream,
            pair.SecondaryFunction,
            rootItem: SecsItem.List(values));
        return new GuidedStepResult(
            7,
            "动态变量已读取",
            "Equipment 运行期提供器在真实请求中执行并返回动态 Item。",
            new[]
            {
                new DemoEvidence("SVID", "U4 1001"),
                new DemoEvidence("值", "U4 73"),
                new DemoEvidence(
                    "提供器执行",
                    providerReads.ToString(CultureInfo.InvariantCulture) + " 次"),
                new DemoEvidence(
                    "消息对",
                    $"S{pair.Stream}F{pair.PrimaryFunction} / " +
                        $"S{pair.Stream}F{pair.SecondaryFunction}"),
            },
            _sml.Encode(resultMessage),
            "解码值 SML");
    }

    private GuidedStepResult BuildRedactedTrace()
    {
        var source = _transactionTrace ??
            throw new InvalidOperationException("真实事务 Trace 源尚未生成。");
        var redactor = new SecsTraceRedactor(
            new[]
            {
                new SecsTraceRedactionRule(
                    source.Message.Stream,
                    source.Message.Function,
                    new[] { 0 },
                    SecsItem.Ascii("REDACTED")),
            });
        var redacted = redactor.Redact(source);
        var codec = new SecsTraceCodec();
        var text = codec.Encode(new[] { redacted });
        var decoded = codec.Decode(text);
        if (decoded.Count != 1 ||
            text.Contains("DEMO-LOT-01", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "脱敏 Trace 未通过严格读取或明文复核。");
        }

        return new GuidedStepResult(
            8,
            "脱敏 Trace 已导出",
            "Item 路径规则替换敏感值，严格 codec 完成一次往返。",
            new[]
            {
                new DemoEvidence("格式", SecsTraceCodec.FormatIdentifier),
                new DemoEvidence("规则", "S6F11 / Item [0]"),
                new DemoEvidence("明文复核", "DEMO-LOT-01 未出现"),
                new DemoEvidence(
                    "System Bytes",
                    $"0x{source.SystemBytes:X8}"),
            },
            text,
            "Trace 证据");
    }

    private async Task<GuidedStepResult> CaptureT3DiagnosticAsync()
    {
        var host = RequireActive();
        HsmsDiagnostic? diagnostic = null;
        try
        {
            _ = await host.SendAsync(
                new SecsMessage(
                    DiagnosticStream,
                    DiagnosticFunction,
                    replyExpected: true,
                    rootItem: SecsItem.Ascii("NO-REPLY")))
                .ConfigureAwait(false);
        }
        catch (Exception error)
        {
            diagnostic = HsmsDiagnostic.Classify(error, host.State);
            if (diagnostic is null)
                throw;
        }

        if (diagnostic?.Code != HsmsDiagnosticCode.T3Timeout)
            throw new InvalidOperationException("预期的 T3 诊断没有出现。");

        var record = SecsTraceDiagnosticRecord.Create(
            DateTimeOffset.UtcNow,
            diagnostic);
        var codec = new SecsTraceDiagnosticCodec();
        var text = codec.Encode(new[] { record });
        var decoded = codec.Decode(text);
        if (decoded.Count != 1 ||
            decoded[0].Code != HsmsDiagnosticCode.T3Timeout)
        {
            throw new InvalidOperationException("诊断 Trace 严格读取失败。");
        }

        return new GuidedStepResult(
            9,
            "T3 诊断已捕获",
            "未回复事务真实等待 T3，导出只保留稳定诊断标量。",
            new[]
            {
                new DemoEvidence("代码", diagnostic.Code.ToString()),
                new DemoEvidence("层级", diagnostic.Layer.ToString()),
                new DemoEvidence("计时器", diagnostic.Timer?.ToString() ?? "-"),
                new DemoEvidence(
                    "System Bytes",
                    diagnostic.SystemBytes is { } systemBytes
                        ? $"0x{systemBytes:X8}"
                        : "-"),
            },
            text,
            "诊断 Trace");
    }

    private async Task PumpActiveEventsAsync(
        SecsHost connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in connection
                .GetEventsAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (item.Kind == HsmsConnectionEventKind.StateChanged)
                {
                    State = item.State;
                    if (item.State == HsmsSessionState.Selected)
                        SignalActiveSelection();
                    NotifyChanged();
                }

                var gem = _hostGem;
                if (gem is not null)
                {
                    _ = await gem.TryDispatchAsync(
                        item,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Error = error.Message;
            NotifyChanged();
        }
    }

    private async Task PumpPassiveEventsAsync(
        SecsEquipment connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in connection
                .GetEventsAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                var gem = _equipmentGem;
                if (gem is not null &&
                    await gem.TryDispatchAsync(
                        item,
                        cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                if (item.Kind != HsmsConnectionEventKind.DataMessageReceived)
                    continue;

                await ReplyToPrimaryAsync(
                    connection,
                    item.IncomingMessage!,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Error = error.Message;
            NotifyChanged();
        }
    }

    private static Task ReplyToPrimaryAsync(
        SecsEquipment connection,
        HsmsIncomingDataMessage incoming,
        CancellationToken cancellationToken)
    {
        if (!incoming.ReplyExpected)
            return Task.CompletedTask;

        var primary = incoming.DataMessage.Message;
        if (primary.Stream == DiagnosticStream &&
            primary.Function == DiagnosticFunction)
        {
            return Task.CompletedTask;
        }

        var function = primary.Function == byte.MaxValue
            ? primary.Function
            : (byte)(primary.Function + 1);
        return connection.ReplyAsync(
            incoming,
            new SecsMessage(
                primary.Stream,
                function,
                rootItem: primary.RootItem),
            cancellationToken);
    }

    private async Task<long> WaitForActiveSelectionAfterAsync(
        long previousGeneration,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            Task<long> signal;
            lock (_selectionGate)
            {
                if (_activeSelectionGeneration > previousGeneration)
                    return _activeSelectionGeneration;
                signal = _nextActiveSelection.Task;
            }

            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void SignalActiveSelection()
    {
        TaskCompletionSource<long> signal;
        long generation;
        lock (_selectionGate)
        {
            generation = ++_activeSelectionGeneration;
            signal = _nextActiveSelection;
            _nextActiveSelection = CreateSelectionSignal();
        }
        signal.TrySetResult(generation);
    }

    private async Task DisposePairAsync()
    {
        var host = _host;
        var equipment = _equipment;
        var hostGem = _hostGem;
        var equipmentGem = _equipmentGem;
        var cancellation = _sessionCancellation;
        var activePump = _activePump;
        var passivePump = _passivePump;
        _host = null;
        _equipment = null;
        _hostGem = null;
        _equipmentGem = null;
        _sessionCancellation = null;
        _activePump = null;
        _passivePump = null;

        hostGem?.Dispose();
        equipmentGem?.Dispose();
        cancellation?.Cancel();
        if (host is not null)
            await host.DisposeAsync().ConfigureAwait(false);
        if (equipment is not null)
            await equipment.DisposeAsync().ConfigureAwait(false);
        await ObserveCompletionAsync(activePump).ConfigureAwait(false);
        await ObserveCompletionAsync(passivePump).ConfigureAwait(false);
        cancellation?.Dispose();
        State = HsmsSessionState.Disconnected;
    }

    private SecsHost RequireActive()
        => _host ??
            throw new InvalidOperationException(
                "演示连接尚未建立。");

    private static HsmsConnectionOptions CreateOptions(
        int port,
        HsmsConnectionMode mode)
        => new(
            IPAddress.Loopback,
            port,
            mode,
            ProtocolSessionId,
            t3: TimeSpan.FromSeconds(3),
            t5: TimeSpan.FromMilliseconds(50),
            t6: TimeSpan.FromSeconds(3),
            t7: TimeSpan.FromSeconds(5),
            t8: TimeSpan.FromSeconds(3));

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        NotifyChanged();
    }

    private void NotifyChanged()
        => Changed?.Invoke(this, EventArgs.Empty);

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(GuidedDemoSession));
    }

    private static TaskCompletionSource<long> CreateSelectionSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task ObserveCompletionAsync(Task? task)
    {
        if (task is null)
            return;

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class DemoClock : IGemClock
    {
        public ValueTask<DateTimeOffset> GetCurrentTimeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(DateTimeOffset.UtcNow);
        }

        public ValueTask<bool> SetCurrentTimeAsync(
            DateTimeOffset value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }
    }
}
