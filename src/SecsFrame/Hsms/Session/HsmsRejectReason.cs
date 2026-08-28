namespace SecsFrame;

internal enum HsmsRejectReason : byte
{
    UnsupportedSessionType = 1,
    UnsupportedPresentationType = 2,
    TransactionNotOpen = 3,
    EntityNotSelected = 4,
}
