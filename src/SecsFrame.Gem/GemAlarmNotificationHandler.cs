namespace SecsFrame.Gem;

/// <summary>Handles a decoded alarm notification and chooses its acknowledgement.</summary>
/// <returns><see langword="true"/> to accept the notification; otherwise false.</returns>
public delegate ValueTask<bool> GemAlarmNotificationHandler(
    GemAlarmNotification notification,
    CancellationToken cancellationToken);
