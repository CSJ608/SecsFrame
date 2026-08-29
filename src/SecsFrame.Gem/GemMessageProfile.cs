using System.Globalization;

namespace SecsFrame.Gem;

/// <summary>
/// Configures the message pairs and acknowledgement values used by the
/// foundational GEM services.
/// </summary>
/// <remarks>
/// A profile is an interoperability configuration, not a declaration of
/// conformance to a particular SEMI E30 revision.
/// </remarks>
public sealed class GemMessageProfile
{
    private static readonly GemMessagePair BaselineAlarmNotification =
        new(5, 1, 2);
    private static readonly GemMessagePair BaselineRemoteCommand =
        new(2, 41, 42);
    private static readonly GemMessagePair BaselineListAlarms =
        new(5, 5, 6);

    /// <summary>
    /// Creates a foundational profile using the engineering-baseline alarm and
    /// remote-command pairs.
    /// </summary>
    public GemMessageProfile(
        GemMessagePair establishCommunication,
        GemMessagePair areYouOnline,
        GemMessagePair requestOnline,
        GemMessagePair requestOffline,
        GemMessagePair readStatusVariables,
        GemMessagePair readEquipmentConstants,
        GemMessagePair getClock,
        GemMessagePair setClock,
        GemMessagePair defineReports,
        GemMessagePair linkEventReports,
        GemMessagePair collectionEvent,
        byte acceptedAcknowledgement,
        byte failedAcknowledgement,
        GemClockCodec clockCodec)
        : this(
            establishCommunication,
            areYouOnline,
            requestOnline,
            requestOffline,
            readStatusVariables,
            readEquipmentConstants,
            getClock,
            setClock,
            defineReports,
            linkEventReports,
            collectionEvent,
            BaselineAlarmNotification,
            acceptedAcknowledgement,
            failedAcknowledgement,
            clockCodec)
    {
    }

    /// <summary>
    /// Creates a foundational profile using the engineering-baseline
    /// remote-command pair.
    /// </summary>
    public GemMessageProfile(
        GemMessagePair establishCommunication,
        GemMessagePair areYouOnline,
        GemMessagePair requestOnline,
        GemMessagePair requestOffline,
        GemMessagePair readStatusVariables,
        GemMessagePair readEquipmentConstants,
        GemMessagePair getClock,
        GemMessagePair setClock,
        GemMessagePair defineReports,
        GemMessagePair linkEventReports,
        GemMessagePair collectionEvent,
        GemMessagePair alarmNotification,
        byte acceptedAcknowledgement,
        byte failedAcknowledgement,
        GemClockCodec clockCodec)
        : this(
            establishCommunication,
            areYouOnline,
            requestOnline,
            requestOffline,
            readStatusVariables,
            readEquipmentConstants,
            getClock,
            setClock,
            defineReports,
            linkEventReports,
            collectionEvent,
            alarmNotification,
            BaselineRemoteCommand,
            acceptedAcknowledgement,
            failedAcknowledgement,
            clockCodec)
    {
    }

    /// <summary>
    /// Creates a foundational profile using the engineering-baseline
    /// alarm-list pair.
    /// </summary>
    public GemMessageProfile(
        GemMessagePair establishCommunication,
        GemMessagePair areYouOnline,
        GemMessagePair requestOnline,
        GemMessagePair requestOffline,
        GemMessagePair readStatusVariables,
        GemMessagePair readEquipmentConstants,
        GemMessagePair getClock,
        GemMessagePair setClock,
        GemMessagePair defineReports,
        GemMessagePair linkEventReports,
        GemMessagePair collectionEvent,
        GemMessagePair alarmNotification,
        GemMessagePair remoteCommand,
        byte acceptedAcknowledgement,
        byte failedAcknowledgement,
        GemClockCodec clockCodec)
        : this(
            establishCommunication,
            areYouOnline,
            requestOnline,
            requestOffline,
            readStatusVariables,
            readEquipmentConstants,
            getClock,
            setClock,
            defineReports,
            linkEventReports,
            collectionEvent,
            alarmNotification,
            remoteCommand,
            BaselineListAlarms,
            acceptedAcknowledgement,
            failedAcknowledgement,
            clockCodec)
    {
    }

    /// <summary>Creates an explicit foundational message profile.</summary>
    public GemMessageProfile(
        GemMessagePair establishCommunication,
        GemMessagePair areYouOnline,
        GemMessagePair requestOnline,
        GemMessagePair requestOffline,
        GemMessagePair readStatusVariables,
        GemMessagePair readEquipmentConstants,
        GemMessagePair getClock,
        GemMessagePair setClock,
        GemMessagePair defineReports,
        GemMessagePair linkEventReports,
        GemMessagePair collectionEvent,
        GemMessagePair alarmNotification,
        GemMessagePair remoteCommand,
        GemMessagePair listAlarms,
        byte acceptedAcknowledgement,
        byte failedAcknowledgement,
        GemClockCodec clockCodec)
    {
        if (acceptedAcknowledgement == failedAcknowledgement)
        {
            throw new ArgumentException(
                "Accepted and failed acknowledgement values must differ.",
                nameof(failedAcknowledgement));
        }

        ValidateUniquePrimaryRoutes(
            establishCommunication,
            areYouOnline,
            requestOnline,
            requestOffline,
            readStatusVariables,
            readEquipmentConstants,
            getClock,
            setClock,
            defineReports,
            linkEventReports,
            collectionEvent,
            alarmNotification,
            remoteCommand,
            listAlarms);
        EstablishCommunication = establishCommunication;
        AreYouOnline = areYouOnline;
        RequestOnline = requestOnline;
        RequestOffline = requestOffline;
        ReadStatusVariables = readStatusVariables;
        ReadEquipmentConstants = readEquipmentConstants;
        GetClock = getClock;
        SetClock = setClock;
        DefineReports = defineReports;
        LinkEventReports = linkEventReports;
        CollectionEvent = collectionEvent;
        AlarmNotification = alarmNotification;
        RemoteCommand = remoteCommand;
        ListAlarms = listAlarms;
        AcceptedAcknowledgement = acceptedAcknowledgement;
        FailedAcknowledgement = failedAcknowledgement;
        ClockCodec = clockCodec ?? throw new ArgumentNullException(nameof(clockCodec));
    }

    /// <summary>Gets the communication-establishment message pair.</summary>
    public GemMessagePair EstablishCommunication { get; }

    /// <summary>Gets the online-query message pair.</summary>
    public GemMessagePair AreYouOnline { get; }

    /// <summary>Gets the online-request message pair.</summary>
    public GemMessagePair RequestOnline { get; }

    /// <summary>Gets the offline-request message pair.</summary>
    public GemMessagePair RequestOffline { get; }

    /// <summary>Gets the status-variable request message pair.</summary>
    public GemMessagePair ReadStatusVariables { get; }

    /// <summary>Gets the equipment-constant request message pair.</summary>
    public GemMessagePair ReadEquipmentConstants { get; }

    /// <summary>Gets the clock-read message pair.</summary>
    public GemMessagePair GetClock { get; }

    /// <summary>Gets the clock-set message pair.</summary>
    public GemMessagePair SetClock { get; }

    /// <summary>Gets the report-definition message pair.</summary>
    public GemMessagePair DefineReports { get; }

    /// <summary>Gets the event-to-report linking message pair.</summary>
    public GemMessagePair LinkEventReports { get; }

    /// <summary>Gets the Collection Event message pair.</summary>
    public GemMessagePair CollectionEvent { get; }

    /// <summary>Gets the alarm-notification message pair.</summary>
    public GemMessagePair AlarmNotification { get; }

    /// <summary>Gets the remote-command message pair.</summary>
    public GemMessagePair RemoteCommand { get; }

    /// <summary>Gets the alarm-list request/reply message pair.</summary>
    public GemMessagePair ListAlarms { get; }

    /// <summary>Gets the configured successful acknowledgement byte.</summary>
    public byte AcceptedAcknowledgement { get; }

    /// <summary>Gets the configured unsuccessful acknowledgement byte.</summary>
    public byte FailedAcknowledgement { get; }

    /// <summary>Gets the explicit clock text codec.</summary>
    public GemClockCodec ClockCodec { get; }

    /// <summary>
    /// Creates the library's conventional, non-normative engineering baseline.
    /// </summary>
    public static GemMessageProfile CreateEngineeringBaseline()
        => new(
            new GemMessagePair(1, 13, 14),
            new GemMessagePair(1, 1, 2),
            new GemMessagePair(1, 17, 18),
            new GemMessagePair(1, 15, 16),
            new GemMessagePair(1, 3, 4),
            new GemMessagePair(2, 13, 14),
            new GemMessagePair(2, 17, 18),
            new GemMessagePair(2, 31, 32),
            new GemMessagePair(2, 33, 34),
            new GemMessagePair(2, 35, 36),
            new GemMessagePair(6, 11, 12),
            BaselineAlarmNotification,
            BaselineRemoteCommand,
            BaselineListAlarms,
            acceptedAcknowledgement: 0,
            failedAcknowledgement: 1,
            new GemClockCodec(EncodeBaselineTime, DecodeBaselineTime));

    private static string EncodeBaselineTime(DateTimeOffset value)
        => value.ToUniversalTime().ToString(
            "yyyyMMddHHmmssff",
            CultureInfo.InvariantCulture);

    private static DateTimeOffset DecodeBaselineTime(string value)
        => DateTimeOffset.ParseExact(
            value,
            "yyyyMMddHHmmssff",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static void ValidateUniquePrimaryRoutes(params GemMessagePair[] pairs)
    {
        var routes = new HashSet<ushort>();
        foreach (var pair in pairs)
        {
            var key = (ushort)((pair.Stream << 8) | pair.PrimaryFunction);
            if (!routes.Add(key))
            {
                throw new ArgumentException(
                    $"The profile contains duplicate Primary route " +
                    $"S{pair.Stream}F{pair.PrimaryFunction}.",
                    nameof(pairs));
            }
        }
    }
}
