using System.Net;

namespace SecsFrame;

/// <summary>
/// Immutable network, protocol-session, and timer settings for an
/// <see cref="HsmsConnection"/>.
/// </summary>
public sealed class HsmsConnectionOptions
{
    /// <summary>The default number of retained T8 observations.</summary>
    public const int DefaultTransportFaultObservationCapacity = 16;

    /// <summary>The engineering reference T3 reply timeout, 45 seconds.</summary>
    /// <remarks>
    /// Cross-referenced with the MIT-licensed Secs4Net implementation and
    /// common HSMS-SS deployment practice; not verified against an authorized
    /// SEMI E37/E37.1 copy and not a claim of standard default values.
    /// </remarks>
    public static readonly TimeSpan ReferenceT3 = TimeSpan.FromSeconds(45);

    /// <summary>The engineering reference T5 connect retry interval, 10 seconds.</summary>
    /// <remarks>
    /// Cross-referenced with the MIT-licensed Secs4Net implementation and
    /// common HSMS-SS deployment practice; not verified against an authorized
    /// SEMI E37/E37.1 copy and not a claim of standard default values.
    /// </remarks>
    public static readonly TimeSpan ReferenceT5 = TimeSpan.FromSeconds(10);

    /// <summary>The engineering reference T6 control reply timeout, 5 seconds.</summary>
    /// <remarks>
    /// Cross-referenced with the MIT-licensed Secs4Net implementation and
    /// common HSMS-SS deployment practice; not verified against an authorized
    /// SEMI E37/E37.1 copy and not a claim of standard default values.
    /// </remarks>
    public static readonly TimeSpan ReferenceT6 = TimeSpan.FromSeconds(5);

    /// <summary>The engineering reference T7 selection timeout, 10 seconds.</summary>
    /// <remarks>
    /// Cross-referenced with the MIT-licensed Secs4Net implementation and
    /// common HSMS-SS deployment practice; not verified against an authorized
    /// SEMI E37/E37.1 copy and not a claim of standard default values.
    /// </remarks>
    public static readonly TimeSpan ReferenceT7 = TimeSpan.FromSeconds(10);

    /// <summary>The engineering reference T8 incomplete-message timeout, 5 seconds.</summary>
    /// <remarks>
    /// Cross-referenced with the MIT-licensed Secs4Net implementation and
    /// common HSMS-SS deployment practice; not verified against an authorized
    /// SEMI E37/E37.1 copy and not a claim of standard default values.
    /// </remarks>
    public static readonly TimeSpan ReferenceT8 = TimeSpan.FromSeconds(5);

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
    /// <param name="enableTransportFaultObservation">
    /// Enables the separate bounded T8 prefix-snapshot observation stream.
    /// </param>
    /// <param name="transportFaultObservationCapacity">
    /// The maximum queued T8 observations before the oldest item is discarded.
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
        bool enableControlMessageObservation = false,
        bool enableTransportFaultObservation = false,
        int transportFaultObservationCapacity =
            DefaultTransportFaultObservationCapacity)
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
        if (transportFaultObservationCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transportFaultObservationCapacity),
                transportFaultObservationCapacity,
                "The transport-fault observation capacity must be positive.");
        }

        Port = port;
        ConnectionMode = connectionMode;
        SessionId = sessionId;
        T3 = t3;
        T5 = t5;
        T6 = t6;
        T7 = t7;
        T8 = t8;
        EnableControlMessageObservation = enableControlMessageObservation;
        EnableTransportFaultObservation = enableTransportFaultObservation;
        TransportFaultObservationCapacity = transportFaultObservationCapacity;
    }

    /// <summary>
    /// Creates connection settings that fill T3 through T8 with the
    /// engineering reference values.
    /// </summary>
    /// <param name="ipAddress">The remote address in Active mode or local bind address in Passive mode.</param>
    /// <param name="port">The TCP port, from 1 through 65535.</param>
    /// <param name="connectionMode">Which peer initiates the TCP connection.</param>
    /// <param name="sessionId">The protocol Session ID used by outgoing data messages.</param>
    /// <param name="enableControlMessageObservation">
    /// Enables the separate restricted-metadata control-message observation stream.
    /// </param>
    /// <param name="enableTransportFaultObservation">
    /// Enables the separate bounded T8 prefix-snapshot observation stream.
    /// </param>
    /// <param name="transportFaultObservationCapacity">
    /// The maximum queued T8 observations before the oldest item is discarded.
    /// </param>
    /// <remarks>
    /// The reference timers are cross-referenced with the MIT-licensed
    /// Secs4Net implementation and common HSMS-SS deployment practice. They
    /// have not been verified against an authorized SEMI E37/E37.1 copy and
    /// are not standard default values; callers that need audited values
    /// must use the explicit constructor.
    /// </remarks>
    public static HsmsConnectionOptions CreateWithReferenceTimers(
        IPAddress ipAddress,
        int port,
        HsmsConnectionMode connectionMode,
        ushort sessionId,
        bool enableControlMessageObservation = false,
        bool enableTransportFaultObservation = false,
        int transportFaultObservationCapacity =
            DefaultTransportFaultObservationCapacity)
        => new(
            ipAddress,
            port,
            connectionMode,
            sessionId,
            ReferenceT3,
            ReferenceT5,
            ReferenceT6,
            ReferenceT7,
            ReferenceT8,
            enableControlMessageObservation,
            enableTransportFaultObservation,
            transportFaultObservationCapacity);

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

    /// <summary>Gets whether the bounded T8 prefix-snapshot stream is enabled.</summary>
    public bool EnableTransportFaultObservation { get; }

    /// <summary>Gets the maximum number of queued T8 observations.</summary>
    public int TransportFaultObservationCapacity { get; }

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
