namespace SecsFrame.Gem;

/// <summary>
/// Decides whether to accept a peer-requested communication establishment.
/// </summary>
/// <param name="peerIdentity">The identity supplied by the requesting peer.</param>
/// <param name="cancellationToken">Cancels the pending request.</param>
/// <returns><see langword="true"/> to accept the request; otherwise, <see langword="false"/>.</returns>
public delegate ValueTask<bool> GemCommunicationEstablishmentHandler(
    GemIdentity peerIdentity,
    CancellationToken cancellationToken);
