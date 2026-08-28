namespace SecsFrame;

/// <summary>Identifies an HSMS timer associated with a diagnostic.</summary>
public enum HsmsTimer
{
    /// <summary>Reply transaction timeout.</summary>
    T3,

    /// <summary>Active connection retry delay.</summary>
    T5,

    /// <summary>Control transaction timeout.</summary>
    T6,

    /// <summary>Selection timeout after TCP connection.</summary>
    T7,

    /// <summary>Incomplete frame receive timeout.</summary>
    T8,
}
