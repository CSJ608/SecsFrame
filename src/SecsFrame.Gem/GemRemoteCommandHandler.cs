namespace SecsFrame.Gem;

/// <summary>Executes a decoded remote command and returns its raw result.</summary>
public delegate ValueTask<GemRemoteCommandResult> GemRemoteCommandHandler(
    GemRemoteCommand command,
    CancellationToken cancellationToken);
