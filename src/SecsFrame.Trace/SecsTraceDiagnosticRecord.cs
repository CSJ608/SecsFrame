namespace SecsFrame.Trace;

/// <summary>
/// Describes a restricted-field, immutable snapshot of one structured HSMS diagnostic.
/// </summary>
/// <remarks>
/// The snapshot deliberately excludes the original exception and undecodable
/// frame because either can contain environment or application data.
/// </remarks>
public sealed class SecsTraceDiagnosticRecord
{
    /// <summary>Creates a structured diagnostic trace record.</summary>
    public SecsTraceDiagnosticRecord(
        DateTimeOffset timestamp,
        HsmsDiagnosticCode code,
        HsmsDiagnosticLayer layer,
        HsmsOperation operation,
        HsmsSessionState state,
        HsmsTimer? timer = null,
        ushort? protocolSessionId = null,
        uint? systemBytes = null,
        byte? peerStatus = null,
        byte? rejectedMessageType = null)
    {
        ValidateEnum(code, nameof(code));
        ValidateEnum(layer, nameof(layer));
        ValidateEnum(operation, nameof(operation));
        ValidateEnum(state, nameof(state));
        if (timer.HasValue)
            ValidateEnum(timer.Value, nameof(timer));

        Timestamp = timestamp.ToUniversalTime();
        Code = code;
        Layer = layer;
        Operation = operation;
        State = state;
        Timer = timer;
        ProtocolSessionId = protocolSessionId;
        SystemBytes = systemBytes;
        PeerStatus = peerStatus;
        RejectedMessageType = rejectedMessageType;
    }

    /// <summary>Gets the UTC timestamp associated with the local observation.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Gets the stable diagnostic code.</summary>
    public HsmsDiagnosticCode Code { get; }

    /// <summary>Gets the layer that produced the diagnostic.</summary>
    public HsmsDiagnosticLayer Layer { get; }

    /// <summary>Gets the operation associated with the diagnostic.</summary>
    public HsmsOperation Operation { get; }

    /// <summary>Gets the observed HSMS session state.</summary>
    public HsmsSessionState State { get; }

    /// <summary>Gets the associated timer, when present.</summary>
    public HsmsTimer? Timer { get; }

    /// <summary>Gets the protocol Session ID, when present.</summary>
    public ushort? ProtocolSessionId { get; }

    /// <summary>Gets the transaction System Bytes, when present.</summary>
    public uint? SystemBytes { get; }

    /// <summary>Gets the peer-provided status or reject-reason byte, when present.</summary>
    public byte? PeerStatus { get; }

    /// <summary>Gets the rejected HSMS SType byte, when present.</summary>
    public byte? RejectedMessageType { get; }

    /// <summary>
    /// Creates a restricted-field snapshot without copying the diagnostic exception or frame.
    /// </summary>
    public static SecsTraceDiagnosticRecord Create(
        DateTimeOffset timestamp,
        HsmsDiagnostic diagnostic)
    {
        if (diagnostic is null)
            throw new ArgumentNullException(nameof(diagnostic));

        return new SecsTraceDiagnosticRecord(
            timestamp,
            diagnostic.Code,
            diagnostic.Layer,
            diagnostic.Operation,
            diagnostic.State,
            diagnostic.Timer,
            diagnostic.ProtocolSessionId,
            diagnostic.SystemBytes,
            diagnostic.PeerStatus,
            diagnostic.RejectedMessageType);
    }

    private static void ValidateEnum<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(typeof(TEnum), value))
            throw new ArgumentOutOfRangeException(parameterName, value, "Unknown diagnostic field value.");
    }
}
