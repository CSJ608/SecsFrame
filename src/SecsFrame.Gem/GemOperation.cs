namespace SecsFrame.Gem;

/// <summary>Identifies a foundational GEM service operation.</summary>
public enum GemOperation
{
    /// <summary>Establish communications.</summary>
    EstablishCommunication,

    /// <summary>Check whether the peer is online.</summary>
    AreYouOnline,

    /// <summary>Request equipment online.</summary>
    RequestOnline,

    /// <summary>Request equipment offline.</summary>
    RequestOffline,

    /// <summary>Read status variables.</summary>
    ReadStatusVariables,

    /// <summary>Read equipment constants.</summary>
    ReadEquipmentConstants,

    /// <summary>Read the equipment clock.</summary>
    GetClock,

    /// <summary>Set the equipment clock.</summary>
    SetClock,

    /// <summary>Replace the Equipment report definitions.</summary>
    DefineReports,

    /// <summary>Replace the Equipment event-to-report links.</summary>
    LinkEventReports,

    /// <summary>Send or handle a Collection Event.</summary>
    CollectionEvent,

    /// <summary>Send or handle an alarm notification.</summary>
    AlarmNotification,
}
