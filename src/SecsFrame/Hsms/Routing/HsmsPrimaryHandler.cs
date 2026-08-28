namespace SecsFrame;

/// <summary>Handles one dynamically routed HSMS data message.</summary>
/// <param name="context">The incoming message and transaction identity.</param>
/// <param name="cancellationToken">Cancels handler work and any automatic reply.</param>
/// <returns>
/// The Secondary to send, or <see langword="null"/> when the handler intentionally
/// sends no reply.
/// </returns>
public delegate ValueTask<SecsMessage?> HsmsPrimaryHandler(
    HsmsPrimaryContext context,
    CancellationToken cancellationToken);
