using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Collections.Generic;
using System.Numerics;
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
        int defaultPriority,
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
    private delegate int CheckLongerKeybindExists(InputData* thisPtr, SeVirtualKey key, byte mod);

    private readonly Hook<CheckLongerKeybindExists> hkLongerKeybindExists;
    private readonly Hook<InputData.Delegates.IsInputIdPressed> hkPressed;
    private readonly Hook<InputData.Delegates.IsInputIdDown> hkDown;
    private readonly Hook<InputData.Delegates.IsInputIdHeld> hkHeld;
    private readonly Hook<InputData.Delegates.IsInputIdReleased> hkReleased;

    private readonly LongestKeySequence[] longestKeybinds = new LongestKeySequence[160];
    private bool needsLongestKeybindUpdate;

    private record struct LongestKeySequence(
        int Priority = 0,
        InputId InputId = InputId.NotFound,
        IKeybindCommand? Command = null,
        int KeybindIndex = -1,
        byte ModLen = 0,
        byte SequenceIndex = 0)
    {
        public static LongestKeySequence FromDalamud(IKeybindCommand command, int keybindIndex, byte modLen, byte sequenceIndex) => new(
            Priority: command.Priority,
            Command: command,
            KeybindIndex: keybindIndex,
            ModLen: modLen,
            SequenceIndex: sequenceIndex);
        
        public static LongestKeySequence FromGame(InputId inputId, byte modLen) => new(
            Priority: 0,
            InputId: inputId,
            ModLen: modLen,
            SequenceIndex: 1);
    }

    public KeybindCommandManager()
    {
        this.hkLongerKeybindExists = Plugin.InteropProvider.HookFromAddress<CheckLongerKeybindExists>(
            Plugin.SigScanner.ScanText("e8 ?? ?? ?? ?? 83 f8 ff e9"),
            this.CheckLongerKeybindExistsDetour);
        this.hkPressed = Plugin.InteropProvider.HookFromAddress<InputData.Delegates.IsInputIdPressed>(
            InputData.Addresses.IsInputIdPressed.Value,
            this.IsInputIdPressedDetour);
        this.hkDown = Plugin.InteropProvider.HookFromAddress<InputData.Delegates.IsInputIdDown>(
            InputData.Addresses.IsInputIdPressed.Value,
            this.IsInputIdDownDetour);
        this.hkHeld = Plugin.InteropProvider.HookFromAddress<InputData.Delegates.IsInputIdHeld>(
            InputData.Addresses.IsInputIdPressed.Value,
            this.IsInputIdHeldDetour);
        this.hkReleased = Plugin.InteropProvider.HookFromAddress<InputData.Delegates.IsInputIdReleased>(
            InputData.Addresses.IsInputIdPressed.Value,
            this.IsInputIdReleasedDetour);
        this.hkLongerKeybindExists.Enable();
        this.hkPressed.Enable();
        this.hkDown.Enable();
        this.hkHeld.Enable();
        this.hkReleased.Enable();
        Plugin.Framework.Update += this.FrameworkOnUpdate;
    }

    public List<KeybindCommand> Commands { get; } = [];

    private void FrameworkOnUpdate(IFramework framework)
    {
        var inputData = UIInputData.Instance();
        this.needsLongestKeybindUpdate = true;
    }

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
        int defaultPriority,
        params ReadOnlySpan<Keybind> defaultKeybinds)
    {
        var command = new KeybindCommand
        {
            PersistentId = persistentId,
            DisplayName = displayName,
            Description = description,
            DefaultPriority = defaultPriority,
        };
        var pos = this.Commands.BinarySearch(command, KeybindComparer.Instance);
        if (pos >= 0)
        {
            command = this.Commands[pos];
            command.DisplayName = displayName;
            command.Description = description;
            command.DefaultPriority = defaultPriority;
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

    private void UpdateKeybindStates()
    {
        var inputData = UIInputData.Instance();
        if (!this.needsLongestKeybindUpdate)
            return;
        this.needsLongestKeybindUpdate = false;

        var now = Environment.TickCount64;
        this.longestKeybinds.AsSpan().Clear();

        var numNewlyPressedKeys = 0;
        foreach (var keyState in inputData->KeyboardInputs.KeyState)
        {
            if ((keyState & KeyStateFlags.Pressed) != 0)
                numNewlyPressedKeys++;
        }

        foreach (var command in this.Commands)
        {
            if (command.KeybindNextSequenceIndices.Length != command.Keybinds.Count)
                command.KeybindNextSequenceIndices = new byte[command.Keybinds.Count];

            for (var i = 0; i < command.Keybinds.Count; i++)
            {
                var keybind = command.Keybinds[i];
                if (keybind.IsKeybindKeyboard)
                {
                    var modifierActive =
                        ((KeyboardModifierFlags)inputData->CurrentKeyModifier & keybind.KeyboardModifiers) ==
                        keybind.KeyboardModifiers;
                    if (!modifierActive)
                    {
                        command.KeybindNextSequenceIndices[i] = 0;
                        continue;
                    }

                    ref var nextIndex = ref command.KeybindNextSequenceIndices[i];
                    var sequence = keybind.KeySequence;
                    var nextKey = (int)sequence[Math.Min(nextIndex, sequence.Length - 1)];
                    if (nextIndex < sequence.Length)
                    {
                        var keyState = inputData->KeyboardInputs.KeyState[nextKey];
                        switch (nextIndex) {
                            case 0 when (keyState & KeyStateFlags.Down) != 0:
                            case > 0 when (keyState & KeyStateFlags.Pressed) != 0:
                                nextIndex++;
                                break;
                            case > 0 when numNewlyPressedKeys >= 1:
                                nextIndex = 0;
                                break;
                        }
                    }
                    else if ((inputData->KeyboardInputs.KeyState[nextKey] & KeyStateFlags.Down) == 0)
                    {
                        nextIndex = 0;
                    }

                    if (nextIndex > 0)
                    {
                        var lastKey = (int)sequence[nextIndex - 1];
                        var newModLen = (byte)BitOperations.PopCount((uint)keybind.KeyboardModifiers);
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
        }

        var span = inputData->GetKeybindSpan();
        for (var index = 0; index < span.Length; index++)
        {
            var keybind = span[index];
            foreach (var keys in keybind.KeySettings)
            {
                if (keys.Key is not (> SeVirtualKey.NO_KEY and < SeVirtualKey.PAD_LMB))
                    continue;
                if ((inputData->CurrentKeyModifier & keys.KeyModifier) != keys.KeyModifier)
                    continue;
                var newModLen = (byte)BitOperations.PopCount((uint)keys.KeyModifier);
                var nextKey = (int)keys.Key;
                if ((inputData->KeyboardInputs.KeyState[nextKey] & KeyStateFlags.Down) == 0)
                    continue;
                if (this.longestKeybinds[nextKey].ModLen <= newModLen
                    && this.longestKeybinds[nextKey].SequenceIndex <= 1
                    && this.longestKeybinds[nextKey].Priority <= 0)
                {
                    if ((SeVirtualKey)nextKey != SeVirtualKey.CONTROL)
                        ((Action)(()=>{ }))();
                    this.longestKeybinds[nextKey] = LongestKeySequence.FromGame((InputId)index, newModLen);
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
                if (keybind.IsKeybindKeyboard)
                {
                    if (command.KeybindNextSequenceIndices[i] != sequence.Length)
                        continue;
                    var lkb = this.longestKeybinds[(int)sequence[^1]];
                    if (lkb.Command != command || lkb.KeybindIndex != i)
                        continue;
                }
                else
                {
                    continue;
                }

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

    private int CheckLongerKeybindExistsDetour(InputData* thisPtr, SeVirtualKey key, byte mod)
    {
        if (key is > SeVirtualKey.NO_KEY and < SeVirtualKey.PAD_LMB)
        {
            var lkb = this.longestKeybinds[(int)key];
            if (lkb.SequenceIndex > 1)
                return 1;
            if (lkb.Priority > 0)
                return 1;
            if (lkb.ModLen > BitOperations.PopCount(mod))
                return 1;
            return -1;
        }

        var orig = this.hkLongerKeybindExists.Original(thisPtr, key, mod);
        return orig;
    }

    private bool IsInputIdPressedDetour(InputData* thisPtr, InputId inputId)
    {
        this.UpdateKeybindStates();

        var orig = this.hkPressed.Original(thisPtr, inputId);
        return orig;
    }

    private bool IsInputIdDownDetour(InputData* thisPtr, InputId inputId)
    {
        this.UpdateKeybindStates();

        var orig = this.hkDown.Original(thisPtr, inputId);
        return orig;
    }

    private bool IsInputIdHeldDetour(InputData* thisPtr, InputId inputId)
    {
        this.UpdateKeybindStates();

        var orig = this.hkHeld.Original(thisPtr, inputId);
        return orig;
    }

    private bool IsInputIdReleasedDetour(InputData* thisPtr, InputId inputId)
    {
        this.UpdateKeybindStates();

        var orig = this.hkReleased.Original(thisPtr, inputId);
        return orig;
    }

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
}
