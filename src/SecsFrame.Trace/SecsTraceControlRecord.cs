namespace SecsFrame.Trace;

/// <summary>
/// Describes a restricted-field snapshot of one HSMS control-message header.
/// </summary>
/// <remarks>
/// The record stores only the ten-byte header fields and never contains a frame body.
/// </remarks>
public sealed class SecsTraceControlRecord
{
    /// <summary>Creates an immutable control-message metadata record.</summary>
    public SecsTraceControlRecord(
        DateTimeOffset timestamp,
        SecsTraceDirection direction,
        HsmsSessionState state,
        ushort protocolSessionId,
        byte headerByte2,
        byte headerByte3,
        byte presentationType,
        byte messageType,
        uint systemBytes)
    {
        if (direction != SecsTraceDirection.Sent && direction != SecsTraceDirection.Received)
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown trace direction.");
        if (!Enum.IsDefined(typeof(HsmsSessionState), state))
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown HSMS session state.");
        if (messageType == (byte)HsmsMessageType.DataMessage)
            throw new ArgumentOutOfRangeException(nameof(messageType), messageType, "A control-message SType must be nonzero.");

        Timestamp = timestamp.ToUniversalTime();
        Direction = direction;
        State = state;
        ProtocolSessionId = protocolSessionId;
        HeaderByte2 = headerByte2;
        HeaderByte3 = headerByte3;
        PresentationType = presentationType;
        MessageType = messageType;
        SystemBytes = systemBytes;
    }

    /// <summary>Gets the UTC timestamp associated with the local observation.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Gets the local message direction.</summary>
    public SecsTraceDirection Direction { get; }

    /// <summary>Gets the HSMS session state observed with the control message.</summary>
    public HsmsSessionState State { get; }

    /// <summary>Gets the raw protocol Session ID.</summary>
    public ushort ProtocolSessionId { get; }

    /// <summary>Gets raw HSMS header byte 2.</summary>
    public byte HeaderByte2 { get; }

    /// <summary>Gets raw HSMS header byte 3.</summary>
    public byte HeaderByte3 { get; }

    /// <summary>Gets the raw PType byte.</summary>
    public byte PresentationType { get; }

    /// <summary>Gets the raw nonzero SType byte.</summary>
    public byte MessageType { get; }

    /// <summary>Gets the raw System Bytes value.</summary>
    public uint SystemBytes { get; }

    /// <summary>Creates a metadata record from a control frame.</summary>
    public static SecsTraceControlRecord Create(
        DateTimeOffset timestamp,
        SecsTraceDirection direction,
        HsmsSessionState state,
        HsmsFrame frame)
    {
        if (frame is null)
            throw new ArgumentNullException(nameof(frame));
        if (frame.Header.IsDataMessage || !frame.Body.IsEmpty)
            throw new ArgumentException("The frame must be an HSMS control message without a body.", nameof(frame));

        var header = frame.Header;
        return new SecsTraceControlRecord(
            timestamp,
            direction,
            state,
            header.SessionId,
            header.HeaderByte2,
            header.HeaderByte3,
            header.PresentationType,
            (byte)header.MessageType,
            header.SystemBytes);
    }

    /// <summary>
    /// Creates a received metadata record from an unclaimed public control event.
    /// </summary>
    public static SecsTraceControlRecord CreateReceived(
        DateTimeOffset timestamp,
        HsmsConnectionEvent connectionEvent)
    {
        if (connectionEvent is null)
            throw new ArgumentNullException(nameof(connectionEvent));
        if (connectionEvent.Kind != HsmsConnectionEventKind.ControlMessageReceived ||
            connectionEvent.Frame is null)
        {
            throw new ArgumentException("The connection event must contain an unclaimed control message.", nameof(connectionEvent));
        }

        return Create(
            timestamp,
            SecsTraceDirection.Received,
            connectionEvent.State,
            connectionEvent.Frame);
    }
}
