using System;
using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// What the whole game shares outside an adventure: the settings file and the key bindings. Loaded
/// once when the game starts; the settings screen changes them here and whoever cares listens to
/// Changed.
/// </summary>
public static class App
{
    public static GameSettings Settings { get; private set; } = new();
    public static KeyBindings Keys { get; private set; } = KeyBindings.Read(ContentNode.Parse("keys", "{\"actions\": []}"));

    /// <summary>A setting or a key was changed.</summary>
    public static event Action? Changed;

    public static ContentFiles Content() => new(ProjectSettings.GlobalizePath("res://assets"));

    public static void Load()
    {
        Settings = GameSettings.Load(Places.SettingsFile());
        try
        {
            Keys = KeyBindings.Read(ContentNode.Read(Content(), "ui/keys.json"));
        }
        catch (ContentException error)
        {
            // the keys project.godot ships still pan and zoom; the panels keep their buttons
            GD.PushWarning($"Couldn't read the key bindings: {error.Message}");
        }
        Keys.Apply(Settings.Keys);
        ApplyKeys();
        ApplyWindow();
    }

    /// <summary>Writes the settings file and tells the screens. A screenshot run writes its own under ../.dev.</summary>
    public static void Save()
    {
        Settings.Keys.Clear();
        foreach (KeyValuePair<string, List<string>> changed in Keys.Overrides())
        {
            Settings.Keys[changed.Key] = changed.Value;
        }
        try
        {
            Settings.Save(Places.SettingsFile());
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Couldn't write the settings: {error.Message}");
        }
        ApplyKeys();
        ApplyWindow();
        Changed?.Invoke();
    }

    /// <summary>The event is a bound action's key going down. False for an action that has no binding at all.</summary>
    public static bool Pressed(InputEvent @event, string action)
    {
        return InputMap.HasAction(action) && @event.IsActionPressed(action);
    }

    /// <summary>"C" for a label like "Sheet (C)"; the first key when there are two, "" when there is none.</summary>
    public static string KeyHint(string action)
    {
        IReadOnlyList<string> keys = Keys.Keys(action);
        return keys.Count > 0 ? keys[0] : "";
    }

    /// <summary>"Sheet (C)", or just "Sheet" for an action with no key.</summary>
    public static string WithKey(string label, string action)
    {
        string key = KeyHint(action);
        return key.Length > 0 ? $"{label} ({key})" : label;
    }

    // The bindings become input map actions under their ids. Only an action's keys are replaced, so
    // the gamepad sticks and buttons project.godot gives the camera stay.
    private static void ApplyKeys()
    {
        foreach (KeyAction action in Keys.Actions)
        {
            if (!InputMap.HasAction(action.Id))
            {
                InputMap.AddAction(action.Id);
            }
            foreach (InputEvent bound in InputMap.ActionGetEvents(action.Id))
            {
                if (bound is InputEventKey)
                {
                    InputMap.ActionEraseEvent(action.Id, bound);
                }
            }
            foreach (string name in Keys.Keys(action.Id))
            {
                Key key = OS.FindKeycodeFromString(name);
                if (key != Key.None)
                {
                    InputMap.ActionAddEvent(action.Id, new InputEventKey { Keycode = key });
                }
            }
        }
    }

    private static void ApplyWindow()
    {
        // a screenshot run keeps its small window off screen whatever the settings say
        if (ShotRunner.Running || DisplayServer.GetName() == "headless")
        {
            return;
        }
        DisplayServer.WindowMode now = DisplayServer.WindowGetMode();
        bool full = now is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
        if (full != Settings.Fullscreen)
        {
            DisplayServer.WindowSetMode(Settings.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        }
    }
}
