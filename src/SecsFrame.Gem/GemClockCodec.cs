namespace SecsFrame.Gem;

/// <summary>Provides explicit, replaceable GEM clock text encoding.</summary>
public sealed class GemClockCodec
{
    private readonly GemTimeEncoder _encoder;
    private readonly GemTimeDecoder _decoder;

    /// <summary>Creates a clock codec from application-owned delegates.</summary>
    public GemClockCodec(GemTimeEncoder encoder, GemTimeDecoder decoder)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
    }

    /// <summary>Encodes a clock value and validates seven-bit ASCII.</summary>
    public string Encode(DateTimeOffset value)
    {
        var encoded = _encoder(value) ??
            throw new InvalidOperationException("The GEM clock encoder returned null.");
        _ = SecsItem.Ascii(encoded);
        return encoded;
    }

    /// <summary>Decodes a clock value.</summary>
    public DateTimeOffset Decode(string value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        return _decoder(value);
    }
}
