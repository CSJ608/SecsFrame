namespace SecsFrame.CommunicationDemo.Models;

internal sealed record PendingReply(
    long Id,
    DateTimeOffset ReceivedAt,
    ushort SessionId,
    uint SystemBytes,
    byte Stream,
    byte Function,
    string SuggestedSecondarySml);
