namespace SecsFrame;

internal sealed class HsmsSessionOptions
{
    public HsmsSessionOptions(
        HsmsConnectionMode connectionMode,
        TimeSpan controlReplyTimeout,
        TimeSpan selectionTimeout,
        bool enableControlMessageObservation = false)
    {
        if (connectionMode != HsmsConnectionMode.Active &&
            connectionMode != HsmsConnectionMode.Passive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectionMode),
                connectionMode,
                "The HSMS connection mode must be Active or Passive.");
        }

        if (controlReplyTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(controlReplyTimeout),
                controlReplyTimeout,
                "The control reply timeout must be positive.");
        }

        if (selectionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectionTimeout),
                selectionTimeout,
                "The selection timeout must be positive.");
        }

        ConnectionMode = connectionMode;
        ControlReplyTimeout = controlReplyTimeout;
        SelectionTimeout = selectionTimeout;
        EnableControlMessageObservation = enableControlMessageObservation;
    }

    public HsmsConnectionMode ConnectionMode { get; }

    public TimeSpan ControlReplyTimeout { get; }

    public TimeSpan SelectionTimeout { get; }

    public bool EnableControlMessageObservation { get; }
}
