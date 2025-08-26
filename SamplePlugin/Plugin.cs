using System;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace SamplePlugin;

public sealed unsafe class Plugin : IDalamudPlugin
{
    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    [PluginService]
    internal static IGameInteropProvider InteropProvider { get; private set; } = null!;

    [PluginService]
    internal static ISigScanner SigScanner { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    public readonly WindowSystem WindowSystem = new("SamplePlugin");

    private readonly KeybindCommandManager manager;

    public Plugin()
    {
        PluginInterface.UiBuilder.Draw += this.DrawUI;

        this.manager = new();
        this.WindowSystem.AddWindow(new KeybindsWindow(this.manager));

        this.RegisterEvents(
            this.manager.AddCommand(
                new("c31f9869-3b3e-4c5c-a1a8-fcbf704a6641"),
                "Test 1",
                "Test 1 Description",
                1,
                Keybind.FromKeyboard(KeyboardModifierFlags.Control | KeyboardModifierFlags.Alt, SeVirtualKey.NUMPAD2)));
        this.RegisterEvents(
            this.manager.AddCommand(
                new("c31f9869-3b3e-4c5c-a1a8-fcbf704a6642"),
                "Test 2",
                "Test 2 Description",
                1,
                Keybind.FromKeyboard(KeyboardModifierFlags.Control, SeVirtualKey.NUMPAD2)));
        this.RegisterEvents(
            this.manager.AddCommand(
                new("c31f9869-3b3e-4c5c-a1a8-fcbf704a6643"),
                "Test 3",
                "Test 3 Description",
                1,
                Keybind.FromKeyboard(KeyboardModifierFlags.Alt, SeVirtualKey.NUMPAD2)));
        this.RegisterEvents(
            this.manager.AddCommand(
                new("c31f9869-3b3e-4c5c-a1a8-fcbf704a6644"),
                "C-R, R",
                null,
                1,
                Keybind.FromKeyboard(KeyboardModifierFlags.Control, SeVirtualKey.R, SeVirtualKey.R)));
        this.RegisterEvents(
            this.manager.AddCommand(
                new("c31f9869-3b3e-4c5c-a1a8-fcbf704a6645"),
                "C-R, X",
                null,
                1,
                Keybind.FromKeyboard(KeyboardModifierFlags.Control, SeVirtualKey.R, SeVirtualKey.X)));
        this.RegisterEvents(
            this.manager.AddCommand(
                new("c31f9869-3b3e-4c5c-a1a8-fcbf704a6646"),
                "C-S-D, K",
                null,
                1,
                Keybind.FromKeyboard(KeyboardModifierFlags.Control | KeyboardModifierFlags.Shift, SeVirtualKey.D, SeVirtualKey.K)));
    }

    public void Dispose()
    {
        this.WindowSystem.RemoveAllWindows();
        this.manager.Dispose();
    }

    private void DrawUI() => WindowSystem.Draw();

    private void RegisterEvents(IKeybindCommand command)
    {
        // command.Down += this.OnDown;
        command.Press += this.OnPress;
        command.Release += this.OnRelease;
        // command.Hold += this.OnHold;
    }

    private void OnDown(IKeybindCommand command, Keybind trigger) =>
        Log.Information("Down: {c} from {kb}", command, trigger);

    private void OnPress(IKeybindCommand command, Keybind trigger) =>
        Log.Information("Press: {c} from {kb}", command, trigger);

    private void OnRelease(IKeybindCommand command, Keybind trigger) =>
        Log.Information("Release: {c} from {kb}", command, trigger);

    private void OnHold(IKeybindCommand command, Keybind trigger) =>
        Log.Information("Hold: {c} from {kb}", command, trigger);

    private class KeybindsWindow : Window
    {
        private readonly KeybindCommandManager _manager;

        public KeybindsWindow(KeybindCommandManager keybindCommandManager) : base("Keybinds##Keybinds")
        {
            this.IsOpen = true;
            this._manager = keybindCommandManager;
        }

        public override void Draw()
        {
            if (!ImGui.BeginTable("##KeybindsTable", 4, ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortMulti))
                return;
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Plugin"u8);
            ImGui.TableSetupColumn("Name"u8);
            ImGui.TableSetupColumn("Priority"u8);
            ImGui.TableSetupColumn("Keybinds"u8);
            ImGui.TableHeadersRow();
            
            var clipper = ImGui.ImGuiListClipper();
            clipper.Begin(this._manager.Commands.Count);
            while (clipper.Step())
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    ImGui.PushID($"command_{i}");
                    var command = this._manager.Commands[i];
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(command.Plugin?.ToString() ?? "(Dalamud)");

                    ImGui.TableNextColumn();
                    ImGui.Text(command.DisplayName);
                    if (command.Description is not null)
                    {
                        ImGui.SameLine();
                        ImGuiComponents.HelpMarker(command.Description);
                    }

                    ImGui.TableNextColumn();
                    ImGui.Text(command.Priority.ToString());
                    if (!command.UseDefaultPriority)
                    {
                        ImGui.SameLine();
                        if (ImGuiComponents.IconButton("##priorityRestore", FontAwesomeIcon.Undo))
                            command.CustomPriority = null;
                    }

                    ImGui.TableNextColumn();
                    for (var j = 0; j < command.Keybinds.Count; j++)
                    {
                        var keybind = command.Keybinds[j];
                        if (j != 0)
                            ImGui.SameLine();

                        ImGui.Button($"{keybind}##keybind_{j}");
                    }

                    if (command.Keybinds.Count != 0)
                        ImGui.SameLine();
                    if (ImGuiComponents.IconButton("##keybindAdd", FontAwesomeIcon.Plus))
                    {
                        // TODO
                    }
                    
                    if (!command.UseDefaultKeybinds)
                    {
                        if (ImGuiComponents.IconButton("##keybindRestore", FontAwesomeIcon.Undo))
                            command.CustomKeybinds = null;
                    }
                }
            }
            
            clipper.Destroy();
            ImGui.EndTable();
        }
    }
}
