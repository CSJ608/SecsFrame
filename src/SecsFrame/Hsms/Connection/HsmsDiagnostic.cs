using System.IO;

namespace SecsFrame;

/// <summary>
/// Provides stable, structured context for a failure observed through
/// <see cref="HsmsConnection"/>.
/// </summary>
public sealed class HsmsDiagnostic
{
    private HsmsDiagnostic(
        HsmsDiagnosticCode code,
        HsmsDiagnosticLayer layer,
        HsmsOperation operation,
        HsmsSessionState state,
        Exception error,
        HsmsTimer? timer = null,
        ushort? protocolSessionId = null,
        uint? systemBytes = null,
        byte? peerStatus = null,
        byte? rejectedMessageType = null,
        HsmsFrame? frame = null)
    {
        Code = code;
        Layer = layer;
        Operation = operation;
        State = state;
        Error = error;
        Timer = timer;
        ProtocolSessionId = protocolSessionId;
        SystemBytes = systemBytes;
        PeerStatus = peerStatus;
        RejectedMessageType = rejectedMessageType;
        Frame = frame;
    }

    /// <summary>Gets the stable diagnostic code.</summary>
    public HsmsDiagnosticCode Code { get; }

    /// <summary>Gets the layer that produced the diagnostic.</summary>
    public HsmsDiagnosticLayer Layer { get; }

    /// <summary>Gets the operation associated with the diagnostic.</summary>
    public HsmsOperation Operation { get; }

    /// <summary>Gets the HSMS session state observed with the diagnostic.</summary>
    public HsmsSessionState State { get; }

    /// <summary>Gets the associated timer, when the diagnostic is a timer expiry.</summary>
    public HsmsTimer? Timer { get; }

    /// <summary>Gets the protocol Session ID, when available.</summary>
    public ushort? ProtocolSessionId { get; }

    /// <summary>Gets the transaction System Bytes, when available.</summary>
    public uint? SystemBytes { get; }

    /// <summary>Gets a peer-provided status or reject reason byte, when available.</summary>
    public byte? PeerStatus { get; }

    /// <summary>Gets the rejected HSMS SType byte, when available.</summary>
    public byte? RejectedMessageType { get; }

    /// <summary>Gets the undecodable frame for a decode diagnostic, when available.</summary>
    /// <remarks>The frame can contain application data and should be logged deliberately.</remarks>
    public HsmsFrame? Frame { get; }

    /// <summary>Gets the original exception for detailed local investigation.</summary>
    public Exception Error { get; }

    /// <summary>Classifies an exception produced by a public HSMS operation.</summary>
    /// <param name="error">The exception to classify.</param>
    /// <param name="state">The connection state observed by the caller.</param>
    /// <returns>
    /// A structured diagnostic, or <see langword="null"/> for caller cancellation,
    /// disposal, argument errors, invalid lifecycle use, or unknown exceptions.
    /// </returns>
    public static HsmsDiagnostic? Classify(
        Exception error,
        HsmsSessionState state)
    {
        if (error is null)
            throw new ArgumentNullException(nameof(error));

        if (error is OperationCanceledException ||
            error is ObjectDisposedException ||
            error is ArgumentException ||
            error is InvalidOperationException)
        {
            return null;
        }

        return ClassifyTimeout(error, state) ??
            ClassifyRejection(error, state) ??
            ClassifyFailure(error, state);
    }

    private static HsmsDiagnostic? ClassifyTimeout(
        Exception error,
        HsmsSessionState state)
        => error switch
        {
            HsmsDataTransactionTimeoutException timeout => new HsmsDiagnostic(
                HsmsDiagnosticCode.T3Timeout,
                HsmsDiagnosticLayer.Transaction,
                HsmsOperation.WaitForSecondary,
                state,
                timeout,
                HsmsTimer.T3,
                timeout.Primary.SessionId,
                timeout.Primary.SystemBytes),
            HsmsSessionTimeoutException timeout => new HsmsDiagnostic(
                timeout.Timer == HsmsTimer.T6
                    ? HsmsDiagnosticCode.T6Timeout
                    : HsmsDiagnosticCode.T7Timeout,
                HsmsDiagnosticLayer.Session,
                timeout.Operation,
                state,
                timeout,
                timeout.Timer),
            HsmsT8TimeoutException timeout => new HsmsDiagnostic(
                HsmsDiagnosticCode.T8Timeout,
                HsmsDiagnosticLayer.Transport,
                HsmsOperation.ReceiveFrame,
                state,
                timeout,
                HsmsTimer.T8),
            _ => null,
        };

    private static HsmsDiagnostic? ClassifyRejection(
        Exception error,
        HsmsSessionState state)
        => error switch
        {
            HsmsSelectionRejectedException rejected => new HsmsDiagnostic(
                HsmsDiagnosticCode.SelectionRejected,
                HsmsDiagnosticLayer.Session,
                HsmsOperation.Select,
                state,
                rejected,
                peerStatus: (byte)rejected.Status),
            HsmsDeselectRejectedException rejected => new HsmsDiagnostic(
                HsmsDiagnosticCode.DeselectRejected,
                HsmsDiagnosticLayer.Session,
                HsmsOperation.Deselect,
                state,
                rejected,
                peerStatus: (byte)rejected.Status),
            HsmsControlRejectedException rejected => new HsmsDiagnostic(
                HsmsDiagnosticCode.ControlRejected,
                HsmsDiagnosticLayer.Session,
                GetControlOperation(rejected.RejectedMessageType),
                state,
                rejected,
                peerStatus: (byte)rejected.Reason,
                rejectedMessageType: rejected.RejectedMessageType),
            HsmsDataMessageRejectedException rejected => new HsmsDiagnostic(
                HsmsDiagnosticCode.DataMessageRejected,
                HsmsDiagnosticLayer.Transaction,
                HsmsOperation.SendData,
                state,
                rejected,
                peerStatus: (byte)rejected.Reason,
                rejectedMessageType: (byte)HsmsMessageType.DataMessage),
            _ => null,
        };

    private static HsmsDiagnostic? ClassifyFailure(
        Exception error,
        HsmsSessionState state)
        => error switch
        {
            HsmsDataTransactionInterruptedException interrupted => new HsmsDiagnostic(
                HsmsDiagnosticCode.TransactionInterrupted,
                HsmsDiagnosticLayer.Transaction,
                HsmsOperation.SendData,
                interrupted.State,
                interrupted),
            HsmsControlTransactionInterruptedException interrupted => new HsmsDiagnostic(
                HsmsDiagnosticCode.TransactionInterrupted,
                HsmsDiagnosticLayer.Session,
                interrupted.Operation,
                interrupted.State,
                interrupted),
            HsmsTransportSessionExpiredException expired => new HsmsDiagnostic(
                HsmsDiagnosticCode.TransportSessionExpired,
                HsmsDiagnosticLayer.Transport,
                HsmsOperation.None,
                state,
                expired),
            HsmsProtocolException protocol => new HsmsDiagnostic(
                HsmsDiagnosticCode.ProtocolViolation,
                HsmsDiagnosticLayer.Session,
                HsmsOperation.ReceiveFrame,
                state,
                protocol),
            SecsProtocolException codec => new HsmsDiagnostic(
                HsmsDiagnosticCode.CodecFailure,
                HsmsDiagnosticLayer.Codec,
                HsmsOperation.None,
                state,
                codec),
            IOException transport => new HsmsDiagnostic(
                HsmsDiagnosticCode.TransportFailure,
                HsmsDiagnosticLayer.Transport,
                HsmsOperation.Connect,
                state,
                transport),
            _ => null,
        };

    internal static HsmsDiagnostic DataMessageDecodeFailed(
        HsmsFrame frame,
        Exception error)
        => new(
            HsmsDiagnosticCode.DataMessageDecodeFailed,
            HsmsDiagnosticLayer.Codec,
            HsmsOperation.DecodeData,
            HsmsSessionState.Selected,
            error,
            protocolSessionId: frame.Header.SessionId,
            systemBytes: frame.Header.SystemBytes,
            frame: frame);

    private static HsmsOperation GetControlOperation(byte messageType)
        => (HsmsMessageType)messageType switch
        {
            HsmsMessageType.SelectRequest => HsmsOperation.Select,
            HsmsMessageType.LinktestRequest => HsmsOperation.Linktest,
            HsmsMessageType.DeselectRequest => HsmsOperation.Deselect,
            HsmsMessageType.SeparateRequest => HsmsOperation.Separate,
            _ => HsmsOperation.None,
        };
}
