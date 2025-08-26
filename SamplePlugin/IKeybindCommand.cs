using System;
using System.Collections.Generic;

namespace SamplePlugin;

public interface IKeybindCommand
{
    // Called every frame if held down
    event KeybindTriggerEventDelegate? Down;

    // One-time conditions are evaluated per command
    // (press keybind 1) (press keybind 2) (release keybind 1) (release keybind 2) will only invoke the first and the last event
    event KeybindTriggerEventDelegate? Press;
    event KeybindTriggerEventDelegate? Release;

    // If multiple keybinds are held at the same time they will be treated as one keybind where it is kept held down as long as any of the keybinds are held down
    // Repeat rate is configured from Windows Control Panel > Keyboard Properties > Repeat rate
    event KeybindTriggerEventDelegate? Hold;

    // Null if owned by Dalamud itself
    IPlugin? Plugin { get; }

    Guid PersistentId { get; }

    string DisplayName { get; }
    string? Description { get; }

    // A keybind with a higher priority value takes priority
    // Non-negative: take priority over the game's default handler; make corresponding `IsInputId...` return false
    // Negative: do not handle if the game already has a corresponding keybind
    // NOTE: Longer modifier keys always have higher priority; "Ctrl+S" and "S" will only trigger "Ctrl+S", including Down event
    bool UseDefaultPriority { get; }
    int Priority { get; }
    int DefaultPriority { get; }

    bool UseDefaultKeybinds { get; }
    IEnumerable<Keybind> Keybinds { get; }
    IEnumerable<Keybind> DefaultKeybinds { get; }

    bool IsDown { get; }     // => Keybinds.Any(x => x.IsDown)
    bool IsPressed { get; }  // true for the frame where Press would be invoked
    bool IsReleased { get; } // true for the frame where Release would be invoked
    bool IsHeld { get; }     // true for the frame where Hold would be invoked
}

public class KeybindCommand : IKeybindCommand
{
    public event KeybindTriggerEventDelegate? Down;
    public event KeybindTriggerEventDelegate? Press;
    public event KeybindTriggerEventDelegate? Release;
    public event KeybindTriggerEventDelegate? Hold;

    public IPlugin? Plugin { get; init; }

    public required Guid PersistentId { get; init; }

    public required string DisplayName { get; set; }

    public string? Description { get; set; }

    public bool UseDefaultPriority => this.CustomPriority is null;

    public int Priority => this.CustomPriority ?? this.DefaultPriority;

    public int? CustomPriority { get; set; }

    public int DefaultPriority { get; set; }

    public bool UseDefaultKeybinds => this.CustomKeybinds is null;

    public List<Keybind> Keybinds => this.CustomKeybinds ?? this.DefaultKeybinds;

    public List<Keybind>? CustomKeybinds { get; set; }

    public List<Keybind> DefaultKeybinds { get; } = [];

    IEnumerable<Keybind> IKeybindCommand.Keybinds => this.Keybinds;

    IEnumerable<Keybind> IKeybindCommand.DefaultKeybinds => this.DefaultKeybinds;

    public byte[] KeybindNextSequenceIndices { get; set; } = [];

    public long DownTimestamp { get; set; }

    public Keybind DownKeybind { get; set; }

    public long NextRepeatTimestamp { get; set; }

    public bool IsDown { get; set; }

    public bool IsPressed { get; set; }

    public bool IsReleased { get; set; }

    public bool IsHeld { get; set; }

    public override string ToString() => this.DisplayName;

    public void InvokeTriggerEvents(Keybind sourceKeybind)
    {
        if (this.IsDown)
            Invoke(this.Down, this, sourceKeybind);
        if (this.IsPressed)
            Invoke(this.Press, this, sourceKeybind);
        if (this.IsReleased)
            Invoke(this.Release, this, sourceKeybind);
        if (this.IsHeld)
            Invoke(this.Hold, this, sourceKeybind);

        return;

        static void Invoke(KeybindTriggerEventDelegate? @delegates, IKeybindCommand command, Keybind keybind)
        {
            foreach (var @delegate in Delegate.EnumerateInvocationList(delegates))
            {
                try
                {
                    @delegate.Invoke(command, keybind);
                }
                catch (Exception e)
                {
                    SamplePlugin.Plugin.Log.Error(e, $"{nameof(KeybindCommand)}.{nameof(InvokeTriggerEvents)} error");
                }
            }
        }
    }
}
