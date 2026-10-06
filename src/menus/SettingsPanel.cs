using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The settings screen: every setting and every key as a row, the picked one explained on its
/// page with the buttons that change it. A change is applied and written at once. Change key
/// waits for the next key pressed; a key another action had moves over and the page says so.
/// </summary>
public sealed class SettingsPanel
{
    public static readonly string[] LightingWords = { "As the map says", "Off", "Mood", "Rules" };
    private static readonly string[] TimeWords = { "As the map says", "Day", "Dusk", "Night" };

    // Escape, Enter and the digits always do the same thing (back, confirm, replies and hotbar slots)
    private static readonly Key[] Fixed =
    {
        Key.Escape, Key.Enter, Key.KpEnter, Key.Key0, Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5, Key.Key6, Key.Key7, Key.Key8, Key.Key9,
        Key.Shift, Key.Ctrl, Key.Alt, Key.Meta,
    };

    private static readonly DataColumn[] Columns = { new("Setting", 200), new("Group", 70), new("Now", 150) };

    private sealed record Setting(string Id, string Name, string Group, string Description, Func<GameSettings, string> Value, string[] Options,
        Action<GameSettings, int> Set);

    private static readonly Setting[] Settings =
    {
        new("zoomToCursor", "Zoom toward the pointer", "Camera", "The wheel zooms in on what the pointer is over. Off zooms on the middle of the screen.",
            s => OnOff(s.ZoomToCursor), new[] { "On", "Off" }, (s, i) => s.ZoomToCursor = i == 0),
        new("edgeScroll", "Pan at the screen edge", "Camera", "The view moves while the pointer rests on an edge of the window.",
            s => OnOff(s.EdgeScroll), new[] { "On", "Off" }, (s, i) => s.EdgeScroll = i == 0),
        new("cameraFollows", "Follow who is moving", "Camera", "The view goes with the selected hero as they walk, and with whoever acts in a fight. Off leaves it where you put it.",
            s => OnOff(s.CameraFollows), new[] { "On", "Off" }, (s, i) => s.CameraFollows = i == 0),
        new("panSpeed", "Pan speed", "Camera", $"How fast the keys and the screen edge move the view, from {GameSettings.PanSpeedMin:0} to {GameSettings.PanSpeedMax:0}.",
            s => s.PanSpeed.ToString("0"), new[] { "Slower", "Faster" }, (s, i) => s.StepPanSpeed(i == 0 ? -1 : 1)),
        new("fullscreen", "Fullscreen", "Display", "The game fills the screen. Off puts it in a window.",
            s => OnOff(s.Fullscreen), new[] { "On", "Off" }, (s, i) => s.Fullscreen = i == 0),
        new("lighting", "Lighting", "Display", "Off draws every map fully lit. Mood darkens it for the look only. Rules also makes darkness hide things. As the map says leaves it to each chapter.",
            s => LightingWords[Math.Clamp(s.Lighting, 0, 3)], LightingWords, (s, i) => s.Lighting = i),
        new("timeOfDay", "Time of day", "Display", "Outdoor maps can be played by day, at dusk or at night. Underground maps stay as they are.",
            s => TimeWords[Math.Clamp(s.TimeOfDay, 0, 3)], TimeWords, (s, i) => s.TimeOfDay = i),
        new("sharedFog", "Shared party view", "Game", "The map shows what anyone in the party sees. Off shows only what the selected hero sees.",
            s => OnOff(s.SharedFog), new[] { "On", "Off" }, (s, i) => s.SharedFog = i == 0),
        new("reactionPrompts", "Ask before a reaction", "Game", "A hero's reaction waits for you to use it or pass. Off takes it at once.",
            s => OnOff(s.ReactionPrompts), new[] { "On", "Off" }, (s, i) => s.ReactionPrompts = i == 0),
    };

    public DataPanel View { get; }
    /// <summary>Waiting for the key to bind.</summary>
    public bool Capturing => _capturing.Length > 0;

    private string _capturing = "";
    private string _said = "";
    private string _saidFor = "";

    public SettingsPanel(DataPanel view)
    {
        View = view;
        view.ActionPressed += Act;
    }

    public void Opened()
    {
        _capturing = "";
        _said = "";
        View.Reset();
    }

    public void Refresh()
    {
        GameSettings now = App.Settings;
        var defaults = new GameSettings();
        KeyBindings keys = App.Keys;
        int changed = Settings.Count(s => s.Value(now) != s.Value(defaults)) + keys.Overrides().Count;
        View.SetHead("Settings", changed == 0 ? "everything as shipped" : $"{changed} changed");
        View.SetSources(Array.Empty<(string, string)>(), "");
        View.SetTabs(new[] { "All", "Camera", "Display", "Game", "Keys" });
        View.SetChips(new[] { "Changed" });
        View.SetColumns(Columns);

        var rows = new List<DataRow>();
        foreach (Setting setting in Settings)
        {
            var tags = new HashSet<string> { setting.Group };
            if (setting.Value(now) != setting.Value(defaults))
            {
                tags.Add("Changed");
            }
            rows.Add(new DataRow
            {
                Key = "set:" + setting.Id,
                Cells = new[] { setting.Name, setting.Group, setting.Value(now) },
                Sort = new IComparable?[] { rows.Count }, // as listed, group by group, until a header is clicked
                Tags = tags,
                Search = setting.Description,
            });
        }
        foreach (KeyAction action in keys.Actions)
        {
            var tags = new HashSet<string> { "Keys" };
            if (keys.Changed(action.Id))
            {
                tags.Add("Changed");
            }
            rows.Add(new DataRow
            {
                Key = "key:" + action.Id,
                Cells = new[] { action.Name, "Key", keys.KeysText(action.Id) },
                Sort = new IComparable?[] { rows.Count },
                Tags = tags,
                Search = action.Description + " " + action.Group,
                Dim = keys.Keys(action.Id).Count == 0,
            });
        }
        View.SetRows(rows);

        string picked = View.Picked;
        if (_saidFor != picked)
        {
            _said = "";
            _saidFor = picked;
            _capturing = "";
        }
        if (picked.StartsWith("set:", StringComparison.Ordinal) && Array.Find(Settings, s => "set:" + s.Id == picked) is Setting found)
        {
            string value = found.Value(now);
            var page = new BookPage().Title(found.Name).Sub(found.Group.ToLowerInvariant()).Rule();
            page.Stats(("Now", value), ("Shipped", found.Value(defaults)));
            page.Gap().Text(found.Description);
            var actions = new List<DataAction>();
            for (int i = 0; i < found.Options.Length; i++)
            {
                actions.Add(new DataAction($"option:{i}", found.Options[i], found.Options[i] != value, "That is how it is now."));
            }
            View.SetEntry(page.ToString(), actions, _said);
        }
        else if (picked.StartsWith("key:", StringComparison.Ordinal) && keys.Action(picked[4..]) is KeyAction action)
        {
            var page = new BookPage().Title(action.Name).Sub($"key, {action.Group.ToLowerInvariant()}").Rule();
            page.Stats(("Now", keys.KeysText(action.Id)), ("Shipped", action.Defaults.Count > 0 ? string.Join(", ", action.Defaults) : "none"));
            page.Gap().Text(action.Description);
            if (Capturing)
            {
                page.Gap().Entry("Waiting", $"press the key for {action.Name}. Escape leaves it as it is.");
            }
            page.Gap().Note("Escape, Enter and the number keys are fixed.");
            var actions = new List<DataAction>
            {
                Capturing ? new DataAction("cancel", "Cancel") : new DataAction("change", "Change key"),
                new("default", "Shipped keys", keys.Changed(action.Id), "It has its shipped keys."),
            };
            View.SetEntry(page.ToString(), actions, _said);
        }
        else
        {
            View.SetEntry(new BookPage().Note("Nothing matches.").ToString(), Array.Empty<DataAction>(), "");
        }
        View.SetFoot("Changes are kept as they are made. " + Places.SettingsFile());
    }

    /// <summary>The key pressed while Change key was waiting.</summary>
    public void Captured(Key key)
    {
        string id = _capturing;
        if (key == Key.Escape)
        {
            _capturing = "";
            return;
        }
        if (key is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta)
        {
            return; // on its way to something else; keep waiting
        }
        _capturing = "";
        if (Array.IndexOf(Fixed, key) >= 0)
        {
            _said = $"{OS.GetKeycodeString(key)} is fixed and can't be given to anything.";
            return;
        }
        string name = OS.GetKeycodeString(key);
        if (name.Length == 0 || !App.Keys.Bind(id, name, out string? taken))
        {
            _said = "That key can't be used.";
            return;
        }
        _said = taken != null && App.Keys.Action(taken) is KeyAction loser
            ? $"{name} was the key for {loser.Name}, which has {App.Keys.KeysText(taken)} now."
            : "";
        App.Save();
    }

    private void Act(string id)
    {
        string picked = View.Picked;
        _said = "";
        if (id.StartsWith("option:", StringComparison.Ordinal) && Array.Find(Settings, s => "set:" + s.Id == picked) is Setting setting)
        {
            setting.Set(App.Settings, int.Parse(id[7..]));
            App.Save();
            return;
        }
        if (!picked.StartsWith("key:", StringComparison.Ordinal))
        {
            return;
        }
        switch (id)
        {
            case "change":
                _capturing = picked[4..];
                break;
            case "cancel":
                _capturing = "";
                break;
            case "default":
                App.Keys.Reset(picked[4..]);
                if (App.Keys.Changed(picked[4..]))
                {
                    _said = "Some of its shipped keys belong to another action now.";
                }
                App.Save();
                break;
        }
    }

    private static string OnOff(bool on) => on ? "On" : "Off";
}
