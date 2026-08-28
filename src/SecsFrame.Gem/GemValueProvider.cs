namespace SecsFrame.Gem;

/// <summary>Reads one application-owned dynamic GEM value.</summary>
public delegate ValueTask<SecsItem> GemValueProvider(
    CancellationToken cancellationToken);
