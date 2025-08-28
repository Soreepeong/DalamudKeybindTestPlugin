using System;

namespace SamplePlugin;

/// <summary>Specifies which modifier keys in gamepad are (to be) held.</summary>
[Flags]
public enum GamepadModifierFlags : byte
{
    None = 0,
    LeftTrigger = 1 << 0,
    RightTrigger = 1 << 1,
    LeftBumper = 1 << 2,
    RightBumper = 1 << 3,
    Start = 1 << 6,
    Select = 1 << 7,
}
