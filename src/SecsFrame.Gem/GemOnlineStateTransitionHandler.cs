namespace SecsFrame.Gem;

/// <summary>Decides whether to accept a Host-requested online-state transition.</summary>
public delegate ValueTask<bool> GemOnlineStateTransitionHandler(
    GemOnlineState currentState,
    GemOnlineState requestedState,
    CancellationToken cancellationToken);
