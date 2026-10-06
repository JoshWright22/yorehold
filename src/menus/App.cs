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

    public static ContentFiles Content() => new(Places.GameContent());

    /// <summary>The account server. Off unless the settings or YOREHOLD_SERVER name one; playing never waits on it.</summary>
    public static Online Online { get; } = new(new HttpTransport());
    /// <summary>Keeps the saves and characters the same as the account's copies.</summary>
    public static AccountSync Sync { get; } = new(Online);

    private static string _connected = "\u0000"; // the address and key Online was last given
    private static double _configTimer;

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
        ConnectOnline(false);
    }

    /// <summary>
    /// Signs in again with what the settings say now. The server comes from YOREHOLD_SERVER and
    /// YOREHOLD_SERVER_KEY when they are set (the dev script sets them for a local server), else
    /// from the settings. Nothing happens when neither changed, unless asked again.
    /// </summary>
    public static void ConnectOnline(bool again)
    {
        string address = System.Environment.GetEnvironmentVariable("YOREHOLD_SERVER") ?? Settings.Server;
        string key = System.Environment.GetEnvironmentVariable("YOREHOLD_SERVER_KEY") ?? Settings.ServerKey;
        if (!again && address + "\n" + key == _connected)
        {
            return;
        }
        _connected = address + "\n" + key;
        if (address.Length == 0)
        {
            Online.Connect("", "", "");
            Sync.SetFolders(null);
            return;
        }
        string device;
        if (System.Environment.GetEnvironmentVariable("YOREHOLD_DEVICE") is string fromEnvironment)
        {
            device = fromEnvironment;
        }
        else if (ShotRunner.Running)
        {
            device = "yorehold-test-run";
        }
        else
        {
            if (Settings.DeviceId.Length < 10)
            {
                Settings.DeviceId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                Save();
            }
            device = Settings.DeviceId;
        }
        Online.Connect(address, key, device);
        _configTimer = 60;
        // screenshot runs never touch the player's files, so they have nothing to sync
        Sync.SetFolders(ShotRunner.Running ? null : Places.SyncFolders());
    }

    /// <summary>Call once per frame: answers from the server, the sync pass and the config every minute.</summary>
    public static void UpdateOnline(double delta)
    {
        Online.Update();
        // the server's settings are read at sign-in and again every minute, so a change there reaches a running game
        _configTimer -= delta;
        if (_configTimer <= 0 && Online.State == OnlineState.SignedIn)
        {
            _configTimer = 60;
            Online.RefreshConfig();
        }
        int passes = Sync.Passes;
        Sync.Update(delta);
        if (Sync.Passes != passes)
        {
            foreach (string problem in Sync.Problems)
            {
                GD.PushWarning("Account sync: " + problem);
            }
        }
    }

    /// <summary>The game wrote or removed a save or a character: send it at the next chance.</summary>
    public static void FilesWritten() => Sync.Request();

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
        ConnectOnline(false);
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
