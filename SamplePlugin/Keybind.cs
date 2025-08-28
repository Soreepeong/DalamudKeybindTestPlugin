using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace SamplePlugin;

[StructLayout(LayoutKind.Explicit, Size = 16)]
public unsafe struct Keybind : IEquatable<Keybind>
{
    public const int MaxKeySequenceLength = 15;

    // Valid only if Key < 0xA0
    [FieldOffset(0)]
    public KeyboardModifierFlags KeyboardModifiers;

    // Valid only if Key >= 0xA7
    // Start/Select+DPad/ABXY are never bound by the game
    [FieldOffset(0)]
    public GamepadModifierFlags GamepadModifiers;

    // 00...9F (VK) A0...A6 (LMB MMB RMB X1 X2 X3 X4) A7... (↑↓←→ YAXB LB LT LS RB RT RS Select Start)
    // The game will reject certain keys (Win, Menu, IME keys, F13~F24, etc.)
    // Modifier keys should not be specified here (would result in unknown behavior?)
    [FieldOffset(1)]
    public fixed byte KeySequenceBuffer[MaxKeySequenceLength];

    [FieldOffset(0)]
    private int Value32;

    [FieldOffset(0)]
    private int Value8;

    public readonly ReadOnlySpan<SeVirtualKey> KeySequence =>
        MemoryMarshal.Cast<byte, SeVirtualKey>(
            MemoryMarshal.CreateReadOnlySpan(in this.KeySequenceBuffer[0], MaxKeySequenceLength).TrimEnd((byte)0));

    public readonly byte ModifierLength => (byte)BitOperations.PopCount((uint)this.Value8);

    public readonly bool IsEmpty => this.KeySequenceBuffer[0] == 0;

    public readonly bool IsKeybindKeyboard =>
        (SeVirtualKey)this.KeySequenceBuffer[0] is > SeVirtualKey.NO_KEY and < SeVirtualKey.PAD_LMB;

    public readonly bool IsKeybindMouse =>
        (SeVirtualKey)this.KeySequenceBuffer[0] is >= SeVirtualKey.PAD_LMB and <= SeVirtualKey.PAD_MB7;

    public static bool operator ==(Keybind left, Keybind right) => left.Equals(right);

    public static bool operator !=(Keybind left, Keybind right) => !left.Equals(right);

    public static Keybind FromKeyboard(KeyboardModifierFlags modifiers, params ReadOnlySpan<SeVirtualKey> keySequence)
    {
        if (keySequence.Length is 0 or > MaxKeySequenceLength)
            throw new ArgumentException($"Up to {MaxKeySequenceLength} keys are allowed.");

        var res = new Keybind {KeyboardModifiers = modifiers};
        for (var i = 0; i < keySequence.Length; i++)
        {
            if (keySequence[i] is <= SeVirtualKey.NO_KEY or >= SeVirtualKey.PAD_MB7)
                throw new ArgumentException($"{keySequence[i]} is not allowed.");
            res.KeySequenceBuffer[i] = (byte)keySequence[i];
        }

        return res;
    }

    public static Keybind FromGamepad(GamepadModifierFlags modifiers, params ReadOnlySpan<SeVirtualKey> keySequence)
    {
        if (keySequence.Length is 0 or > MaxKeySequenceLength)
            throw new ArgumentException($"Up to {MaxKeySequenceLength} keys are allowed.");

        var res = new Keybind {GamepadModifiers = modifiers};
        for (var i = 0; i < keySequence.Length; i++)
        {
            if (keySequence[i] is < SeVirtualKey.PAD_UP or > SeVirtualKey.PAD_Start)
                throw new ArgumentException($"{keySequence[i]} is not allowed.");
            res.KeySequenceBuffer[i] = (byte)keySequence[i];
        }

        return res;
    }

    public readonly bool Equals(Keybind other) => this.Value32 == other.Value32;

    public override readonly bool Equals(object? obj) => obj is Keybind other && this.Equals(other);

    public override readonly int GetHashCode() => this.Value32;

    public override readonly string ToString() =>
        this.KeySequenceBuffer[0] == 0
            ? "<empty>"
            : this.IsKeybindKeyboard || this.IsKeybindMouse
                ? DescribeKeySequence(this.KeyboardModifiers, this.KeySequence)
                : DescribeKeySequence(this.GamepadModifiers, this.KeySequence);

    private static string DescribeKeySequence(GamepadModifierFlags flags, ReadOnlySpan<SeVirtualKey> keys)
    {
        var sb = new StringBuilder();
        if ((flags & GamepadModifierFlags.LeftTrigger) != 0)
            sb.Append("LT");
        if ((flags & GamepadModifierFlags.RightTrigger) != 0)
            sb.Append(sb.Length == 0 ? "" : "+").Append("RT");
        if ((flags & GamepadModifierFlags.LeftBumper) != 0)
            sb.Append(sb.Length == 0 ? "" : "+").Append("LB");
        if ((flags & GamepadModifierFlags.RightBumper) != 0)
            sb.Append(sb.Length == 0 ? "" : "+").Append("RB");
        if ((flags & GamepadModifierFlags.Select) != 0)
            sb.Append(sb.Length == 0 ? "" : "+").Append("Select");
        if ((flags & GamepadModifierFlags.Start) != 0)
            sb.Append(sb.Length == 0 ? "" : "+").Append("Start");
        if (sb.Length != 0)
            sb.Append('+');

        for (var i = 0; i < keys.Length; i++)
        {
            if (i != 0)
                sb.Append(", ");

            var name = Enum.GetName(keys[i]);
            sb.Append(name);
        }

        return sb.ToString();
    }

    private static string DescribeKeySequence(KeyboardModifierFlags flags, ReadOnlySpan<SeVirtualKey> keys)
    {
        var sb = new StringBuilder();
        if ((flags & KeyboardModifierFlags.Control) != 0)
            sb.Append("Ctrl");
        if ((flags & KeyboardModifierFlags.Alt) != 0)
            sb.Append(sb.Length == 0 ? "" : "+").Append("Alt");
        if ((flags & KeyboardModifierFlags.Shift) != 0)
            sb.Append(sb.Length == 0 ? "" : "+").Append("Shift");
        if (sb.Length != 0)
            sb.Append('+');

        for (var i = 0; i < keys.Length; i++)
        {
            if (i != 0)
                sb.Append(", ");

            var name = Enum.GetName(keys[i]);
            sb.Append(name);
        }

        return sb.ToString();
    }
}
