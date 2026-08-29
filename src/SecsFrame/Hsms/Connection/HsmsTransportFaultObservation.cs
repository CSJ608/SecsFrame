namespace SecsFrame;

/// <summary>
/// Describes one explicitly enabled, session-associated HSMS transport fault.
/// </summary>
/// <remarks>
/// The snapshot is a defensive copy of at most the first 8 KiB retained by
/// StreamFrame. It can include the four-byte HSMS length prefix and is not
/// guaranteed to contain the complete network fragment.
/// </remarks>
public sealed class HsmsTransportFaultObservation
{
    private readonly byte[] _snapshot;

    /// <summary>The maximum prefix snapshot retained by StreamFrame.</summary>
    public const int MaxSnapshotBytes = 8 * 1024;

    internal HsmsTransportFaultObservation(
        HsmsTransportFaultKind kind,
        long transportSessionId,
        HsmsSessionState state,
        ReadOnlySpan<byte> snapshot)
    {
        if (!Enum.IsDefined(typeof(HsmsTransportFaultKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown HSMS transport-fault kind.");
        if (transportSessionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(transportSessionId), transportSessionId, "The transport session identifier must be positive.");
        if (!Enum.IsDefined(typeof(HsmsSessionState), state))
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown HSMS session state.");
        if (snapshot.IsEmpty)
            throw new ArgumentException("A T8 observation requires a nonempty prefix snapshot.", nameof(snapshot));
        if (snapshot.Length > MaxSnapshotBytes)
            throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.Length, $"A T8 prefix snapshot cannot exceed {MaxSnapshotBytes} bytes.");

        Kind = kind;
        TransportSessionId = transportSessionId;
        State = state;
        _snapshot = snapshot.ToArray();
    }

    /// <summary>Gets the transport fault kind.</summary>
    public HsmsTransportFaultKind Kind { get; }

    /// <summary>Gets the StreamFrame TCP-session generation.</summary>
    public long TransportSessionId { get; }

    /// <summary>Gets the HSMS session state observed with the fault.</summary>
    public HsmsSessionState State { get; }

    /// <summary>Gets the copied, bounded network-prefix snapshot.</summary>
    public ReadOnlySpan<byte> Snapshot => _snapshot;
}
