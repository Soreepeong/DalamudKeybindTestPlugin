using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;

namespace SamplePlugin;

public interface IKeybindCommandManager
{
    // Adds a command bound to the caller plugin. If a command is already registered, updates the default priority and keybind.
    IKeybindCommand AddCommand(
        /* (internal) IPlugin? plugin = caller plugin, */
        Guid persistentId,
        string displayName,
        string? description,
        params ReadOnlySpan<Keybind> defaultKeybinds);

    // Removes a command and forgets all the associated keybind configuration.
    void RemoveCommand(
        /* (internal) IPlugin? plugin = caller plugin, */
        params ReadOnlySpan<Guid> persistentIds);

    // IKeybindCommand GetCommand(Guid persistentId);
    // IKeybindCommand GetCommand(IPlugin? plugin, Guid persistentId);
    // IKeybindCommand[] GetCommands(IPlugin? plugin);

    // void ShowKeybindSettings();
    // void ShowKeybindSettings(IPlugin plugin, params ReadOnlySpan<Guid> focusPersistentIds);
}

public unsafe class KeybindCommandManager : IKeybindCommandManager, IDisposable
{
    private const int GameBuiltinKeybindPriority = -1;
    
    private delegate int CheckLongerKeybindExists(InputData* thisPtr, SeVirtualKey key, byte mod);

    private readonly Hook<CheckLongerKeybindExists> hkLongerKeybindExists;
    private readonly Hook<InputData.Delegates.IsInputIdPressed> hkPressed;
    private readonly Hook<InputData.Delegates.IsInputIdDown> hkDown;
    private readonly Hook<InputData.Delegates.IsInputIdHeld> hkHeld;
    private readonly Hook<InputData.Delegates.IsInputIdReleased> hkReleased;

    private readonly LongestKeySequence[] longestKeybinds =
        new LongestKeySequence[Enum.GetValues<SeVirtualKey>().Max(x => (int)x) + 1];
    private bool needsLongestKeybindUpdate;

    public KeybindCommandManager()
    {
        this.hkLongerKeybindExists = Plugin.InteropProvider.HookFromAddress<CheckLongerKeybindExists>(
            Plugin.SigScanner.ScanText("e8 ?? ?? ?? ?? 83 f8 ff e9"),
            this.CheckLongerKeybindExistsDetour);
        this.hkPressed = Plugin.InteropProvider.HookFromAddress<InputData.Delegates.IsInputIdPressed>(
            InputData.Addresses.IsInputIdPressed.Value,
            this.IsInputIdPressedDetour);
        this.hkDown = Plugin.InteropProvider.HookFromAddress<InputData.Delegates.IsInputIdDown>(
            InputData.Addresses.IsInputIdDown.Value,
            this.IsInputIdDownDetour);
        this.hkHeld = Plugin.InteropProvider.HookFromAddress<InputData.Delegates.IsInputIdHeld>(
            InputData.Addresses.IsInputIdHeld.Value,
            this.IsInputIdHeldDetour);
        this.hkReleased = Plugin.InteropProvider.HookFromAddress<InputData.Delegates.IsInputIdReleased>(
            InputData.Addresses.IsInputIdReleased.Value,
            this.IsInputIdReleasedDetour);
        this.hkLongerKeybindExists.Enable();
        this.hkPressed.Enable();
        this.hkDown.Enable();
        this.hkHeld.Enable();
        this.hkReleased.Enable();
        Plugin.Framework.Update += this.FrameworkOnUpdate;
    }

    public List<KeybindCommand> Commands { get; } = [];

    private void FrameworkOnUpdate(IFramework framework) => this.needsLongestKeybindUpdate = true;

    public void Dispose()
    {
        Plugin.Framework.Update -= this.FrameworkOnUpdate;
        this.hkLongerKeybindExists.Dispose();
        this.hkPressed.Dispose();
        this.hkDown.Dispose();
        this.hkHeld.Dispose();
        this.hkReleased.Dispose();
    }

    public IKeybindCommand AddCommand(
        Guid persistentId,
        string displayName,
        string? description,
        params ReadOnlySpan<Keybind> defaultKeybinds)
    {
        var command = new KeybindCommand
        {
            PersistentId = persistentId,
            DisplayName = displayName,
            Description = description,
        };
        var pos = this.Commands.BinarySearch(command, KeybindComparer.Instance);
        if (pos >= 0)
        {
            command = this.Commands[pos];
            command.DisplayName = displayName;
            command.Description = description;
            command.DefaultKeybinds.Clear();
        }
        else
        {
            this.Commands.Insert(~pos, command);
        }

        command.DefaultKeybinds.AddRange(defaultKeybinds);
        return command;
    }

    public void RemoveCommand(params ReadOnlySpan<Guid> persistentIds)
    {
        throw new NotImplementedException();
    }

    private static KeyStateFlags GetKeyStateFlags(SeVirtualKey key)
    {
        var inputData = UIInputData.Instance();
        switch (key.GetInputDeviceType())
        {
            case SeVirtualKeyInputDeviceType.None:
            default:
                return KeyStateFlags.None;
            case SeVirtualKeyInputDeviceType.Keyboard:
                return inputData->KeyboardInputs.KeyState[(int)key];
            case SeVirtualKeyInputDeviceType.Mouse:
            {
                var n = (MouseButtonFlags)(1 << (key - SeVirtualKey.PAD_LMB));
                var f = KeyStateFlags.None;
                var tmp = &inputData->UIFilteredCursorInputs.MouseButtonHeldFlags;
                if ((tmp[0] & n) != 0)
                    f |= KeyStateFlags.Down;
                if ((tmp[1] & n) != 0)
                    f |= KeyStateFlags.Pressed;
                if ((tmp[3] & n) != 0)
                    f |= KeyStateFlags.Released;
                if ((tmp[4] & n) != 0)
                    f |= KeyStateFlags.Held;
                return f;
            }
            case SeVirtualKeyInputDeviceType.Gamepad:
            {
                var n = (GamepadButtonsFlags)(1 << (key - SeVirtualKey.PAD_UP));
                var f = KeyStateFlags.None;
                if ((inputData->GamepadInputs.Buttons & n) != 0)
                    f |= KeyStateFlags.Down;
                if ((inputData->GamepadInputs.ButtonsPressed & n) != 0)
                    f |= KeyStateFlags.Pressed;
                if ((inputData->GamepadInputs.ButtonsReleased & n) != 0)
                    f |= KeyStateFlags.Released;
                if ((inputData->GamepadInputs.ButtonsRepeat & n) != 0)
                    f |= KeyStateFlags.Held;
                return f;
            }
        }
    }

    private static bool ModifierMatches(Keybind keybind)
    {
        var inputData = UIInputData.Instance();
        return keybind.IsKeybindKeyboard || keybind.IsKeybindMouse
                   ? ((KeyboardModifierFlags)inputData->CurrentKeyModifier & keybind.KeyboardModifiers) ==
                     keybind.KeyboardModifiers
                   : ((GamepadModifierFlags)inputData->CurrentGamepadModifier & keybind.GamepadModifiers) ==
                     keybind.GamepadModifiers;
    }

    private static bool ModifierMatches(KeySetting keybind)
    {
        var inputData = UIInputData.Instance();
        return keybind.Key is > SeVirtualKey.NO_KEY and <= SeVirtualKey.PAD_MB7
                   ? (inputData->CurrentKeyModifier & keybind.KeyModifier) == keybind.KeyModifier
                   : (inputData->CurrentGamepadModifier & keybind.GamepadModifier) == keybind.GamepadModifier;
    }

    private void UpdateKeybindStates()
    {
        var inputData = UIInputData.Instance();
        if (!this.needsLongestKeybindUpdate)
            return;
        this.needsLongestKeybindUpdate = false;

        var now = Environment.TickCount64;
        this.longestKeybinds.AsSpan().Fill(new(InputId: InputId.NotFound));

        var numNewlyPressedKeys = 0;
        foreach (var keyState in inputData->KeyboardInputs.KeyState)
        {
            if ((keyState & KeyStateFlags.Pressed) != 0)
                numNewlyPressedKeys++;
        }

        numNewlyPressedKeys += BitOperations.PopCount((uint)inputData->CursorInputs.MouseButtonPressedFlags);
        numNewlyPressedKeys += BitOperations.PopCount((uint)inputData->GamepadInputs.ButtonsPressed);

        foreach (var command in this.Commands)
        {
            if (command.KeybindNextSequenceIndices.Length != command.Keybinds.Count)
                command.KeybindNextSequenceIndices = new byte[command.Keybinds.Count];

            for (var i = 0; i < command.Keybinds.Count; i++)
            {
                var keybind = command.Keybinds[i];
                if (!ModifierMatches(keybind))
                {
                    command.KeybindNextSequenceIndices[i] = 0;
                    continue;
                }

                ref var nextIndex = ref command.KeybindNextSequenceIndices[i];
                var sequence = keybind.KeySequence;
                var nextKey = sequence[Math.Min(nextIndex, sequence.Length - 1)];
                if (nextIndex < sequence.Length)
                {
                    var keyState = GetKeyStateFlags(nextKey);
                    switch (nextIndex)
                    {
                        case 0 when (keyState & KeyStateFlags.Down) != 0 && sequence.Length == 1:
                        case >= 0 when (keyState & KeyStateFlags.Pressed) != 0:
                            nextIndex++;
                            break;
                        case > 0 when numNewlyPressedKeys >= 1:
                            nextIndex = 0;
                            break;
                    }
                }
                else if ((GetKeyStateFlags(nextKey) & KeyStateFlags.Down) == 0)
                {
                    nextIndex = 0;
                }

                if (nextIndex > 0)
                {
                    var lastKey = (int)sequence[nextIndex - 1];
                    var newModLen = keybind.ModifierLength;
                    if (this.longestKeybinds[lastKey].ModLen <= newModLen
                        && this.longestKeybinds[lastKey].SequenceIndex <= nextIndex
                        && this.longestKeybinds[lastKey].Priority <= command.Priority)
                    {
                        this.longestKeybinds[lastKey] =
                            LongestKeySequence.FromDalamud(command, i, newModLen, nextIndex);
                    }
                }
            }
        }

        var span = inputData->GetKeybindSpan();
        for (var index = 0; index < span.Length; index++)
        {
            var keybind = span[index];
            foreach (var keys in keybind.KeySettings)
            {
                if (!ModifierMatches(keys))
                    continue;
                if ((GetKeyStateFlags(keys.Key) & KeyStateFlags.Down) == 0)
                    continue;

                var keyInt = (int)keys.Key;
                var newModLen = (byte)BitOperations.PopCount((uint)keys.KeyModifier);
                if (this.longestKeybinds[keyInt].ModLen <= newModLen
                    && this.longestKeybinds[keyInt].SequenceIndex <= 1
                    && this.longestKeybinds[keyInt].Priority <= GameBuiltinKeybindPriority)
                {
                    this.longestKeybinds[keyInt] = LongestKeySequence.FromGame((InputId)index, newModLen);
                }
            }
        }

        const int keyboardDelay = 250; // 0...3 250ms...1000ms
        const int keyboardSpeed = 33;  // 0...31 2.5/s...30/s

        foreach (var command in this.Commands)
        {
            var sourceKeybind = default(Keybind);
            for (var i = 0; i < command.Keybinds.Count; i++)
            {
                var keybind = command.Keybinds[i];
                var sequence = keybind.KeySequence;
                if (command.KeybindNextSequenceIndices[i] != sequence.Length)
                    continue;

                var lkb = this.longestKeybinds[(int)sequence[^1]];
                if (lkb.Command != command || lkb.KeybindIndex != i)
                    continue;

                sourceKeybind = keybind;
                break;
            }

            var wasActive = command.DownTimestamp != 0;
            if (!wasActive && !sourceKeybind.IsEmpty)
            {
                command.DownTimestamp = now;
                command.DownKeybind = sourceKeybind;
                command.NextRepeatTimestamp = command.DownTimestamp + keyboardDelay;
                command.IsDown = command.IsPressed = true;
                command.IsReleased = command.IsHeld = false;
            }
            else if (wasActive && sourceKeybind.IsEmpty)
            {
                sourceKeybind = command.DownKeybind;
                command.DownKeybind = default;
                command.DownTimestamp = command.NextRepeatTimestamp = 0;
                command.IsDown = command.IsPressed = command.IsHeld = false;
                command.IsReleased = true;
            }
            else if (wasActive && !sourceKeybind.IsEmpty)
            {
                command.IsDown = true;
                command.DownKeybind = sourceKeybind;
                command.IsReleased = command.IsPressed = false;
                command.IsHeld = now >= command.NextRepeatTimestamp;
                if (command.IsHeld)
                    command.NextRepeatTimestamp = now + keyboardSpeed;
            }
            else
            {
                command.IsDown = command.IsPressed = command.IsHeld = command.IsReleased = false;
            }

            command.InvokeTriggerEvents(sourceKeybind);
        }
    }

    private bool DalamudHandlesKeybind(KeySetting k, out LongestKeySequence seq) =>
        this.DalamudHandlesKeybind(k.Key, (byte)k.KeyModifier, out seq);

    private bool DalamudHandlesKeybind(SeVirtualKey key, byte mod, out LongestKeySequence seq)
    {
        if ((uint)key >= this.longestKeybinds.Length)
        {
            seq = default;
            return false;
        }

        seq = this.longestKeybinds[(int)key];
        if (seq.Command is null)
            return false;

        if (seq.Priority > GameBuiltinKeybindPriority)
            return true;
        if (seq.ModLen > BitOperations.PopCount(mod))
            return true;
        return false;
    }

    private int CheckLongerKeybindExistsDetour(InputData* thisPtr, SeVirtualKey key, byte mod) =>
        this.DalamudHandlesKeybind(key, mod, out _) ? 1 : -1;

    private bool IsInputIdInState(InputData* thisPtr, InputId inputId, KeyStateFlags desiredState)
    {
        this.UpdateKeybindStates();
        if ((uint)inputId >= thisPtr->NumKeybinds)
            return false;

        if (inputId == InputId.MOUSE_OK &&
            desiredState == KeyStateFlags.Released &&
            GetKeyStateFlags(SeVirtualKey.PAD_LMB) == KeyStateFlags.Released)
            System.Diagnostics.Debugger.Break();
        foreach (ref var bind in MemoryMarshal.CreateSpan(ref thisPtr->Keybinds[(int)inputId].KeySettings[0], 4))
        {
            if (this.DalamudHandlesKeybind(bind, out var seq))
                continue;
            if (seq.InputId != inputId && seq.InputId != InputId.NotFound)
                continue;
            if (bind.Key.GetInputDeviceType() == SeVirtualKeyInputDeviceType.Keyboard &&
                this.hkLongerKeybindExists.Original(thisPtr, bind.Key, (byte)bind.KeyModifier) != -1)
                continue;
            if ((GetKeyStateFlags(bind.Key) & desiredState) == desiredState)
                return true;
        }

        return false;
    }

    private bool IsInputIdPressedDetour(InputData* thisPtr, InputId inputId) =>
        this.IsInputIdInState(thisPtr, inputId, KeyStateFlags.Pressed);

    private bool IsInputIdDownDetour(InputData* thisPtr, InputId inputId) =>
        this.IsInputIdInState(thisPtr, inputId, KeyStateFlags.Down);

    private bool IsInputIdHeldDetour(InputData* thisPtr, InputId inputId) =>
        this.IsInputIdInState(thisPtr, inputId, KeyStateFlags.Held);

    private bool IsInputIdReleasedDetour(InputData* thisPtr, InputId inputId) =>
        this.IsInputIdInState(thisPtr, inputId, KeyStateFlags.Released);

    private class KeybindComparer : IComparer<KeybindCommand>
    {
        public static KeybindComparer Instance { get; } = new();

        private KeybindComparer() { }

        public int Compare(KeybindCommand? x, KeybindCommand? y) =>
            x is null
                ? y is null
                      ? 0
                      : -1
                : y is null
                    ? 1
                    : x.PersistentId.CompareTo(y.PersistentId);
    }

    private record struct LongestKeySequence(
        int Priority = 0,
        InputId InputId = InputId.NotFound,
        IKeybindCommand? Command = null,
        int KeybindIndex = -1,
        byte ModLen = 0,
        byte SequenceIndex = 0)
    {
        public static LongestKeySequence FromDalamud(
            IKeybindCommand command, int keybindIndex, byte modLen, byte sequenceIndex) => new(
            Priority: command.Priority,
            Command: command,
            KeybindIndex: keybindIndex,
            ModLen: modLen,
            SequenceIndex: sequenceIndex);

        public static LongestKeySequence FromGame(InputId inputId, byte modLen) => new(
            Priority: GameBuiltinKeybindPriority,
            InputId: inputId,
            ModLen: modLen,
            SequenceIndex: 1);
    }
}
