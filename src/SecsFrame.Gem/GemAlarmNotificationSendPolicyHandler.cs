namespace SecsFrame.Gem;

/// <summary>Decides whether one alarm notification may be sent.</summary>
public delegate ValueTask<bool> GemAlarmNotificationSendPolicyHandler(
    GemCommunicationState communicationState,
    GemOnlineState onlineState,
    GemAlarmNotification notification,
    CancellationToken cancellationToken);
