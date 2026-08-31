using System.Net;
using SecsFrame.CommunicationDemo.Models;
using SecsFrame.Sml;

namespace SecsFrame.CommunicationDemo.Services;

internal sealed class CommunicationWorkspace : IAsyncDisposable
{
    private const int ActivityLimit = 500;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _activityGate = new();
    private readonly List<ActivityEntry> _activities = new();
    private readonly SmlMessageCodec _sml = new(
        maxNestingDepth: 32,
        maxItemCount: 10_000,
        maxValueCount: 100_000,
        maxTextLength: 1_000_000);
    private CancellationTokenSource? _sessionCancellation;
    private HsmsConnection? _connection;
    private DemoLoopbackPeer? _loopbackPeer;
    private Task? _eventPump;
    private long _nextActivityId;
    private int _disposed;

    public event EventHandler? Changed;

    public HsmsSessionState State { get; private set; } =
        HsmsSessionState.Disconnected;

    public bool IsBusy { get; private set; }

    public bool IsConnected => _connection is not null;

    public string Endpoint { get; private set; } = "未配置";

    public IReadOnlyList<ActivityEntry> Activities
    {
        get
        {
            lock (_activityGate)
                return _activities.ToArray();
        }
    }

    public async Task ConnectAsync(ConnectionDraft source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfDisposed();
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            SetBusy(true);
            await DisconnectCoreAsync(addActivity: false).ConfigureAwait(false);
            await StartConnectionAsync(source.Snapshot()).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            AddFailure("连接失败", error);
            await DisconnectCoreAsync(addActivity: false).ConfigureAwait(false);
            throw;
        }
        finally
        {
            SetBusy(false);
            _operationGate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            SetBusy(true);
            await DisconnectCoreAsync(addActivity: true).ConfigureAwait(false);
        }
        finally
        {
            SetBusy(false);
            _operationGate.Release();
        }
    }

    public async Task SendAsync(string text)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            SetBusy(true);
            await SendCoreAsync(text).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            AddFailure("发送失败", error);
            throw;
        }
        finally
        {
            SetBusy(false);
            _operationGate.Release();
        }
    }

    public Task LinktestAsync()
        => RunControlAsync(
            "Linktest",
            static connection => connection.LinktestAsync());

    public Task SeparateAsync()
        => RunControlAsync(
            "Separate",
            static connection => connection.SeparateAsync());

    public void ClearActivities()
    {
        lock (_activityGate)
            _activities.Clear();
        NotifyChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync(addActivity: false).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }

    private async Task StartConnectionAsync(ConnectionDraft draft)
    {
        var address = draft.UseLoopbackPeer
            ? IPAddress.Loopback
            : IPAddress.Parse(draft.IpAddress);
        var mode = draft.UseLoopbackPeer
            ? HsmsConnectionMode.Active
            : draft.ConnectionMode;
        var options = CreateOptions(draft, address, mode);
        var cancellation = new CancellationTokenSource();
        _sessionCancellation = cancellation;

        if (draft.UseLoopbackPeer)
            StartLoopbackPeer(draft);

        _connection = new HsmsConnection(options);
        _connection.Start();
        _eventPump = PumpEventsAsync(_connection, cancellation.Token);
        Endpoint = $"{options.ConnectionMode} " +
            $"{options.IpAddress}:{options.Port} / Session {options.SessionId}";
        AddActivity(
            ActivityEntryTone.Neutral,
            "连接",
            "连接已启动",
            Endpoint,
            null);

        using var selectionTimeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellation.Token);
        selectionTimeout.CancelAfter(
            options.T7 + options.T6 + TimeSpan.FromSeconds(2));
        await _connection.WaitUntilSelectedAsync(selectionTimeout.Token)
            .ConfigureAwait(false);
        State = HsmsSessionState.Selected;
        AddActivity(
            ActivityEntryTone.Success,
            "会话",
            "会话已选择",
            "可以发送数据消息或控制命令",
            null);
    }

    private void StartLoopbackPeer(ConnectionDraft draft)
    {
        _loopbackPeer = DemoLoopbackPeer.Start(
            CreateOptions(
                draft,
                IPAddress.Loopback,
                HsmsConnectionMode.Passive));
        _loopbackPeer.Failed += HandleLoopbackFailure;
        AddActivity(
            ActivityEntryTone.Neutral,
            "回环端",
            "本机回环端已启动",
            $"127.0.0.1:{draft.Port}",
            "回环端仅用于工程演示：收到 W-Bit 消息后返回同一 Body，" +
            "Function 加一；这不是设备消息 Profile。");
    }

    private async Task SendCoreAsync(string text)
    {
        var connection = RequireConnection();
        var message = _sml.Decode(text);
        AddActivity(
            ActivityEntryTone.Neutral,
            "发送",
            $"发送 S{message.Stream}F{message.Function}" +
                (message.ReplyExpected ? " W" : string.Empty),
            message.ReplyExpected ? "等待 Secondary" : "写出后完成",
            _sml.Encode(message));

        var secondary = await connection.SendAsync(message)
            .ConfigureAwait(false);
        if (secondary is null)
        {
            AddActivity(
                ActivityEntryTone.Success,
                "发送",
                "消息写出完成",
                "无 W-Bit，不等待回复",
                null);
            return;
        }

        AddActivity(
            ActivityEntryTone.Success,
            "接收",
            $"收到 S{secondary.Message.Stream}F" +
                $"{secondary.Message.Function}",
            $"System Bytes 0x{secondary.SystemBytes:X8}",
            _sml.Encode(secondary.Message));
    }

    private async Task RunControlAsync(
        string name,
        Func<HsmsConnection, Task> operation)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            SetBusy(true);
            var connection = RequireConnection();
            AddActivity(
                ActivityEntryTone.Neutral,
                "控制",
                $"发送 {name}",
                connection.State.ToString(),
                null);
            await operation(connection).ConfigureAwait(false);
            AddActivity(
                ActivityEntryTone.Success,
                "控制",
                $"{name} 完成",
                connection.State.ToString(),
                null);
        }
        catch (Exception error)
        {
            AddFailure($"{name} 失败", error);
            throw;
        }
        finally
        {
            SetBusy(false);
            _operationGate.Release();
        }
    }

    private async Task PumpEventsAsync(
        HsmsConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in connection
                .GetEventsAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                ProcessEvent(item);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            AddFailure("事件流结束", error);
        }
    }

    private void ProcessEvent(HsmsConnectionEvent item)
    {
        State = item.State;
        switch (item.Kind)
        {
            case HsmsConnectionEventKind.StateChanged:
                AddStateActivity(item);
                break;
            case HsmsConnectionEventKind.DataMessageReceived:
                AddIncomingActivity(item.IncomingMessage!.DataMessage);
                break;
            case HsmsConnectionEventKind.ControlMessageReceived:
                AddActivity(
                    ActivityEntryTone.Warning,
                    "控制",
                    "收到未认领控制消息",
                    item.State.ToString(),
                    null);
                break;
            case HsmsConnectionEventKind.DataMessageDecodeFailed:
                AddActivity(
                    ActivityEntryTone.Error,
                    "解码",
                    "数据消息解码失败",
                    item.Diagnostic?.Code.ToString() ??
                        item.Error?.GetType().Name ??
                        "未知错误",
                    FormatDiagnostic(item.Diagnostic));
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(item),
                    item.Kind,
                    "Unknown connection event kind.");
        }
    }

    private void AddStateActivity(HsmsConnectionEvent item)
        => AddActivity(
            item.Error is null
                ? ActivityEntryTone.Neutral
                : ActivityEntryTone.Warning,
            "状态",
            $"状态变为 {item.State}",
            item.Diagnostic?.Code.ToString() ??
                item.Error?.GetType().Name ??
                "正常转换",
            FormatDiagnostic(item.Diagnostic));

    private void AddIncomingActivity(HsmsDataMessage incoming)
        => AddActivity(
            ActivityEntryTone.Success,
            "接收",
            $"收到 S{incoming.Message.Stream}F" +
                $"{incoming.Message.Function}" +
                (incoming.Message.ReplyExpected ? " W" : string.Empty),
            $"System Bytes 0x{incoming.SystemBytes:X8}",
            _sml.Encode(incoming.Message));

    private async Task DisconnectCoreAsync(bool addActivity)
    {
        var connection = _connection;
        var peer = _loopbackPeer;
        var cancellation = _sessionCancellation;
        var eventPump = _eventPump;

        _connection = null;
        _loopbackPeer = null;
        _sessionCancellation = null;
        _eventPump = null;
        Endpoint = "未配置";
        State = HsmsSessionState.Disconnected;

        cancellation?.Cancel();
        if (connection is not null)
            await connection.DisposeAsync().ConfigureAwait(false);
        if (peer is not null)
        {
            peer.Failed -= HandleLoopbackFailure;
            await peer.DisposeAsync().ConfigureAwait(false);
        }
        await ObserveCompletionAsync(eventPump).ConfigureAwait(false);
        cancellation?.Dispose();

        if (addActivity)
        {
            AddActivity(
                ActivityEntryTone.Neutral,
                "连接",
                "连接已停止",
                "资源已释放",
                null);
        }
        else
        {
            NotifyChanged();
        }
    }

    private HsmsConnection RequireConnection()
        => _connection ??
            throw new InvalidOperationException("请先建立连接。");

    private static HsmsConnectionOptions CreateOptions(
        ConnectionDraft draft,
        IPAddress address,
        HsmsConnectionMode mode)
        => new(
            address,
            draft.Port,
            mode,
            checked((ushort)draft.SessionId),
            TimeSpan.FromSeconds(draft.T3Seconds),
            TimeSpan.FromSeconds(draft.T5Seconds),
            TimeSpan.FromSeconds(draft.T6Seconds),
            TimeSpan.FromSeconds(draft.T7Seconds),
            TimeSpan.FromSeconds(draft.T8Seconds));

    private void HandleLoopbackFailure(
        object? sender,
        LoopbackPeerFailedEventArgs args)
        => AddFailure("回环端停止", args.Error);

    private void AddFailure(string title, Exception error)
    {
        var diagnostic = HsmsDiagnostic.Classify(error, State);
        AddActivity(
            ActivityEntryTone.Error,
            "诊断",
            title,
            diagnostic?.Code.ToString() ?? error.GetType().Name,
            FormatDiagnostic(diagnostic) ?? error.Message);
    }

    private void AddActivity(
        ActivityEntryTone tone,
        string category,
        string title,
        string summary,
        string? detail)
    {
        var entry = new ActivityEntry(
            Interlocked.Increment(ref _nextActivityId),
            DateTimeOffset.Now,
            tone,
            category,
            title,
            summary,
            detail);
        lock (_activityGate)
        {
            _activities.Insert(0, entry);
            if (_activities.Count > ActivityLimit)
            {
                _activities.RemoveRange(
                    ActivityLimit,
                    _activities.Count - ActivityLimit);
            }
        }
        NotifyChanged();
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
            throw new ObjectDisposedException(nameof(CommunicationWorkspace));
    }

    private static string? FormatDiagnostic(HsmsDiagnostic? diagnostic)
        => diagnostic is null
            ? null
            : $"Code: {diagnostic.Code}\n" +
                $"Layer: {diagnostic.Layer}\n" +
                $"Operation: {diagnostic.Operation}\n" +
                $"State: {diagnostic.State}\n" +
                $"Timer: {diagnostic.Timer?.ToString() ?? "-"}\n" +
                $"Session: {diagnostic.ProtocolSessionId?.ToString() ?? "-"}\n" +
                "System Bytes: " +
                (diagnostic.SystemBytes is { } systemBytes
                    ? $"0x{systemBytes:X8}"
                    : "-");

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
