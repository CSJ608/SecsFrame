namespace SecsFrame.Tests;

public sealed class HsmsDiagnosticTests
{
    [Fact]
    public void T3_diagnostic_preserves_transaction_identity()
    {
        var primary = new HsmsDataMessage(
            sessionId: 10,
            systemBytes: 0x10203040,
            new SecsMessage(6, 11, true));
        var error = new HsmsDataTransactionTimeoutException(primary);

        var diagnostic = HsmsDiagnostic.Classify(
            error,
            HsmsSessionState.Selected)!;

        Assert.Equal(HsmsDiagnosticCode.T3Timeout, diagnostic.Code);
        Assert.Equal(HsmsDiagnosticLayer.Transaction, diagnostic.Layer);
        Assert.Equal(HsmsOperation.WaitForSecondary, diagnostic.Operation);
        Assert.Equal(HsmsTimer.T3, diagnostic.Timer);
        Assert.Equal((ushort)10, diagnostic.ProtocolSessionId);
        Assert.Equal(0x10203040U, diagnostic.SystemBytes);
        Assert.Same(error, diagnostic.Error);
        Assert.Null(diagnostic.Frame);
    }

    [Fact]
    public void Session_and_transport_timeouts_keep_timer_and_operation()
    {
        var t6 = HsmsDiagnostic.Classify(
            new HsmsSessionTimeoutException(
                HsmsTimer.T6,
                HsmsOperation.Linktest),
            HsmsSessionState.Disconnected)!;
        var t7 = HsmsDiagnostic.Classify(
            new HsmsSessionTimeoutException(
                HsmsTimer.T7,
                HsmsOperation.Select),
            HsmsSessionState.Disconnected)!;
        var t8 = HsmsDiagnostic.Classify(
            new HsmsT8TimeoutException(new HsmsTransportSessionId(7)),
            HsmsSessionState.Disconnected)!;

        Assert.Equal(HsmsDiagnosticCode.T6Timeout, t6.Code);
        Assert.Equal(HsmsOperation.Linktest, t6.Operation);
        Assert.Equal(HsmsTimer.T6, t6.Timer);
        Assert.Equal(HsmsDiagnosticCode.T7Timeout, t7.Code);
        Assert.Equal(HsmsOperation.Select, t7.Operation);
        Assert.Equal(HsmsTimer.T7, t7.Timer);
        Assert.Equal(HsmsDiagnosticCode.T8Timeout, t8.Code);
        Assert.Equal(HsmsDiagnosticLayer.Transport, t8.Layer);
        Assert.Equal(HsmsOperation.ReceiveFrame, t8.Operation);
        Assert.Equal(HsmsTimer.T8, t8.Timer);
    }

    [Fact]
    public void Peer_rejection_preserves_operation_and_status_bytes()
    {
        var error = new HsmsControlRejectedException(
            (byte)HsmsMessageType.DeselectRequest,
            HsmsRejectReason.TransactionNotOpen);

        var diagnostic = HsmsDiagnostic.Classify(
            error,
            HsmsSessionState.Selected)!;

        Assert.Equal(HsmsDiagnosticCode.ControlRejected, diagnostic.Code);
        Assert.Equal(HsmsDiagnosticLayer.Session, diagnostic.Layer);
        Assert.Equal(HsmsOperation.Deselect, diagnostic.Operation);
        Assert.Equal((byte)HsmsRejectReason.TransactionNotOpen, diagnostic.PeerStatus);
        Assert.Equal((byte)HsmsMessageType.DeselectRequest, diagnostic.RejectedMessageType);
    }

    [Fact]
    public void Control_transaction_interruption_is_not_misclassified_as_transport()
    {
        var error = new HsmsControlTransactionInterruptedException(
            HsmsOperation.Linktest,
            HsmsSessionState.Connected);

        var diagnostic = HsmsDiagnostic.Classify(
            error,
            HsmsSessionState.Connected)!;

        Assert.Equal(HsmsDiagnosticCode.TransactionInterrupted, diagnostic.Code);
        Assert.Equal(HsmsDiagnosticLayer.Session, diagnostic.Layer);
        Assert.Equal(HsmsOperation.Linktest, diagnostic.Operation);
        Assert.Equal(HsmsSessionState.Connected, diagnostic.State);
        Assert.Same(error, diagnostic.Error);
    }

    [Fact]
    public void Decode_event_exposes_diagnostic_without_replacing_error_or_frame()
    {
        var frame = new HsmsFrame(
            HsmsMessageHeader.CreateData(
                sessionId: 23,
                stream: 1,
                function: 2,
                replyExpected: false,
                systemBytes: 0xAABBCCDD),
            new byte[] { 0xFF });
        var error = new SecsProtocolException("Invalid Item header.");

        var connectionEvent = HsmsConnectionEvent.DataMessageDecodeFailed(
            frame,
            error);
        var diagnostic = connectionEvent.Diagnostic!;

        Assert.Equal(HsmsConnectionEventKind.DataMessageDecodeFailed, connectionEvent.Kind);
        Assert.Equal(HsmsDiagnosticCode.DataMessageDecodeFailed, diagnostic.Code);
        Assert.Equal(HsmsDiagnosticLayer.Codec, diagnostic.Layer);
        Assert.Equal(HsmsOperation.DecodeData, diagnostic.Operation);
        Assert.Equal((ushort)23, diagnostic.ProtocolSessionId);
        Assert.Equal(0xAABBCCDDU, diagnostic.SystemBytes);
        Assert.Same(frame, diagnostic.Frame);
        Assert.Same(frame, connectionEvent.Frame);
        Assert.Same(error, diagnostic.Error);
        Assert.Same(error, connectionEvent.Error);
    }

    [Fact]
    public void State_event_classifies_failure_and_normal_state_has_no_diagnostic()
    {
        var error = new IOException("Connection reset.");

        var failed = HsmsConnectionEvent.StateChanged(
            HsmsSessionState.Disconnected,
            error);
        var selected = HsmsConnectionEvent.StateChanged(
            HsmsSessionState.Selected,
            error: null);

        Assert.Equal(HsmsDiagnosticCode.TransportFailure, failed.Diagnostic!.Code);
        Assert.Equal(HsmsDiagnosticLayer.Transport, failed.Diagnostic.Layer);
        Assert.Equal(HsmsOperation.Connect, failed.Diagnostic.Operation);
        Assert.Same(error, failed.Diagnostic.Error);
        Assert.Null(selected.Diagnostic);
        Assert.Null(selected.Error);
    }

    [Fact]
    public void Caller_control_flow_and_unknown_errors_are_not_diagnostics()
    {
        Assert.Null(HsmsDiagnostic.Classify(
            new OperationCanceledException(),
            HsmsSessionState.Selected));
        Assert.Null(HsmsDiagnostic.Classify(
            new ObjectDisposedException("connection"),
            HsmsSessionState.Disconnected));
        Assert.Null(HsmsDiagnostic.Classify(
            new InvalidOperationException("Not started."),
            HsmsSessionState.Disconnected));
        Assert.Null(HsmsDiagnostic.Classify(
            new Exception("Unknown."),
            HsmsSessionState.Selected));
    }
}
