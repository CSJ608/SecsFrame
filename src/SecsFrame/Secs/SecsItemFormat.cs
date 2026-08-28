namespace SecsFrame;

/// <summary>SECS-II six-bit Item format codes targeted to SEMI E5-0725.</summary>
public enum SecsItemFormat : byte
{
    /// <summary>A sequence of nested Items.</summary>
    List = 0x00,

    /// <summary>Uninterpreted bytes.</summary>
    Binary = 0x08,

    /// <summary>Boolean values, one byte per value.</summary>
    Boolean = 0x09,

    /// <summary>Seven-bit ASCII characters.</summary>
    Ascii = 0x10,

    /// <summary>JIS-8 encoded bytes.</summary>
    Jis8 = 0x11,

    /// <summary>Signed 8-byte integers.</summary>
    I8 = 0x18,

    /// <summary>Signed 1-byte integers.</summary>
    I1 = 0x19,

    /// <summary>Signed 2-byte integers.</summary>
    I2 = 0x1A,

    /// <summary>Signed 4-byte integers.</summary>
    I4 = 0x1C,

    /// <summary>IEEE 754 8-byte floating-point values.</summary>
    F8 = 0x20,

    /// <summary>IEEE 754 4-byte floating-point values.</summary>
    F4 = 0x24,

    /// <summary>Unsigned 8-byte integers.</summary>
    U8 = 0x28,

    /// <summary>Unsigned 1-byte integers.</summary>
    U1 = 0x29,

    /// <summary>Unsigned 2-byte integers.</summary>
    U2 = 0x2A,

    /// <summary>Unsigned 4-byte integers.</summary>
    U4 = 0x2C,
}
