namespace SecsFrame.Gem;

/// <summary>
/// Decides whether a decoded remote command may be passed to its executor.
/// </summary>
public delegate ValueTask<bool> GemRemoteCommandAcceptanceHandler(
    GemCommunicationState communicationState,
    GemOnlineState onlineState,
    GemRemoteCommand command,
    CancellationToken cancellationToken);
