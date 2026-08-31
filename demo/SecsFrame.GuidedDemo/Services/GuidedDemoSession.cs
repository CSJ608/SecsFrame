using System.Globalization;
using System.Net;
using System.Net.Sockets;
using SecsFrame.GuidedDemo.Models;
using SecsFrame.Sml;

namespace SecsFrame.GuidedDemo.Services;

internal sealed class GuidedDemoSession : IAsyncDisposable
{
    private const ushort ProtocolSessionId = 10;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _selectionGate = new();
    private readonly List<GuidedStepResult> _results = new();
    private readonly SmlMessageCodec _sml = new();
    private TaskCompletionSource<long> _nextActiveSelection =
        CreateSelectionSignal();
    private CancellationTokenSource? _sessionCancellation;
    private HsmsConnection? _active;
    private HsmsConnection? _passive;
    private Task? _activePump;
    private Task? _passivePump;
    private SecsMessage? _sampleMessage;
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
        _passive = new HsmsConnection(
            CreateOptions(port, HsmsConnectionMode.Passive));
        _active = new HsmsConnection(
            CreateOptions(port, HsmsConnectionMode.Active));
        _passive.Start();
        _passivePump = PumpPassiveEventsAsync(
            _passive,
            cancellation.Token);
        _active.Start();
        _activePump = PumpActiveEventsAsync(
            _active,
            cancellation.Token);

        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellation.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        await Task.WhenAll(
            _passive.WaitUntilSelectedAsync(timeout.Token),
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

    private async Task PumpActiveEventsAsync(
        HsmsConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in connection
                .GetEventsAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (item.Kind != HsmsConnectionEventKind.StateChanged)
                    continue;

                State = item.State;
                if (item.State == HsmsSessionState.Selected)
                    SignalActiveSelection();
                NotifyChanged();
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
        HsmsConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in connection
                .GetEventsAsync(cancellationToken)
                .ConfigureAwait(false))
            {
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
        HsmsConnection connection,
        HsmsIncomingDataMessage incoming,
        CancellationToken cancellationToken)
    {
        if (!incoming.ReplyExpected)
            return Task.CompletedTask;

        var primary = incoming.DataMessage.Message;
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
        var active = _active;
        var passive = _passive;
        var cancellation = _sessionCancellation;
        var activePump = _activePump;
        var passivePump = _passivePump;
        _active = null;
        _passive = null;
        _sessionCancellation = null;
        _activePump = null;
        _passivePump = null;

        cancellation?.Cancel();
        if (active is not null)
            await active.DisposeAsync().ConfigureAwait(false);
        if (passive is not null)
            await passive.DisposeAsync().ConfigureAwait(false);
        await ObserveCompletionAsync(activePump).ConfigureAwait(false);
        await ObserveCompletionAsync(passivePump).ConfigureAwait(false);
        cancellation?.Dispose();
        State = HsmsSessionState.Disconnected;
    }

    private HsmsConnection RequireActive()
        => _active ??
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
}
