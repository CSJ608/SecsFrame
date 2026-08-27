using System.Runtime.InteropServices;

namespace SecsFrame;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct HsmsTransportFrame(
    HsmsTransportSessionId SessionId,
    HsmsFrame Frame);
