namespace SecsFrame;

/// <summary>Describes the current HSMS transport and selection state.</summary>
public enum HsmsSessionState
{
    /// <summary>No TCP session is currently open.</summary>
    Disconnected,

    /// <summary>A TCP session is open but is not selected.</summary>
    Connected,

    /// <summary>An Active connection is waiting for Select Response.</summary>
    Selecting,

    /// <summary>The HSMS session is selected and can exchange data messages.</summary>
    Selected,
}
