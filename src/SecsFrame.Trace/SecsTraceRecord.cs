namespace SecsFrame.Trace;

/// <summary>
/// Describes one decoded SECS-II data message with optional HSMS diagnostic
/// identifiers.
/// </summary>
public sealed class SecsTraceRecord
{
    /// <summary>Creates an immutable decoded-message trace record.</summary>
    public SecsTraceRecord(
        DateTimeOffset timestamp,
        SecsTraceDirection direction,
        SecsMessage message,
        ushort? sessionId = null,
        uint? systemBytes = null)
    {
        if (direction != SecsTraceDirection.Sent && direction != SecsTraceDirection.Received)
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown trace direction.");

        Timestamp = timestamp.ToUniversalTime();
        Direction = direction;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        SessionId = sessionId;
        SystemBytes = systemBytes;
    }

    /// <summary>Gets the UTC timestamp associated with the local observation.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Gets the local message direction.</summary>
    public SecsTraceDirection Direction { get; }

    /// <summary>Gets the optional HSMS protocol Session ID.</summary>
    public ushort? SessionId { get; }

    /// <summary>Gets the optional original System Bytes value.</summary>
    public uint? SystemBytes { get; }

    /// <summary>Gets the decoded dynamic SECS-II message.</summary>
    public SecsMessage Message { get; }

    /// <summary>Creates a sent record before a public connection send.</summary>
    public static SecsTraceRecord CreateSent(
        DateTimeOffset timestamp,
        SecsMessage message,
        ushort? sessionId = null)
        => new(timestamp, SecsTraceDirection.Sent, message, sessionId);

    /// <summary>Creates a received record from a decoded incoming message.</summary>
    public static SecsTraceRecord CreateReceived(
        DateTimeOffset timestamp,
        HsmsIncomingDataMessage incoming)
    {
        if (incoming is null)
            throw new ArgumentNullException(nameof(incoming));

        var dataMessage = incoming.DataMessage;
        return new SecsTraceRecord(
            timestamp,
            SecsTraceDirection.Received,
            dataMessage.Message,
            dataMessage.SessionId,
            dataMessage.SystemBytes);
    }
}
