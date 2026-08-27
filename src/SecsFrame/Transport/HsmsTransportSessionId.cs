using System.Runtime.InteropServices;

namespace SecsFrame;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct HsmsTransportSessionId(long Value);
