using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The settings screen, laid out like the title: the groups down the ink band on the left
/// (Display, Gameplay, Camera, Controls, Account), the picked group's settings on the right, each
/// a row with its name, one line of help and its choices right there. A search box looks through
/// every group. A change is applied and kept at once. Change key waits for the next key pressed;
/// a key another action had moves over and the screen says so.
/// </summary>
public sealed class SettingsPanel
{
    public static readonly string[] LightingWords = { "As the map says", "Off", "Mood", "Rules" };
    private static readonly string[] TimeWords = { "As the map says", "Day", "Dusk", "Night" };
    private static readonly string[] DiceWords = { "Off", "Fast", "Full" };
    private static readonly string[] Groups = { "Display", "Gameplay", "Content", "Camera", "Controls", "Account" };

    // Escape, Enter and the digits always do the same thing (back, confirm, replies and hotbar slots)
    private static readonly Key[] Fixed =
    {
        Key.Escape, Key.Enter, Key.KpEnter, Key.Key0, Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5, Key.Key6, Key.Key7, Key.Key8, Key.Key9,
        Key.Shift, Key.Ctrl, Key.Alt, Key.Meta,
    };

    private sealed record Setting(string Id, string Name, string Group, string Help, Func<GameSettings, string> Value, string[] Options,
        Action<GameSettings, int> Set);

    private static readonly Setting[] Settings =
    {
        new("fullscreen", "Fullscreen", "Display", "The game fills the screen, or sits in a window.",
            s => OnOff(s.Fullscreen), new[] { "On", "Off" }, (s, i) => s.Fullscreen = i == 0),
        new("lighting", "Lighting", "Display", "Off: every map fully lit. Mood: darkness for the look. Rules: darkness hides things too.",
            s => LightingWords[Math.Clamp(s.Lighting, 0, 3)], LightingWords, (s, i) => s.Lighting = i),
        new("skin", "Skin", "Display", "A look from the skins folder: its colours, faces and frames. Takes hold when the game starts again.",
            s => s.Skin.Length == 0 ? "Game's own" : s.Skin, new[] { "Game's own", "Next skin" },
            (s, i) => s.Skin = i == 0 ? "" : NextSkin(s.Skin)),
        new("dice", "Dice", "Display", "Rolls thrown as dice over the screen before the numbers reach the log: off, fast or full.",
            s => DiceWords[Math.Clamp(s.Dice, 0, 2)], DiceWords, (s, i) => s.Dice = i),
        new("timeOfDay", "Time of day", "Display", "Outdoor maps by day, at dusk or at night. Underground maps stay as they are.",
            s => TimeWords[Math.Clamp(s.TimeOfDay, 0, 3)], TimeWords, (s, i) => s.TimeOfDay = i),
        new("sharedFog", "Shared party view", "Gameplay", "The map shows what anyone in the party sees, or only the selected hero.",
            s => OnOff(s.SharedFog), new[] { "On", "Off" }, (s, i) => s.SharedFog = i == 0),
        new("reactionPrompts", "Ask before a reaction", "Gameplay", "A hero's reaction waits for you to use it or pass, or is taken at once.",
            s => OnOff(s.ReactionPrompts), new[] { "On", "Off" }, (s, i) => s.ReactionPrompts = i == 0),
        new("cameraFollows", "Follow who is moving", "Camera", "The view goes with the walking hero and with whoever acts in a fight.",
            s => OnOff(s.CameraFollows), new[] { "On", "Off" }, (s, i) => s.CameraFollows = i == 0),
        new("zoomToCursor", "Zoom toward the pointer", "Camera", "The wheel zooms in on what the pointer is over, or on the middle.",
            s => OnOff(s.ZoomToCursor), new[] { "On", "Off" }, (s, i) => s.ZoomToCursor = i == 0),
        new("edgeScroll", "Pan at the screen edge", "Camera", "The view moves while the pointer rests on an edge of the window.",
            s => OnOff(s.EdgeScroll), new[] { "On", "Off" }, (s, i) => s.EdgeScroll = i == 0),
        new("panSpeed", "Pan speed", "Camera", "How fast the keys and the screen edge move the view.",
            s => s.PanSpeed.ToString("0"), new[] { "Slower", "Faster" }, (s, i) => s.StepPanSpeed(i == 0 ? -1 : 1)),
        new("server", "Account server", "Account",
            "Keeps your characters and saves on a server too, so another install on the same account has them. Paste takes an address from the clipboard.",
            s => s.Server.Length == 0 ? "Off" : s.Server == LocalServer ? "This computer" : "Set", new[] { "Off", "This computer", "Paste address" },
            (s, i) => s.Server = i == 0 ? "" : i == 1 ? LocalServer : s.Server),
        new("serverKey", "Server key", "Account", "The key the server was started with, so this game can sign in. Paste takes it from the clipboard; it is never shown.",
            s => s.ServerKey.Length == 0 ? "None" : "Set", new[] { "Paste key", "Clear" }, (s, i) => s.ServerKey = i == 1 ? "" : s.ServerKey),
    };

    private const string LocalServer = "http://127.0.0.1:7350";

    // A set turned on or off joins games from the next one loaded.
    private void TurnSet(string id, bool on)
    {
        if (on)
        {
            App.Settings.SetsOff.Remove(id);
        }
        else
        {
            App.Settings.SetsOff.Add(id);
        }
        App.Save();
        _said.Text = "Takes hold from the next adventure loaded.";
    }

    private static string Capital(string text) => text.Length == 0 ? "Content" : char.ToUpperInvariant(text[0]) + text[1..];

    // A system's own name for its id, from its ruleset, when the game has it
    private static string RulesName(string system)
    {
        ContentFiles game = App.Content();
        foreach (string folder in game.Folders("rulesets"))
        {
            (string id, string name) = RulesFolder.SystemAt(game, "", folder);
            if (id == system)
            {
                return name;
            }
        }
        return system;
    }

    // the installed skin after this one, round again to the first; none installed keeps the game's own
    private static string NextSkin(string now)
    {
        List<string> skins = Places.SkinNames();
        if (skins.Count == 0)
        {
            return "";
        }
        int at = skins.IndexOf(now);
        return skins[(at + 1) % skins.Count];
    }

    /// <summary>One word for how the account stands, for the title's settings page.</summary>
    public static string AccountWord()
    {
        return App.Online.State switch
        {
            OnlineState.SignedIn => "signed in",
            OnlineState.Connecting => "connecting",
            OnlineState.Failed => "offline",
            _ => "off",
        };
    }

    /// <summary>Back was pressed.</summary>
    public event Action? Closed;

    public Control View { get; }
    /// <summary>Waiting for the key to bind.</summary>
    public bool Capturing => _capturing.Length > 0;

    private readonly VBoxContainer _groups;
    private readonly VBoxContainer _rows;
    private readonly LineEdit _search;
    private readonly Label _said;
    private readonly Label _summary;
    private readonly List<Button> _groupButtons = new();
    private string _group = Groups[0];
    private string _capturing = "";
    private string _shown = "";

    public SettingsPanel(Control view)
    {
        View = view;
        ScreenSizes sizes = GameScreen.Sizes;
        var band = new ColorRect { Color = Palette.Ink, MouseFilter = Control.MouseFilterEnum.Ignore };
        view.AddChild(band);
        band.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.LeftWide);
        band.OffsetRight = sizes.BandWidth;
        var edge = new ColorRect { Color = Palette.Iron, MouseFilter = Control.MouseFilterEnum.Ignore };
        view.AddChild(edge);
        edge.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.LeftWide);
        edge.OffsetLeft = sizes.BandWidth;
        edge.OffsetRight = sizes.BandWidth + 1;

        var heading = new Label { Text = "SETTINGS", ThemeTypeVariation = "TitleLabel", Position = new Vector2(sizes.Margin - 2, 44) };
        heading.AddThemeFontSizeOverride("font_size", sizes.HeadingFont);
        heading.AddThemeColorOverride("font_color", Palette.Bone);
        view.AddChild(heading);
        _summary = new Label { ThemeTypeVariation = "DimLabel", Position = new Vector2(sizes.Margin + 2, 126) };
        view.AddChild(_summary);

        _groups = new VBoxContainer { Position = new Vector2(sizes.Margin - 8, 182), Size = new Vector2(sizes.BandWidth - 2 * sizes.Margin + 16, 400) };
        _groups.AddThemeConstantOverride("separation", (int)sizes.Gap);
        view.AddChild(_groups);
        foreach (string group in Groups)
        {
            Button button = GameScreen.BigButton(group);
            button.Pressed += () =>
            {
                _group = group;
                _search.Text = "";
                _shown = "";
            };
            _groups.AddChild(button);
            _groupButtons.Add(button);
        }
        Button back = GameScreen.BigButton("Back", "Esc");
        back.AnchorTop = back.AnchorBottom = 1;
        back.Position = new Vector2(sizes.Margin - 8, 0);
        back.Pressed += () => Closed?.Invoke();
        view.AddChild(back);
        back.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        back.OffsetLeft = sizes.Margin - 8;
        back.OffsetRight = sizes.BandWidth - sizes.Margin + 8;
        back.OffsetTop = -sizes.Margin - sizes.ButtonHeight;
        back.OffsetBottom = -sizes.Margin;

        _search = new LineEdit { PlaceholderText = "Search every setting", ClearButtonEnabled = true };
        view.AddChild(_search);
        _search.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        _search.OffsetLeft = sizes.BandWidth + sizes.Margin;
        _search.OffsetRight = -sizes.Margin;
        _search.OffsetTop = 52;
        _search.OffsetBottom = 84;
        _search.TextChanged += _ => _shown = "";

        _said = new Label { ThemeTypeVariation = "WarnLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        view.AddChild(_said);
        _said.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        _said.OffsetLeft = sizes.BandWidth + sizes.Margin;
        _said.OffsetRight = -sizes.Margin;
        _said.OffsetTop = 92;
        _said.OffsetBottom = 120;

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        view.AddChild(scroll);
        scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        scroll.OffsetLeft = sizes.BandWidth + sizes.Margin;
        scroll.OffsetRight = -sizes.Margin;
        scroll.OffsetTop = 126;
        scroll.OffsetBottom = -sizes.Margin;
        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(_rows);
    }

    public void Opened()
    {
        _capturing = "";
        _said.Text = "";
        _search.Text = "";
        _shown = "";
    }

    public void Refresh()
    {
        GameSettings now = App.Settings;
        var defaults = new GameSettings();
        KeyBindings keys = App.Keys;
        int changed = Settings.Count(s => s.Value(now) != s.Value(defaults)) + keys.Overrides().Count;
        _summary.Text = changed == 0 ? "Everything is at its default." : $"{changed} changed. Changes are kept as they are made.";
        bool searching = _search.Text.Trim().Length > 0;
        for (int i = 0; i < Groups.Length; i++)
        {
            _groupButtons[i].SetPressedNoSignal(!searching && Groups[i] == _group);
        }

        // made again only when what they show changes, so a press isn't lost to a rebuild
        string signature = $"{_group}|{_search.Text}|{_capturing}|{AccountWord()}|{App.Online.Status}|"
            + string.Join("|", Settings.Select(s => s.Value(now))) + "|" + string.Join("|", keys.Actions.Select(a => keys.KeysText(a.Id)))
            + "|" + string.Join(",", now.SetsOff) + "|" + string.Join(",", Places.SetFolders());
        if (signature == _shown)
        {
            return;
        }
        _shown = signature;
        foreach (Node old in _rows.GetChildren())
        {
            _rows.RemoveChild(old);
            old.QueueFree();
        }
        string words = _search.Text.Trim();
        bool Wanted(string group, string text) => words.Length > 0 ? text.Contains(words, StringComparison.OrdinalIgnoreCase) : group == _group;

        foreach (Setting setting in Settings.Where(s => Wanted(s.Group, s.Name + " " + s.Help + " " + s.Group)))
        {
            string value = setting.Value(now);
            HBoxContainer choices = Row(setting.Name, setting.Help, value != setting.Value(defaults));
            if (setting.Id == "panSpeed")
            {
                Choice(choices, "-", false, () => Pick(setting, 0));
                choices.AddChild(new Label { Text = value, CustomMinimumSize = new Vector2(56, 0), HorizontalAlignment = HorizontalAlignment.Center });
                Choice(choices, "+", false, () => Pick(setting, 1));
                continue;
            }
            for (int i = 0; i < setting.Options.Length; i++)
            {
                int option = i;
                bool paste = setting.Options[i].StartsWith("Paste", StringComparison.Ordinal);
                Choice(choices, setting.Options[i], !paste && setting.Options[i] == value, () => Pick(setting, option));
            }
            if (setting.Id == "server" && now.Server.Length > 0 && now.Server != LocalServer)
            {
                choices.AddChild(new Label { Text = now.Server, ThemeTypeVariation = "DimLabel" });
            }
        }
        // the content sets in the user folder's sets\, each on or off for the games of its system
        List<ContentSets.Installed> sets = ContentSets.List(Places.SetFolders());
        if (sets.Count == 0 && Wanted("Content", "content sets"))
        {
            Row("Content sets", "None installed. A set (more creatures, spells or feats for one system) goes in the sets folder of the user folder.", false);
        }
        foreach (ContentSets.Installed set in sets.Where(s => Wanted("Content", s.Name + " " + s.Kind + " " + s.System + " content sets")))
        {
            string about = set.Problem.Length > 0 ? $"Can't be used: {set.Problem}" : $"{Capital(set.Kind)} for {RulesName(set.System)} games.";
            HBoxContainer row = Row(set.Name, about, now.SetsOff.Contains(set.Id));
            bool off = now.SetsOff.Contains(set.Id);
            Choice(row, "On", !off, () => TurnSet(set.Id, true), set.Problem.Length == 0);
            Choice(row, "Off", off, () => TurnSet(set.Id, false), set.Problem.Length == 0);
        }
        if (Wanted("Account", "sign-in sign in account sync online " + App.Online.Status))
        {
            HBoxContainer account = Row("Sign-in", App.Online.Status.Length > 0 ? App.Online.Status : "Off: no server is set.", false);
            bool set = App.Online.State != OnlineState.Off;
            Choice(account, "Sign in again", false, () => App.ConnectOnline(true), set);
            Choice(account, "Sync now", false, () =>
            {
                App.Sync.Request();
                _said.Text = "A sync runs now.";
            }, App.Online.Account.Length > 0 && !App.Sync.Running && !ShotRunner.Running);
        }
        foreach (KeyAction action in keys.Actions.Where(a => Wanted("Controls", a.Name + " " + a.Description + " " + a.Group + " keys controls")))
        {
            HBoxContainer row = Row(action.Name, action.Description, keys.Changed(action.Id));
            bool waiting = _capturing == action.Id;
            row.AddChild(new Label { Text = waiting ? "press a key..." : keys.KeysText(action.Id), CustomMinimumSize = new Vector2(110, 0), ThemeTypeVariation = waiting ? "WarnLabel" : "" });
            Choice(row, waiting ? "Cancel" : "Change", false, () =>
            {
                _said.Text = "";
                _capturing = waiting ? "" : action.Id;
            });
            Choice(row, "Default", false, () =>
            {
                App.Keys.Reset(action.Id);
                _said.Text = App.Keys.Changed(action.Id) ? "Some of its default keys belong to another action now." : "";
                App.Save();
            }, keys.Changed(action.Id));
        }
        if (_group == "Controls" && words.Length == 0)
        {
            _rows.AddChild(new Label { Text = "Escape, Enter and the number keys are fixed.", ThemeTypeVariation = "DimLabel" });
        }
        if (_rows.GetChildCount() == 0)
        {
            _rows.AddChild(new Label { Text = "No setting matches.", ThemeTypeVariation = "DimLabel" });
        }
    }

    // a setting's row: its name (with a mark when changed) and help on the left, its choices on the right
    private HBoxContainer Row(string name, string help, bool changed)
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = _rows.GetChildCount() % 2 == 0 ? Palette.Ink : Palette.Night,
            BorderColor = Palette.Iron,
            BorderWidthBottom = 1,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        panel.AddChild(row);
        var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 54) };
        words.AddThemeConstantOverride("separation", 0);
        var title = new Label { Text = changed ? name + "  • changed" : name };
        title.AddThemeFontSizeOverride("font_size", 18);
        words.AddChild(title);
        words.AddChild(new Label { Text = help, ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        row.AddChild(words);
        var choices = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        choices.AddThemeConstantOverride("separation", 4);
        row.AddChild(choices);
        _rows.AddChild(panel);
        return choices;
    }

    private static void Choice(HBoxContainer row, string text, bool on, Action press, bool enabled = true)
    {
        var button = new Button { Text = text, ToggleMode = true, ThemeTypeVariation = "ChipButton", FocusMode = Control.FocusModeEnum.None, Disabled = !enabled };
        button.SetPressedNoSignal(on);
        button.Pressed += press;
        row.AddChild(button);
    }

    private void Pick(Setting setting, int option)
    {
        _said.Text = "";
        if (setting.Options[option].StartsWith("Paste", StringComparison.Ordinal) && !Paste(setting.Id))
        {
            _shown = "";
            return;
        }
        setting.Set(App.Settings, option);
        App.Save();
    }

    /// <summary>The key pressed while Change was waiting.</summary>
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
            _said.Text = $"{Rules.KeyBindings.Shown(OS.GetKeycodeString(key))} is fixed and can't be given to anything.";
            return;
        }
        string name = OS.GetKeycodeString(key);
        if (name.Length == 0 || !App.Keys.Bind(id, name, out string? taken))
        {
            _said.Text = "That key can't be used.";
            return;
        }
        _said.Text = taken != null && App.Keys.Action(taken) is KeyAction loser
            ? $"{Rules.KeyBindings.Shown(name)} was the key for {loser.Name}, which has {App.Keys.KeysText(taken)} now."
            : "";
        App.Save();
    }

    // The screen has no address box, so an address or a key comes from the clipboard. False with
    // a reason said when what is there can't be one.
    private bool Paste(string id)
    {
        string text = DisplayServer.ClipboardGet().Trim();
        if (id == "server")
        {
            bool web = Uri.TryCreate(text, UriKind.Absolute, out Uri? address) && address.Scheme is "http" or "https" && text.Length <= 253;
            if (!web)
            {
                _said.Text = text.Length == 0 ? "The clipboard is empty." : "The clipboard doesn't hold an address starting with http:// or https://.";
                return false;
            }
            App.Settings.Server = text;
            return true;
        }
        if (text.Length is 0 or > 128 || text.Any(char.IsWhiteSpace))
        {
            _said.Text = text.Length == 0 ? "The clipboard is empty." : "The clipboard doesn't hold a key (one word, up to 128 characters).";
            return false;
        }
        App.Settings.ServerKey = text;
        return true;
    }

    private static string OnOff(bool on) => on ? "On" : "Off";
}
