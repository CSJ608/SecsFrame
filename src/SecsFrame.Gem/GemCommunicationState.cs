namespace SecsFrame.Gem;

/// <summary>Identifies the observed GEM communications state.</summary>
public enum GemCommunicationState
{
    /// <summary>No GEM communications dialogue has completed.</summary>
    NotCommunicating,

    /// <summary>A GEM communications establishment dialogue completed successfully.</summary>
    Communicating,
}
