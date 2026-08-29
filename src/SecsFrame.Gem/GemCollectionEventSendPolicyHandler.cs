namespace SecsFrame.Gem;

/// <summary>
/// Decides whether one configured Collection Event may collect values and be sent.
/// </summary>
public delegate ValueTask<bool> GemCollectionEventSendPolicyHandler(
    GemCommunicationState communicationState,
    GemOnlineState onlineState,
    SecsItem dataId,
    SecsItem eventId,
    CancellationToken cancellationToken);
