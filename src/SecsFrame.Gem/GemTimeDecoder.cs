namespace SecsFrame.Gem;

/// <summary>Decodes a GEM ASCII time value into an application clock value.</summary>
public delegate DateTimeOffset GemTimeDecoder(string value);
