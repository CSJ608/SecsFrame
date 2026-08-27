namespace SecsFrame;

/// <summary>HSMS header SType values.</summary>
public enum HsmsMessageType : byte
{
    /// <summary>A SECS-II data message.</summary>
    DataMessage = 0,

    /// <summary>Request selection of an HSMS session.</summary>
    SelectRequest = 1,

    /// <summary>Reply to <see cref="SelectRequest"/>.</summary>
    SelectResponse = 2,

    /// <summary>Request deselection of an HSMS session.</summary>
    DeselectRequest = 3,

    /// <summary>Reply to <see cref="DeselectRequest"/>.</summary>
    DeselectResponse = 4,

    /// <summary>Request a link test.</summary>
    LinktestRequest = 5,

    /// <summary>Reply to <see cref="LinktestRequest"/>.</summary>
    LinktestResponse = 6,

    /// <summary>Reject an invalid HSMS message.</summary>
    RejectRequest = 7,

    /// <summary>Terminate the current HSMS session.</summary>
    SeparateRequest = 9,
}
