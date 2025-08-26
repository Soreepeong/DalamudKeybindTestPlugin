using System;

namespace SamplePlugin;

/// <summary>Specifies which modifier keys in keyboard are (to be) held.</summary>
/// <remarks>Does not make distinction between left and right side modifier keys.</remarks>
[Flags]
public enum KeyboardModifierFlags : byte
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2,
}