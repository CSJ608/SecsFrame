namespace SecsFrame.Gem;

/// <summary>Handles a decoded Collection Event and chooses its acknowledgement.</summary>
/// <returns><see langword="true"/> to accept the event; otherwise false.</returns>
public delegate ValueTask<bool> GemCollectionEventHandler(
    GemCollectionEvent collectionEvent,
    CancellationToken cancellationToken);
