using FFXIVClientStructs.FFXIV.Client.UI;

namespace SamplePlugin;

public enum SeVirtualKeyInputDeviceType
{
    None,
    Keyboard,
    Mouse,
    Gamepad,
}

public static class SeVirtualKeyInputDeviceTypeExtensions
{
    public static SeVirtualKeyInputDeviceType GetInputDeviceType(this SeVirtualKey key) => key switch
    {
        SeVirtualKey.NO_KEY => SeVirtualKeyInputDeviceType.None,
        < SeVirtualKey.PAD_LMB => SeVirtualKeyInputDeviceType.Keyboard,
        <= SeVirtualKey.PAD_MB7 => SeVirtualKeyInputDeviceType.Mouse,
        <= SeVirtualKey.PAD_Start => SeVirtualKeyInputDeviceType.Gamepad,
        _ => SeVirtualKeyInputDeviceType.None
    };
}
