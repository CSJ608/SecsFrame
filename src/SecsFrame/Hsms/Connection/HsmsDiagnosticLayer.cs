namespace SecsFrame;

/// <summary>Identifies the SecsFrame layer that produced an HSMS diagnostic.</summary>
public enum HsmsDiagnosticLayer
{
    /// <summary>The TCP transport or transport-session adapter.</summary>
    Transport,

    /// <summary>The HSMS selection and control-session state machine.</summary>
    Session,

    /// <summary>The HSMS data transaction manager.</summary>
    Transaction,

    /// <summary>The HSMS or SECS-II message codec.</summary>
    Codec,
}
