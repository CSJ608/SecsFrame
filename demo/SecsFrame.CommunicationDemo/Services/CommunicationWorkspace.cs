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
    private readonly List<PendingReply> _pendingReplies = new();
    private readonly Dictionary<long, HsmsIncomingDataMessage> _replyTokens =
        new();
    private readonly List<MessageFavorite> _favorites = new();
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
    private long _nextPendingReplyId;
    private long _nextFavoriteId;
    private int _loopbackIncomingInFlight;
    private int _disposed;

    public event EventHandler? Changed;

    public HsmsSessionState State { get; private set; } =
        HsmsSessionState.Disconnected;

    public bool IsBusy { get; private set; }

    public bool IsConnected => _connection is not null;

    public bool IsLoopbackPeerActive => _loopbackPeer is not null;

    public bool IsLoopbackIncomingPending =>
        Volatile.Read(ref _loopbackIncomingInFlight) != 0;

    public string Endpoint { get; private set; } = "未配置";

    public IReadOnlyList<ActivityEntry> Activities
    {
        get
        {
            lock (_activityGate)
                return _activities.ToArray();
        }
    }

    public IReadOnlyList<PendingReply> PendingReplies
    {
        get
        {
            lock (_activityGate)
                return _pendingReplies.ToArray();
        }
    }

    public IReadOnlyList<MessageFavorite> Favorites
    {
        get
        {
            lock (_activityGate)
                return _favorites.ToArray();
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

    public async Task ReplyAsync(long pendingReplyId, string text)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            SetBusy(true);
            var connection = RequireConnection();
            var secondary = _sml.Decode(text);
            if (secondary.ReplyExpected)
            {
                throw new ArgumentException(
                    "Secondary 消息不能设置 W-Bit。",
                    nameof(text));
            }

            HsmsIncomingDataMessage incoming;
            lock (_activityGate)
            {
                if (!_replyTokens.Remove(pendingReplyId, out var found))
                    throw new InvalidOperationException("待回复消息已失效或已回复。");

                incoming = found;
                _pendingReplies.RemoveAll(
                    item => item.Id == pendingReplyId);
            }
            NotifyChanged();

            AddActivity(
                ActivityEntryTone.Neutral,
                "回复",
                $"回复 S{secondary.Stream}F{secondary.Function}",
                $"待回复 #{pendingReplyId}",
                _sml.Encode(secondary),
                ActivityDetailKind.ProtocolMessage);
            await connection.ReplyAsync(incoming, secondary)
                .ConfigureAwait(false);
            AddActivity(
                ActivityEntryTone.Success,
                "回复",
                "Secondary 写出完成",
                $"System Bytes 0x{incoming.DataMessage.SystemBytes:X8}",
                null);
        }
        catch (Exception error)
        {
            AddFailure("回复失败", error);
            throw;
        }
        finally
        {
            SetBusy(false);
            _operationGate.Release();
        }
    }

    public MessageFavorite AddFavorite(string name, string text)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("请输入收藏名称。", nameof(name));

        var normalizedName = name.Trim();
        if (normalizedName.Length > 80)
            throw new ArgumentException("收藏名称不能超过 80 个字符。", nameof(name));

        var normalizedSml = _sml.Encode(_sml.Decode(text));
        MessageFavorite favorite;
        lock (_activityGate)
        {
            if (_favorites.Any(
                    item => string.Equals(
                        item.Name,
                        normalizedName,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("已存在同名收藏。");
            }

            favorite = new MessageFavorite(
                Interlocked.Increment(ref _nextFavoriteId),
                normalizedName,
                normalizedSml);
            _favorites.Add(favorite);
        }
        NotifyChanged();
        return favorite;
    }

    public void RemoveFavorite(long favoriteId)
    {
        ThrowIfDisposed();
        lock (_activityGate)
            _favorites.RemoveAll(item => item.Id == favoriteId);
        NotifyChanged();
    }

    public void QueueLoopbackPrimary()
    {
        ThrowIfDisposed();
        var peer = _loopbackPeer ??
            throw new InvalidOperationException("本机回环端未启用。");
        var cancellation = _sessionCancellation ??
            throw new InvalidOperationException("当前连接尚未启动。");
        if (State != HsmsSessionState.Selected)
            throw new InvalidOperationException("入站模拟需要 Selected 会话。");
        if (Interlocked.CompareExchange(
                ref _loopbackIncomingInFlight,
                1,
                0) != 0)
        {
            throw new InvalidOperationException("已有一条回环入站消息等待回复。");
        }

        var primary = new SecsMessage(
            1,
            1,
            replyExpected: true,
            rootItem: SecsItem.List(
                SecsItem.Ascii("REPLY-DEMO"),
                SecsItem.U4(42)));
        AddActivity(
            ActivityEntryTone.Neutral,
            "回环端",
            "发送模拟入站 S1F1 W",
            "等待通讯工具显式回复",
            _sml.Encode(primary),
            ActivityDetailKind.ProtocolMessage);
        _ = ObserveLoopbackPrimaryAsync(
            peer,
            primary,
            cancellation.Token);
        NotifyChanged();
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
            "Function 加一；这不是设备消息 Profile。",
            ActivityDetailKind.BoundaryNote);
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
            _sml.Encode(message),
            ActivityDetailKind.ProtocolMessage);

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
            _sml.Encode(secondary.Message),
            ActivityDetailKind.ProtocolMessage);
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
                if (item.State != HsmsSessionState.Selected)
                    ClearPendingReplies();
                AddStateActivity(item);
                break;
            case HsmsConnectionEventKind.DataMessageReceived:
                AddIncomingActivity(item.IncomingMessage!);
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
                    FormatDiagnostic(item.Diagnostic),
                    ActivityDetailKind.DiagnosticMetadata);
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
            FormatDiagnostic(item.Diagnostic),
            ActivityDetailKind.DiagnosticMetadata);

    private void AddIncomingActivity(HsmsIncomingDataMessage incoming)
    {
        var dataMessage = incoming.DataMessage;
        long? pendingReplyId = null;
        if (incoming.ReplyExpected)
        {
            pendingReplyId = Interlocked.Increment(ref _nextPendingReplyId);
            var primary = dataMessage.Message;
            var function = primary.Function == byte.MaxValue
                ? primary.Function
                : (byte)(primary.Function + 1);
            var suggestedSecondary = new SecsMessage(
                primary.Stream,
                function,
                rootItem: primary.RootItem);
            var pending = new PendingReply(
                pendingReplyId.Value,
                DateTimeOffset.Now,
                dataMessage.SessionId,
                dataMessage.SystemBytes,
                primary.Stream,
                primary.Function,
                _sml.Encode(suggestedSecondary));
            lock (_activityGate)
            {
                _pendingReplies.Add(pending);
                _replyTokens.Add(pending.Id, incoming);
            }
        }

        AddActivity(
            ActivityEntryTone.Success,
            "接收",
            $"收到 S{dataMessage.Message.Stream}F" +
                $"{dataMessage.Message.Function}" +
                (dataMessage.Message.ReplyExpected ? " W" : string.Empty),
            pendingReplyId is null
                ? $"System Bytes 0x{dataMessage.SystemBytes:X8}"
                : $"待回复 #{pendingReplyId} / System Bytes " +
                    $"0x{dataMessage.SystemBytes:X8}",
            _sml.Encode(dataMessage.Message),
            ActivityDetailKind.ProtocolMessage);
    }

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
        ClearPendingReplies();

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
        Interlocked.Exchange(ref _loopbackIncomingInFlight, 0);

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

    private async Task ObserveLoopbackPrimaryAsync(
        DemoLoopbackPeer peer,
        SecsMessage primary,
        CancellationToken cancellationToken)
    {
        try
        {
            var secondary = await peer.SendAsync(primary, cancellationToken)
                .ConfigureAwait(false) ??
                throw new InvalidOperationException(
                    "回环入站 Primary 未获得 Secondary。");
            AddActivity(
                ActivityEntryTone.Success,
                "回环端",
                "收到通讯工具回复",
                $"S{secondary.Message.Stream}F{secondary.Message.Function} / " +
                    $"System Bytes 0x{secondary.SystemBytes:X8}",
                _sml.Encode(secondary.Message),
                ActivityDetailKind.ProtocolMessage);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            AddFailure("回环入站事务失败", error);
        }
        finally
        {
            Interlocked.Exchange(ref _loopbackIncomingInFlight, 0);
            NotifyChanged();
        }
    }

    private void ClearPendingReplies()
    {
        lock (_activityGate)
        {
            _pendingReplies.Clear();
            _replyTokens.Clear();
        }
    }

    private void AddFailure(string title, Exception error)
    {
        var diagnostic = HsmsDiagnostic.Classify(error, State);
        AddActivity(
            ActivityEntryTone.Error,
            "诊断",
            title,
            diagnostic?.Code.ToString() ?? error.GetType().Name,
            FormatDiagnostic(diagnostic) ?? error.Message,
            diagnostic is null
                ? ActivityDetailKind.None
                : ActivityDetailKind.DiagnosticMetadata);
    }

    private void AddActivity(
        ActivityEntryTone tone,
        string category,
        string title,
        string summary,
        string? detail,
        ActivityDetailKind detailKind = ActivityDetailKind.None)
    {
        var entry = new ActivityEntry(
            Interlocked.Increment(ref _nextActivityId),
            DateTimeOffset.Now,
            tone,
            category,
            title,
            summary,
            detail,
            detailKind);
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
