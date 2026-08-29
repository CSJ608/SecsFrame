using System.Net;

namespace SecsFrame;

/// <summary>
/// Immutable network, protocol-session, and timer settings for an
/// <see cref="HsmsConnection"/>.
/// </summary>
public sealed class HsmsConnectionOptions
{
    /// <summary>Creates explicit HSMS connection settings.</summary>
    /// <param name="ipAddress">The remote address in Active mode or local bind address in Passive mode.</param>
    /// <param name="port">The TCP port, from 1 through 65535.</param>
    /// <param name="connectionMode">Which peer initiates the TCP connection.</param>
    /// <param name="sessionId">The protocol Session ID used by outgoing data messages.</param>
    /// <param name="t3">The data transaction reply timeout.</param>
    /// <param name="t5">The Active connection retry interval.</param>
    /// <param name="t6">The control transaction reply timeout.</param>
    /// <param name="t7">The selection timeout after TCP connection.</param>
    /// <param name="t8">The positive whole-millisecond incomplete-message receive timeout.</param>
    /// <param name="enableControlMessageObservation">
    /// Enables the separate restricted-metadata control-message observation stream.
    /// </param>
    public HsmsConnectionOptions(
        IPAddress ipAddress,
        int port,
        HsmsConnectionMode connectionMode,
        ushort sessionId,
        TimeSpan t3,
        TimeSpan t5,
        TimeSpan t6,
        TimeSpan t7,
        TimeSpan t8,
        bool enableControlMessageObservation = false)
    {
        IpAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
        if (port is < 1 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port),
                port,
                "The TCP port must be between 1 and 65535.");
        }

        if (connectionMode != HsmsConnectionMode.Active &&
            connectionMode != HsmsConnectionMode.Passive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectionMode),
                connectionMode,
                "The HSMS connection mode must be Active or Passive.");
        }

        if (sessionId == ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sessionId),
                sessionId,
                "The protocol Session ID cannot use the control-message value 65535.");
        }

        ValidatePositive(t3, nameof(t3), "T3");
        ValidateT5(t5, nameof(t5));
        ValidatePositive(t6, nameof(t6), "T6");
        ValidatePositive(t7, nameof(t7), "T7");
        ValidateStreamFrameMilliseconds(t8, nameof(t8), "T8");

        Port = port;
        ConnectionMode = connectionMode;
        SessionId = sessionId;
        T3 = t3;
        T5 = t5;
        T6 = t6;
        T7 = t7;
        T8 = t8;
        EnableControlMessageObservation = enableControlMessageObservation;
    }

    /// <summary>Gets the remote or local bind IP address.</summary>
    public IPAddress IpAddress { get; }

    /// <summary>Gets the TCP port.</summary>
    public int Port { get; }

    /// <summary>Gets which peer initiates the TCP connection.</summary>
    public HsmsConnectionMode ConnectionMode { get; }

    /// <summary>Gets the protocol Session ID for outgoing data messages.</summary>
    public ushort SessionId { get; }

    /// <summary>Gets the data transaction reply timeout.</summary>
    public TimeSpan T3 { get; }

    /// <summary>Gets the Active connection retry interval.</summary>
    public TimeSpan T5 { get; }

    /// <summary>Gets the control transaction reply timeout.</summary>
    public TimeSpan T6 { get; }

    /// <summary>Gets the selection timeout after TCP connection.</summary>
    public TimeSpan T7 { get; }

    /// <summary>Gets the incomplete-message receive timeout.</summary>
    public TimeSpan T8 { get; }

    /// <summary>
    /// Gets whether the separate control-message metadata stream is enabled.
    /// </summary>
    public bool EnableControlMessageObservation { get; }

    private static void ValidatePositive(
        TimeSpan value,
        string parameterName,
        string timerName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"{timerName} must be positive.");
        }
    }

    private static void ValidateT5(
        TimeSpan value,
        string parameterName)
    {
        ValidateStreamFrameMilliseconds(value, parameterName, "T5");
    }

    private static void ValidateStreamFrameMilliseconds(
        TimeSpan value,
        string parameterName,
        string timerName)
    {
        ValidatePositive(value, parameterName, timerName);
        if (value.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"{timerName} must be representable as a whole number of milliseconds.");
        }

        var milliseconds = value.Ticks / TimeSpan.TicksPerMillisecond;
        if (milliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"{timerName} exceeds the supported StreamFrame millisecond range.");
        }
    }
}
