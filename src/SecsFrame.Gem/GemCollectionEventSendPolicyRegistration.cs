namespace SecsFrame.Gem;

/// <summary>Owns one Equipment Collection Event send-policy registration.</summary>
public sealed class GemCollectionEventSendPolicyRegistration : IDisposable
{
    private Action<GemCollectionEventSendPolicyRegistration>? _unregister;

    internal GemCollectionEventSendPolicyRegistration(
        GemCollectionEventSendPolicyHandler handler,
        Action<GemCollectionEventSendPolicyRegistration> unregister)
    {
        Handler = handler;
        _unregister = unregister;
    }

    internal GemCollectionEventSendPolicyHandler Handler { get; }

    /// <summary>Removes this exact policy registration. Repeated calls are harmless.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _unregister, null)?.Invoke(this);
}
