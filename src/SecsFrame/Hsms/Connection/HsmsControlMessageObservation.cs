namespace SecsFrame;

/// <summary>
/// Describes the restricted metadata observed for one HSMS control message.
/// </summary>
/// <remarks>
/// The observation contains only the ten-byte HSMS header. It does not expose
/// frame bodies, transport-session generations, raw network bytes, or a clock.
/// </remarks>
public sealed class HsmsControlMessageObservation
{
    internal HsmsControlMessageObservation(
        HsmsControlMessageDirection direction,
        HsmsSessionState state,
        HsmsMessageHeader header)
    {
        if (direction != HsmsControlMessageDirection.Sent &&
            direction != HsmsControlMessageDirection.Received)
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction),
                direction,
                "Unknown control-message direction.");
        }

        if (!Enum.IsDefined(typeof(HsmsSessionState), state))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "Unknown HSMS session state.");
        }

        if (header.IsDataMessage)
        {
            throw new ArgumentException(
                "A control-message observation requires a nonzero SType.",
                nameof(header));
        }

        Direction = direction;
        State = state;
        Header = header;
    }

    /// <summary>Gets the local message direction.</summary>
    public HsmsControlMessageDirection Direction { get; }

    /// <summary>Gets the session state observed with the control message.</summary>
    public HsmsSessionState State { get; }

    /// <summary>Gets the original ten-byte HSMS header fields.</summary>
    public HsmsMessageHeader Header { get; }
}
