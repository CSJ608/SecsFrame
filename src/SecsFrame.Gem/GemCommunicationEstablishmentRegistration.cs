namespace SecsFrame.Gem;

/// <summary>Owns one communication-establishment handler registration.</summary>
public sealed class GemCommunicationEstablishmentRegistration : IDisposable
{
    private Action<GemCommunicationEstablishmentRegistration>? _unregister;

    internal GemCommunicationEstablishmentRegistration(
        GemCommunicationEstablishmentHandler handler,
        Action<GemCommunicationEstablishmentRegistration> unregister)
    {
        Handler = handler;
        _unregister = unregister;
    }

    internal GemCommunicationEstablishmentHandler Handler { get; }

    /// <summary>Removes this exact handler registration. Repeated calls are harmless.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _unregister, null)?.Invoke(this);
}
