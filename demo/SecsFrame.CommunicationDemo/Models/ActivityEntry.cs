namespace SecsFrame.CommunicationDemo.Models;

internal sealed record ActivityEntry(
    long Id,
    DateTimeOffset Timestamp,
    ActivityEntryTone Tone,
    string Category,
    string Title,
    string Summary,
    string? Detail);
