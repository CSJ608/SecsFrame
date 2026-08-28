namespace SecsFrame.Gem;

/// <summary>Owns one exact runtime status-variable or equipment-constant registration.</summary>
public sealed class GemValueRegistration : IDisposable
{
    private Action<GemValueRegistration>? _unregister;

    internal GemValueRegistration(
        SecsItem id,
        GemValueProvider provider,
        Action<GemValueRegistration> unregister)
    {
        Id = id;
        Provider = provider;
        _unregister = unregister;
    }

    /// <summary>Gets the exact dynamic identifier.</summary>
    public SecsItem Id { get; }

    internal GemValueProvider Provider { get; }

    /// <summary>Removes this exact registration. Repeated calls are harmless.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _unregister, null)?.Invoke(this);
}
