namespace SecsFrame;

internal enum HsmsSelectStatus : byte
{
    Success = 0,
    AlreadySelected = 1,
    NotReady = 2,
    Unavailable = 3,
}
