using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace SecsFrame;

internal sealed class HsmsSessionStateMachine : IAsyncDisposable
{
    private readonly IHsmsTransport _transport;
    private readonly HsmsSessionOptions _options;
    private readonly IHsmsTransportTimerFactory _timerFactory;
    private readonly IHsmsSystemBytesProvider _systemBytesProvider;
    private readonly Channel<MachineInput> _inputs;
    private readonly Channel<HsmsSessionEvent> _events;
    private readonly HashSet<PendingDataSend> _pendingDataSends = new();
    private CancellationTokenSource? _lifetime;
    private Task? _transportPump;
    private Task? _processor;
    private IHsmsTransportTimer? _t6Timer;
    private IHsmsTransportTimer? _t7Timer;
    private HsmsTransportSessionId _sessionId;
    private uint? _pendingSelectSystemBytes;
    private PendingControlCommand? _pendingControlCommand;
    private TaskCompletionSource<bool>? _separateCompletion;
    private int _t6Generation;
    private int _t7Generation;
    private int _state;
    private int _started;
    private int _disposed;

    public HsmsSessionStateMachine(
        IHsmsTransport transport,
        HsmsSessionOptions options,
        IHsmsTransportTimerFactory? timerFactory = null,
        IHsmsSystemBytesProvider? systemBytesProvider = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timerFactory = timerFactory ?? SystemHsmsTransportTimerFactory.Instance;
        _systemBytesProvider = systemBytesProvider ?? new IncrementingHsmsSystemBytesProvider();
        _inputs = Channel.CreateUnbounded<MachineInput>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _events = Channel.CreateUnbounded<HsmsSessionEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
    }

    public HsmsSessionState State
        => (HsmsSessionState)Volatile.Read(ref _state);

    public void Start(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The HSMS session state machine has already been started.");

        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processor = ProcessInputsAsync();
        _transportPump = PumpTransportEventsAsync(_lifetime.Token);
        try
        {
            _transport.Start(_lifetime.Token);
        }
        catch (Exception ex)
        {
            _lifetime.Cancel();
            _inputs.Writer.TryComplete(ex);
            throw;
        }
    }

    public async IAsyncEnumerable<HsmsSessionEvent> GetEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = _events.Reader;
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var sessionEvent))
                yield return sessionEvent;
        }
    }

    public Task SeparateAsync(CancellationToken cancellationToken = default)
        => RequestCommandAsync(MachineInputKind.SeparateRequested, cancellationToken);

    public Task LinktestAsync(CancellationToken cancellationToken = default)
        => RequestCommandAsync(MachineInputKind.LinktestRequested, cancellationToken);

    public Task DeselectAsync(CancellationToken cancellationToken = default)
        => RequestCommandAsync(MachineInputKind.DeselectRequested, cancellationToken);

    public Task SendDataAsync(
        HsmsFrame frame,
        CancellationToken cancellationToken = default)
    {
        if (frame is null)
            throw new ArgumentNullException(nameof(frame));
        if (!frame.Header.IsDataMessage)
        {
            throw new ArgumentException(
                "Only an HSMS data message can be sent through the data send path.",
                nameof(frame));
        }

        if (frame.Header.PresentationType != 0)
        {
            throw new HsmsProtocolException(
                "SECS-II over HSMS requires a data message with PType 0.");
        }

        ThrowIfDisposed();
        if (Volatile.Read(ref _started) == 0)
            throw new InvalidOperationException("The HSMS session state machine has not been started.");
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inputs.Writer.TryWrite(
            MachineInput.DataSendRequested(
                new PendingDataSend(frame, completion),
                cancellationToken)))
        {
            throw new InvalidOperationException("The HSMS session state machine is no longer accepting commands.");
        }

        return completion.Task;
    }

    private Task RequestCommandAsync(
        MachineInputKind kind,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _started) == 0)
            throw new InvalidOperationException("The HSMS session state machine has not been started.");
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inputs.Writer.TryWrite(
            MachineInput.CommandRequested(kind, completion, cancellationToken)))
        {
            throw new InvalidOperationException("The HSMS session state machine is no longer accepting commands.");
        }

        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _lifetime?.Cancel();
        await _transport.DisposeAsync().ConfigureAwait(false);
        _inputs.Writer.TryComplete();

        if (_transportPump is not null)
        {
            try
            {
                await _transportPump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_processor is not null)
            await _processor.ConfigureAwait(false);

        CancelT6();
        CancelT7();
        FailPendingDataSends(new ObjectDisposedException(nameof(HsmsSessionStateMachine)));
        _pendingControlCommand?.Completion.TrySetCanceled();
        _separateCompletion?.TrySetCanceled();
        _events.Writer.TryComplete();
        _lifetime?.Dispose();
    }

    private async Task PumpTransportEventsAsync(CancellationToken cancellationToken)
    {
        var events = _transport.GetEventsAsync(cancellationToken).GetAsyncEnumerator();
        try
        {
            while (await events.MoveNextAsync().ConfigureAwait(false))
            {
                if (!_inputs.Writer.TryWrite(MachineInput.Transport(events.Current)))
                    break;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                _inputs.Writer.TryWrite(
                    MachineInput.TransportFailed(
                        new IOException(
                            "The HSMS transport event stream ended before the state machine was stopped.")));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _inputs.Writer.TryWrite(MachineInput.TransportFailed(ex));
        }
        finally
        {
            await events.DisposeAsync().ConfigureAwait(false);
            _inputs.Writer.TryComplete();
        }
    }

    private async Task ProcessInputsAsync()
    {
        try
        {
            var reader = _inputs.Reader;
            while (await reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
            {
                while (reader.TryRead(out var input))
                    ProcessInput(input);
            }
        }
        finally
        {
            CancelT6();
            CancelT7();
            _events.Writer.TryComplete();
        }
    }

    private void ProcessInput(MachineInput input)
    {
        switch (input.Kind)
        {
            case MachineInputKind.Transport:
                ProcessTransportEvent(input.TransportEvent);
                break;
            case MachineInputKind.SendCompleted:
                ProcessSendCompleted(input.SendOperation);
                break;
            case MachineInputKind.SendFailed:
                ProcessSendFailed(input.SendOperation, input.Error!);
                break;
            case MachineInputKind.T6Expired:
                ProcessT6Expired(input.Generation);
                break;
            case MachineInputKind.T7Expired:
                ProcessT7Expired(input.Generation);
                break;
            case MachineInputKind.SeparateRequested:
                ProcessSeparateRequested(input.Completion!, input.CancellationToken);
                break;
            case MachineInputKind.LinktestRequested:
                ProcessControlRequested(
                    input.Completion!,
                    input.CancellationToken,
                    HsmsMessageType.LinktestRequest,
                    HsmsMessageType.LinktestResponse,
                    SendPurpose.LinktestRequest);
                break;
            case MachineInputKind.DeselectRequested:
                ProcessControlRequested(
                    input.Completion!,
                    input.CancellationToken,
                    HsmsMessageType.DeselectRequest,
                    HsmsMessageType.DeselectResponse,
                    SendPurpose.DeselectRequest);
                break;
            case MachineInputKind.DataSendRequested:
                ProcessDataSendRequested(
                    input.DataSend!,
                    input.CancellationToken);
                break;
            case MachineInputKind.DataSendCompleted:
                ProcessDataSendCompleted(input.DataSend!);
                break;
            case MachineInputKind.DataSendFailed:
                ProcessDataSendFailed(input.DataSend!, input.Error!);
                break;
            case MachineInputKind.TransportFailed:
                ProcessTransportFailure(input.Error!);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input), input.Kind, "Unknown state-machine input.");
        }
    }

    private void ProcessTransportEvent(HsmsTransportEvent transportEvent)
    {
        switch (transportEvent.Kind)
        {
            case HsmsTransportEventKind.SessionOpened:
                ProcessSessionOpened(transportEvent.SessionId);
                break;
            case HsmsTransportEventKind.FrameReceived:
                if (transportEvent.SessionId == _sessionId)
                    ProcessFrame(transportEvent.Frame!);
                break;
            case HsmsTransportEventKind.SessionClosed:
                if (transportEvent.SessionId == _sessionId)
                    ProcessSessionClosed(transportEvent.Error);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(transportEvent),
                    transportEvent.Kind,
                    "Unknown transport event.");
        }
    }

    private void ProcessSessionOpened(HsmsTransportSessionId sessionId)
    {
        if (_sessionId.IsValid)
        {
            ProcessSessionClosed(
                new HsmsProtocolException(
                    $"Transport session {sessionId.Value} opened before session {_sessionId.Value} closed."));
        }

        _sessionId = sessionId;
        _pendingSelectSystemBytes = null;
        _pendingControlCommand = null;
        Transition(HsmsSessionState.Connected);
        ArmT7();

        if (_options.ConnectionMode != HsmsConnectionMode.Active)
            return;

        Transition(HsmsSessionState.Selecting);
        var systemBytes = _systemBytesProvider.Next();
        _pendingSelectSystemBytes = systemBytes;
        StartControlSend(
            new SendOperation(
                sessionId,
                SendPurpose.SelectRequest,
                systemBytes,
                0,
                0));
    }

    private void ProcessSessionClosed(Exception? error)
    {
        if (!_sessionId.IsValid)
            return;

        var closedSessionId = _sessionId;
        CancelT6();
        CancelT7();
        _pendingSelectSystemBytes = null;
        var closeError =
            error ?? new HsmsTransportSessionExpiredException(closedSessionId);
        FailPendingDataSends(closeError, closedSessionId);
        _pendingControlCommand?.Completion.TrySetException(closeError);
        _pendingControlCommand = null;
        _sessionId = default;
        _separateCompletion?.TrySetException(closeError);
        _separateCompletion = null;
        Transition(closedSessionId, HsmsSessionState.Disconnected, error);
    }

    private void ProcessFrame(HsmsFrame frame)
    {
        var header = frame.Header;
        if (header.PresentationType != 0)
        {
            SendReject(header, HsmsRejectReason.UnsupportedPresentationType);
            return;
        }

        if (header.IsDataMessage)
        {
            if (State != HsmsSessionState.Selected)
            {
                SendReject(header, HsmsRejectReason.EntityNotSelected);
                return;
            }

            _events.Writer.TryWrite(
                HsmsSessionEvent.DataMessageReceived(_sessionId, frame));
            return;
        }

        ProcessControlFrame(frame);
    }

    private void ProcessControlFrame(HsmsFrame frame)
    {
        var header = frame.Header;
        if (header.SessionId != ushort.MaxValue)
        {
            AbortCurrentSession(
                new HsmsProtocolException(
                    "An HSMS control message must use Session ID 0xFFFF."));
            return;
        }

        if (header.MessageType == HsmsMessageType.RejectRequest)
        {
            ProcessRejectRequest(frame);
            return;
        }

        if (header.HeaderByte2 != 0)
        {
            AbortCurrentSession(
                new HsmsProtocolException(
                    "An HSMS control message other than Reject Request must use header byte 2 value zero."));
            return;
        }

        switch (header.MessageType)
        {
            case HsmsMessageType.SelectRequest:
                ProcessSelectRequest(header);
                break;
            case HsmsMessageType.SelectResponse:
                ProcessSelectResponse(header);
                break;
            case HsmsMessageType.DeselectRequest:
                ProcessDeselectRequest(header);
                break;
            case HsmsMessageType.DeselectResponse:
                ProcessDeselectResponse(header);
                break;
            case HsmsMessageType.LinktestRequest:
                ProcessLinktestRequest(header);
                break;
            case HsmsMessageType.LinktestResponse:
                ProcessLinktestResponse(header);
                break;
            case HsmsMessageType.SeparateRequest:
                ProcessSeparateRequest(header);
                break;
            default:
                SendReject(header, HsmsRejectReason.UnsupportedSessionType);
                break;
        }
    }

    private void ProcessSelectRequest(HsmsMessageHeader header)
    {
        if (header.HeaderByte3 != 0)
        {
            AbortCurrentSession(
                new HsmsProtocolException("Select Request must use status byte zero."));
            return;
        }

        var status = State == HsmsSessionState.Selected
            ? HsmsSelectStatus.AlreadySelected
            : HsmsSelectStatus.Success;
        StartControlSend(
            new SendOperation(
                _sessionId,
                SendPurpose.SelectResponse,
                header.SystemBytes,
                0,
                (byte)status));
    }

    private void ProcessSelectResponse(HsmsMessageHeader header)
    {
        if (_pendingSelectSystemBytes is not { } expectedSystemBytes ||
            header.SystemBytes != expectedSystemBytes)
        {
            SendReject(header, HsmsRejectReason.TransactionNotOpen);
            return;
        }

        _pendingSelectSystemBytes = null;
        CancelT6();
        var status = DecodeSelectStatus(header.HeaderByte3);
        if (status == HsmsSelectStatus.Success)
        {
            EnterSelected();
            return;
        }

        Transition(
            HsmsSessionState.Connected,
            new HsmsSelectionRejectedException(status));
    }

    private void ProcessLinktestRequest(HsmsMessageHeader header)
    {
        if (header.HeaderByte3 != 0)
        {
            AbortCurrentSession(
                new HsmsProtocolException("Linktest Request must use status byte zero."));
            return;
        }

        StartControlSend(
            new SendOperation(
                _sessionId,
                SendPurpose.LinktestResponse,
                header.SystemBytes,
                0,
                0));
    }

    private void ProcessLinktestResponse(HsmsMessageHeader header)
    {
        if (header.HeaderByte3 != 0)
        {
            AbortCurrentSession(
                new HsmsProtocolException("Linktest Response must use status byte zero."));
            return;
        }

        if (!TryTakePendingControlResponse(
            header,
            HsmsMessageType.LinktestResponse,
            out var pending))
        {
            return;
        }

        pending.Completion.TrySetResult(true);
    }

    private void ProcessDeselectRequest(HsmsMessageHeader header)
    {
        if (header.HeaderByte3 != 0)
        {
            AbortCurrentSession(
                new HsmsProtocolException("Deselect Request must use status byte zero."));
            return;
        }

        var status = State == HsmsSessionState.Selected
            ? HsmsDeselectStatus.Success
            : HsmsDeselectStatus.NotSelected;
        StartControlSend(
            new SendOperation(
                _sessionId,
                SendPurpose.DeselectResponse,
                header.SystemBytes,
                0,
                (byte)status));
    }

    private void ProcessDeselectResponse(HsmsMessageHeader header)
    {
        if (!TryTakePendingControlResponse(
            header,
            HsmsMessageType.DeselectResponse,
            out var pending))
        {
            return;
        }

        var status = DecodeDeselectStatus(header.HeaderByte3);
        if (status != HsmsDeselectStatus.Success)
        {
            pending.Completion.TrySetException(
                new HsmsDeselectRejectedException(status));
            return;
        }

        EnterConnectedAfterDeselect();
        pending.Completion.TrySetResult(true);
    }

    private void ProcessRejectRequest(HsmsFrame frame)
    {
        var header = frame.Header;
        var reason = DecodeRejectReason(header.HeaderByte3);
        var error = new HsmsControlRejectedException(
            header.HeaderByte2,
            reason);

        if (_pendingSelectSystemBytes == header.SystemBytes &&
            header.HeaderByte2 == (byte)HsmsMessageType.SelectRequest)
        {
            _pendingSelectSystemBytes = null;
            CancelT6();
            Transition(HsmsSessionState.Connected, error);
            return;
        }

        var pending = _pendingControlCommand;
        if (pending is null ||
            pending.SystemBytes != header.SystemBytes ||
            (byte)pending.RequestType != header.HeaderByte2)
        {
            _events.Writer.TryWrite(
                HsmsSessionEvent.ControlMessageReceived(
                    _sessionId,
                    State,
                    frame));
            return;
        }

        _pendingControlCommand = null;
        CancelT6();
        pending.Completion.TrySetException(error);
    }

    private bool TryTakePendingControlResponse(
        HsmsMessageHeader header,
        HsmsMessageType responseType,
        out PendingControlCommand pending)
    {
        var candidate = _pendingControlCommand;
        if (candidate is null ||
            candidate.ResponseType != responseType ||
            candidate.SystemBytes != header.SystemBytes)
        {
            SendReject(header, HsmsRejectReason.TransactionNotOpen);
            pending = null!;
            return false;
        }

        _pendingControlCommand = null;
        CancelT6();
        pending = candidate;
        return true;
    }

    private void ProcessSeparateRequest(HsmsMessageHeader header)
    {
        if (header.HeaderByte3 != 0)
        {
            AbortCurrentSession(
                new HsmsProtocolException("Separate Request must use status byte zero."));
            return;
        }

        _transport.TryCloseSession(_sessionId);
    }

    private void ProcessSendCompleted(SendOperation operation)
    {
        if (operation.SessionId != _sessionId)
            return;

        switch (operation.Purpose)
        {
            case SendPurpose.SelectRequest:
                if (_pendingSelectSystemBytes == operation.SystemBytes &&
                    State != HsmsSessionState.Selected)
                {
                    ArmT6();
                }
                break;
            case SendPurpose.SelectResponse:
                if (operation.HeaderByte3 == (byte)HsmsSelectStatus.Success)
                    EnterSelected();
                break;
            case SendPurpose.LinktestRequest:
            case SendPurpose.DeselectRequest:
                var pending = _pendingControlCommand;
                if (pending is not null &&
                    pending.SystemBytes == operation.SystemBytes &&
                    pending.RequestType == GetRequestType(operation.Purpose))
                {
                    ArmT6();
                }
                break;
            case SendPurpose.DeselectResponse:
                if (operation.HeaderByte3 == (byte)HsmsDeselectStatus.Success)
                    EnterConnectedAfterDeselect();
                break;
            case SendPurpose.LinktestResponse:
            case SendPurpose.Reject:
                break;
            case SendPurpose.Separate:
                _separateCompletion?.TrySetResult(true);
                _separateCompletion = null;
                _transport.TryCloseSession(_sessionId);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(operation),
                    operation.Purpose,
                    "Unknown send purpose.");
        }
    }

    private void ProcessSendFailed(SendOperation operation, Exception error)
    {
        if (operation.SessionId != _sessionId)
            return;

        if (operation.Purpose == SendPurpose.Separate)
        {
            _separateCompletion?.TrySetException(error);
            _separateCompletion = null;
        }

        AbortCurrentSession(error);
    }

    private void ProcessT6Expired(int generation)
    {
        if (generation != _t6Generation ||
            !_sessionId.IsValid ||
            (_pendingSelectSystemBytes is null &&
                _pendingControlCommand is null))
        {
            return;
        }

        AbortCurrentSession(new HsmsSessionTimeoutException("T6"));
    }

    private void ProcessT7Expired(int generation)
    {
        if (generation != _t7Generation ||
            !_sessionId.IsValid ||
            State == HsmsSessionState.Selected)
        {
            return;
        }

        AbortCurrentSession(new HsmsSessionTimeoutException("T7"));
    }

    private void ProcessSeparateRequested(
        TaskCompletionSource<bool> completion,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
            return;
        }

        if (State != HsmsSessionState.Selected || !_sessionId.IsValid)
        {
            completion.TrySetException(
                new InvalidOperationException("Separate Request requires a selected HSMS session."));
            return;
        }

        if (_separateCompletion is not null ||
            _pendingControlCommand is not null)
        {
            completion.TrySetException(
                new InvalidOperationException("Another HSMS control command is already in progress."));
            return;
        }

        _separateCompletion = completion;
        StartControlSend(
            new SendOperation(
                _sessionId,
                SendPurpose.Separate,
                _systemBytesProvider.Next(),
                0,
                0));
    }

    private void ProcessControlRequested(
        TaskCompletionSource<bool> completion,
        CancellationToken cancellationToken,
        HsmsMessageType requestType,
        HsmsMessageType responseType,
        SendPurpose sendPurpose)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
            return;
        }

        if (State != HsmsSessionState.Selected || !_sessionId.IsValid)
        {
            completion.TrySetException(
                new InvalidOperationException(
                    $"{requestType} requires a selected HSMS session."));
            return;
        }

        if (_pendingControlCommand is not null ||
            _separateCompletion is not null)
        {
            completion.TrySetException(
                new InvalidOperationException(
                    "Another HSMS control command is already in progress."));
            return;
        }

        var systemBytes = _systemBytesProvider.Next();
        _pendingControlCommand = new PendingControlCommand(
            requestType,
            responseType,
            systemBytes,
            completion);
        StartControlSend(
            new SendOperation(
                _sessionId,
                sendPurpose,
                systemBytes,
                0,
                0));
    }

    private void ProcessTransportFailure(Exception error)
    {
        if (_sessionId.IsValid)
            ProcessSessionClosed(error);
    }

    private void ProcessDataSendRequested(
        PendingDataSend operation,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            operation.Completion.TrySetCanceled(cancellationToken);
            return;
        }

        if (State != HsmsSessionState.Selected || !_sessionId.IsValid)
        {
            operation.Completion.TrySetException(
                new InvalidOperationException(
                    "An HSMS data message requires a selected session."));
            return;
        }

        operation.SessionId = _sessionId;
        _pendingDataSends.Add(operation);
        _ = SendDataFrameAsync(operation);
    }

    private void ProcessDataSendCompleted(PendingDataSend operation)
    {
        if (_pendingDataSends.Remove(operation))
            operation.Completion.TrySetResult(true);
    }

    private void ProcessDataSendFailed(
        PendingDataSend operation,
        Exception error)
    {
        if (!_pendingDataSends.Remove(operation))
            return;

        operation.Completion.TrySetException(error);
        if (operation.SessionId == _sessionId)
            AbortCurrentSession(error);
    }

    private void StartControlSend(SendOperation operation)
    {
        var messageType = operation.Purpose switch
        {
            SendPurpose.SelectRequest => HsmsMessageType.SelectRequest,
            SendPurpose.SelectResponse => HsmsMessageType.SelectResponse,
            SendPurpose.DeselectRequest => HsmsMessageType.DeselectRequest,
            SendPurpose.DeselectResponse => HsmsMessageType.DeselectResponse,
            SendPurpose.LinktestRequest => HsmsMessageType.LinktestRequest,
            SendPurpose.LinktestResponse => HsmsMessageType.LinktestResponse,
            SendPurpose.Reject => HsmsMessageType.RejectRequest,
            SendPurpose.Separate => HsmsMessageType.SeparateRequest,
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation.Purpose,
                "Unknown send purpose."),
        };
        if (operation.Purpose != SendPurpose.Reject &&
            operation.HeaderByte2 != 0)
        {
            throw new InvalidOperationException(
                "Only Reject Request can use a nonzero HSMS header byte 2.");
        }

        var header = operation.Purpose == SendPurpose.Reject
            ? HsmsMessageHeader.CreateReject(
                operation.SystemBytes,
                operation.HeaderByte2,
                operation.HeaderByte3)
            : HsmsMessageHeader.CreateControl(
                messageType,
                operation.SystemBytes,
                operation.HeaderByte3);
        var frame = new HsmsFrame(
            header);

        _ = SendControlAsync(operation, frame);
    }

    private async Task SendControlAsync(SendOperation operation, HsmsFrame frame)
    {
        try
        {
            await _transport.SendAsync(
                operation.SessionId,
                frame,
                _lifetime?.Token ?? CancellationToken.None).ConfigureAwait(false);
            _inputs.Writer.TryWrite(MachineInput.SendCompleted(operation));
        }
        catch (OperationCanceledException) when (_lifetime?.IsCancellationRequested == true)
        {
        }
        catch (Exception ex)
        {
            _inputs.Writer.TryWrite(MachineInput.SendFailed(operation, ex));
        }
    }

    private async Task SendDataFrameAsync(PendingDataSend operation)
    {
        try
        {
            await _transport.SendAsync(
                operation.SessionId,
                operation.Frame,
                _lifetime?.Token ?? CancellationToken.None).ConfigureAwait(false);
            _inputs.Writer.TryWrite(MachineInput.DataSendCompleted(operation));
        }
        catch (OperationCanceledException) when (_lifetime?.IsCancellationRequested == true)
        {
        }
        catch (Exception ex)
        {
            _inputs.Writer.TryWrite(MachineInput.DataSendFailed(operation, ex));
        }
    }

    private void EnterSelected()
    {
        CancelT6();
        CancelT7();
        Transition(HsmsSessionState.Selected);
    }

    private void EnterConnectedAfterDeselect()
    {
        if (State != HsmsSessionState.Selected)
            return;

        CancelT6();
        var pending = _pendingControlCommand;
        _pendingControlCommand = null;
        Transition(HsmsSessionState.Connected);
        ArmT7();

        if (pending is not null)
        {
            if (pending.RequestType == HsmsMessageType.DeselectRequest)
            {
                pending.Completion.TrySetResult(true);
            }
            else
            {
                pending.Completion.TrySetException(
                    new IOException(
                        $"{pending.RequestType} was interrupted because the HSMS session was deselected."));
            }
        }
    }

    private void SendReject(
        HsmsMessageHeader rejectedHeader,
        HsmsRejectReason reason)
    {
        if (!_sessionId.IsValid)
            return;

        StartControlSend(
            new SendOperation(
                _sessionId,
                SendPurpose.Reject,
                rejectedHeader.SystemBytes,
                (byte)rejectedHeader.MessageType,
                (byte)reason));
    }

    private static HsmsMessageType GetRequestType(SendPurpose purpose)
        => purpose switch
        {
            SendPurpose.LinktestRequest => HsmsMessageType.LinktestRequest,
            SendPurpose.DeselectRequest => HsmsMessageType.DeselectRequest,
            _ => throw new ArgumentOutOfRangeException(
                nameof(purpose),
                purpose,
                "The send purpose is not a control request."),
        };

    private void AbortCurrentSession(Exception error)
    {
        if (_sessionId.IsValid)
            _transport.TryCloseSession(_sessionId, error);
    }

    private void FailPendingDataSends(
        Exception error,
        HsmsTransportSessionId sessionId = default)
    {
        if (_pendingDataSends.Count == 0)
            return;

        var pending = _pendingDataSends.ToArray();
        foreach (var operation in pending)
        {
            if (sessionId.IsValid && operation.SessionId != sessionId)
                continue;

            _pendingDataSends.Remove(operation);
            operation.Completion.TrySetException(error);
        }
    }

    private void ArmT6()
    {
        CancelT6();
        var generation = _t6Generation;
        _t6Timer = _timerFactory.Create(
            () => _inputs.Writer.TryWrite(MachineInput.T6Expired(generation)));
        _t6Timer.Change(_options.ControlReplyTimeout);
    }

    private void CancelT6()
    {
        _t6Generation++;
        _t6Timer?.Dispose();
        _t6Timer = null;
    }

    private void ArmT7()
    {
        CancelT7();
        var generation = _t7Generation;
        _t7Timer = _timerFactory.Create(
            () => _inputs.Writer.TryWrite(MachineInput.T7Expired(generation)));
        _t7Timer.Change(_options.SelectionTimeout);
    }

    private void CancelT7()
    {
        _t7Generation++;
        _t7Timer?.Dispose();
        _t7Timer = null;
    }

    private void Transition(HsmsSessionState state, Exception? error = null)
        => Transition(_sessionId, state, error);

    private void Transition(
        HsmsTransportSessionId sessionId,
        HsmsSessionState state,
        Exception? error = null)
    {
        var previous = (HsmsSessionState)Interlocked.Exchange(ref _state, (int)state);
        if (previous == state && error is null)
            return;

        _events.Writer.TryWrite(
            HsmsSessionEvent.StateChanged(sessionId, state, error));
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(HsmsSessionStateMachine));
    }

    private static HsmsSelectStatus DecodeSelectStatus(byte value)
        => value switch
        {
            (byte)HsmsSelectStatus.Success => HsmsSelectStatus.Success,
            (byte)HsmsSelectStatus.AlreadySelected => HsmsSelectStatus.AlreadySelected,
            (byte)HsmsSelectStatus.NotReady => HsmsSelectStatus.NotReady,
            (byte)HsmsSelectStatus.Unavailable => HsmsSelectStatus.Unavailable,
            _ => (HsmsSelectStatus)value,
        };

    private static HsmsDeselectStatus DecodeDeselectStatus(byte value)
        => value switch
        {
            (byte)HsmsDeselectStatus.Success => HsmsDeselectStatus.Success,
            (byte)HsmsDeselectStatus.NotSelected => HsmsDeselectStatus.NotSelected,
            _ => (HsmsDeselectStatus)value,
        };

    private static HsmsRejectReason DecodeRejectReason(byte value)
        => value switch
        {
            (byte)HsmsRejectReason.UnsupportedSessionType =>
                HsmsRejectReason.UnsupportedSessionType,
            (byte)HsmsRejectReason.UnsupportedPresentationType =>
                HsmsRejectReason.UnsupportedPresentationType,
            (byte)HsmsRejectReason.TransactionNotOpen =>
                HsmsRejectReason.TransactionNotOpen,
            (byte)HsmsRejectReason.EntityNotSelected =>
                HsmsRejectReason.EntityNotSelected,
            _ => (HsmsRejectReason)value,
        };

    private enum MachineInputKind
    {
        Transport,
        SendCompleted,
        SendFailed,
        T6Expired,
        T7Expired,
        SeparateRequested,
        LinktestRequested,
        DeselectRequested,
        DataSendRequested,
        DataSendCompleted,
        DataSendFailed,
        TransportFailed,
    }

    private enum SendPurpose
    {
        SelectRequest,
        SelectResponse,
        DeselectRequest,
        DeselectResponse,
        LinktestRequest,
        LinktestResponse,
        Reject,
        Separate,
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct SendOperation(
        HsmsTransportSessionId SessionId,
        SendPurpose Purpose,
        uint SystemBytes,
        byte HeaderByte2,
        byte HeaderByte3);

    private sealed class PendingControlCommand
    {
        public PendingControlCommand(
            HsmsMessageType requestType,
            HsmsMessageType responseType,
            uint systemBytes,
            TaskCompletionSource<bool> completion)
        {
            RequestType = requestType;
            ResponseType = responseType;
            SystemBytes = systemBytes;
            Completion = completion;
        }

        public HsmsMessageType RequestType { get; }

        public HsmsMessageType ResponseType { get; }

        public uint SystemBytes { get; }

        public TaskCompletionSource<bool> Completion { get; }
    }

    private sealed class PendingDataSend
    {
        public PendingDataSend(
            HsmsFrame frame,
            TaskCompletionSource<bool> completion)
        {
            Frame = frame;
            Completion = completion;
        }

        public HsmsTransportSessionId SessionId { get; set; }

        public HsmsFrame Frame { get; }

        public TaskCompletionSource<bool> Completion { get; }
    }

    private sealed class MachineInput
    {
        private MachineInput(MachineInputKind kind)
        {
            Kind = kind;
        }

        public MachineInputKind Kind { get; }

        public HsmsTransportEvent TransportEvent { get; private init; }

        public SendOperation SendOperation { get; private init; }

        public PendingDataSend? DataSend { get; private init; }

        public Exception? Error { get; private init; }

        public int Generation { get; private init; }

        public TaskCompletionSource<bool>? Completion { get; private init; }

        public CancellationToken CancellationToken { get; private init; }

        public static MachineInput Transport(HsmsTransportEvent transportEvent)
            => new(MachineInputKind.Transport) { TransportEvent = transportEvent };

        public static MachineInput SendCompleted(SendOperation operation)
            => new(MachineInputKind.SendCompleted) { SendOperation = operation };

        public static MachineInput SendFailed(SendOperation operation, Exception error)
            => new(MachineInputKind.SendFailed) { SendOperation = operation, Error = error };

        public static MachineInput T6Expired(int generation)
            => new(MachineInputKind.T6Expired) { Generation = generation };

        public static MachineInput T7Expired(int generation)
            => new(MachineInputKind.T7Expired) { Generation = generation };

        public static MachineInput DataSendRequested(
            PendingDataSend operation,
            CancellationToken cancellationToken)
            => new(MachineInputKind.DataSendRequested)
            {
                DataSend = operation,
                CancellationToken = cancellationToken,
            };

        public static MachineInput DataSendCompleted(PendingDataSend operation)
            => new(MachineInputKind.DataSendCompleted) { DataSend = operation };

        public static MachineInput DataSendFailed(
            PendingDataSend operation,
            Exception error)
            => new(MachineInputKind.DataSendFailed)
            {
                DataSend = operation,
                Error = error,
            };

        public static MachineInput CommandRequested(
            MachineInputKind kind,
            TaskCompletionSource<bool> completion,
            CancellationToken cancellationToken)
        {
            if (kind != MachineInputKind.SeparateRequested &&
                kind != MachineInputKind.LinktestRequested &&
                kind != MachineInputKind.DeselectRequested)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "The input kind is not a local HSMS command.");
            }

            return new MachineInput(kind)
            {
                Completion = completion,
                CancellationToken = cancellationToken,
            };
        }

        public static MachineInput TransportFailed(Exception error)
            => new(MachineInputKind.TransportFailed) { Error = error };
    }
}
