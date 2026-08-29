namespace SecsFrame;

/// <summary>
/// Describes one explicitly enabled, session-associated HSMS transport fault.
/// </summary>
/// <remarks>
/// The snapshot is a defensive copy of at most the first 8 KiB associated
/// with the framing error. <see cref="ObservedByteCount"/> and
/// <see cref="IsTruncated"/> describe whether the copied prefix contains all
/// bytes observed for that error.
/// </remarks>
public sealed class HsmsTransportFaultObservation
{
    private readonly byte[] _snapshot;

    /// <summary>The maximum prefix snapshot retained by SecsFrame.</summary>
    public const int MaxSnapshotBytes = 8 * 1024;

    internal HsmsTransportFaultObservation(
        HsmsTransportFaultKind kind,
        long transportSessionId,
        HsmsSessionState state,
        ReadOnlySpan<byte> snapshot,
        long observedByteCount,
        bool isTruncated)
    {
        if (!Enum.IsDefined(typeof(HsmsTransportFaultKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown HSMS transport-fault kind.");
        if (transportSessionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(transportSessionId), transportSessionId, "The transport session identifier must be positive.");
        if (!Enum.IsDefined(typeof(HsmsSessionState), state))
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown HSMS session state.");
        if (snapshot.Length > MaxSnapshotBytes)
            throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.Length, $"A transport-fault snapshot cannot exceed {MaxSnapshotBytes} bytes.");
        if (observedByteCount < snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(observedByteCount), observedByteCount, "The observed byte count cannot be smaller than the retained snapshot.");
        if (isTruncated != (snapshot.Length < observedByteCount))
            throw new ArgumentException("The truncation flag must equal whether the retained snapshot is shorter than the observed byte count.", nameof(isTruncated));

        Kind = kind;
        TransportSessionId = transportSessionId;
        State = state;
        _snapshot = snapshot.ToArray();
        ObservedByteCount = observedByteCount;
        IsTruncated = isTruncated;
    }

    /// <summary>Gets the transport fault kind.</summary>
    public HsmsTransportFaultKind Kind { get; }

    /// <summary>Gets the StreamFrame TCP-session generation.</summary>
    public long TransportSessionId { get; }

    /// <summary>Gets the HSMS session state observed with the fault.</summary>
    public HsmsSessionState State { get; }

    /// <summary>Gets the copied, bounded network-prefix snapshot.</summary>
    public ReadOnlySpan<byte> Snapshot => _snapshot;

    /// <summary>Gets the number of bytes observed for the framing error.</summary>
    public long ObservedByteCount { get; }

    /// <summary>Gets whether <see cref="Snapshot"/> is a truncated prefix.</summary>
    public bool IsTruncated { get; }
}
