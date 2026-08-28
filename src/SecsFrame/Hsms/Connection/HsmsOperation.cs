namespace SecsFrame;

/// <summary>Identifies the operation associated with an HSMS diagnostic.</summary>
public enum HsmsOperation
{
    /// <summary>No more specific operation is available.</summary>
    None,

    /// <summary>Opening or maintaining the TCP transport.</summary>
    Connect,

    /// <summary>Selecting the HSMS session.</summary>
    Select,

    /// <summary>Running an HSMS Linktest transaction.</summary>
    Linktest,

    /// <summary>Deselecting the HSMS session.</summary>
    Deselect,

    /// <summary>Separating the HSMS session.</summary>
    Separate,

    /// <summary>Sending an HSMS data message.</summary>
    SendData,

    /// <summary>Waiting for a Secondary data message.</summary>
    WaitForSecondary,

    /// <summary>Receiving an HSMS frame.</summary>
    ReceiveFrame,

    /// <summary>Decoding an HSMS data message and its SECS-II Item.</summary>
    DecodeData,
}
