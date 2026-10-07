using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace Yorehold;

/// <summary>
/// The window beside the game in a playtest (playtest.ps1): which test this is, what to try, a box
/// for a note and Good, Problem or Skip. The answer goes on the end of the results file with a
/// picture of the game for a problem, and the game quits so playtest.ps1 opens the next test.
/// Restart quits so the same test opens again; Stop ends the run.
/// It is its own window on the desktop, so it never covers what is being tested.
/// </summary>
public partial class PlaytestWindow : Window
{
    // what playtest.ps1 does next, by the game's exit code
    private const int Next = 0, Again = 3, Stop = 4;

    private string _id = "";
    private string _name = "";
    private string _results = "";
    private TextEdit _note = null!;

    public override void _Ready()
    {
        string queue = "", number = "";
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            switch (args[i])
            {
                case "--playtest": _id = args[++i]; break;
                case "--queue": queue = args[++i]; break;
                case "--results": _results = args[++i]; break;
                case "--number": number = args[++i]; break;
            }
        }
        JsonNode? item = null;
        try
        {
            item = (JsonNode.Parse(File.ReadAllText(queue))?["items"] as JsonArray)?.FirstOrDefault(i => i?["id"]?.GetValue<string>() == _id);
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or ArgumentException)
        {
            GD.PushError($"Playtest queue {queue}: {error.Message}");
        }
        _name = item?["name"]?.GetValue<string>() ?? _id;

        Title = "Playtest " + number;
        Size = new Vector2I(400, 600);
        AlwaysOnTop = true;
        Theme = GD.Load<Theme>("res://scenes/hud/hud-theme.tres");
        CloseRequested += () => GetTree().Quit(Stop);
        PlaceBesideGame();

        var back = new PanelContainer();
        back.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(back);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 8);
        back.AddChild(rows);
        rows.AddChild(new Label { Text = ("Test " + number).ToUpperInvariant(), ThemeTypeVariation = "CapsLabel" });
        var name = new Label { Text = _name, ThemeTypeVariation = "TitleLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        name.AddThemeFontSizeOverride("font_size", 22);
        rows.AddChild(name);
        rows.AddChild(new Label { Text = item?["area"]?.GetValue<string>() ?? "", ThemeTypeVariation = "DimLabel" });
        rows.AddChild(new Label { Text = "TRY", ThemeTypeVariation = "CapsLabel" });
        var tryText = new Label { Text = item?["try"]?.GetValue<string>() ?? "", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0) };
        tryText.AddThemeFontSizeOverride("font_size", 15);
        rows.AddChild(tryText);
        rows.AddChild(new Label { Text = "WHAT IS WRONG (OR RIGHT)", ThemeTypeVariation = "CapsLabel" });
        _note = new TextEdit
        {
            PlaceholderText = "Write as much as you like. A problem saves a picture of the game too.",
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 140),
        };
        rows.AddChild(_note);

        var answers = new HBoxContainer();
        answers.AddThemeConstantOverride("separation", 6);
        rows.AddChild(answers);
        Add(answers, "Good", "EndTurnButton", () => Answer("good"));
        Add(answers, "Problem", "", () => Answer("problem"));
        Add(answers, "Skip", "", () => Answer("skip"));
        var other = new HBoxContainer();
        other.AddThemeConstantOverride("separation", 6);
        rows.AddChild(other);
        Add(other, "Start this test again", "", () => GetTree().Quit(Again));
        Add(other, "Stop", "", () => GetTree().Quit(Stop));
        rows.AddChild(new Label
        {
            Text = "Answers go to " + _results,
            ThemeTypeVariation = "DimLabel",
            AutowrapMode = TextServer.AutowrapMode.Arbitrary,
        });
    }

    private static void Add(HBoxContainer row, string text, string look, Action pressed)
    {
        var button = new Button { Text = text, ThemeTypeVariation = look, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 36) };
        button.Pressed += pressed;
        row.AddChild(button);
    }

    // To the right of the game when the screen has room, else against the screen's right edge.
    private void PlaceBesideGame()
    {
        Vector2I gamePosition = DisplayServer.WindowGetPosition();
        Vector2I gameSize = DisplayServer.WindowGetSize();
        Rect2I screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        int x = gamePosition.X + gameSize.X + 8;
        if (x + Size.X > screen.End.X)
        {
            x = screen.End.X - Size.X;
        }
        Position = new Vector2I(x, Math.Max(screen.Position.Y, gamePosition.Y));
    }

    private void Answer(string verdict)
    {
        string note = _note.Text.Trim();
        string picture = "";
        if (verdict == "problem" || note.Length > 0)
        {
            picture = SavePicture();
        }
        var line = new JsonObject
        {
            ["id"] = _id,
            ["name"] = _name,
            ["verdict"] = verdict,
            ["note"] = note,
            ["picture"] = picture,
            ["at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_results))!);
            File.AppendAllText(_results, line.ToJsonString() + "\n");
        }
        catch (IOException error)
        {
            GD.PushError($"Playtest results {_results}: {error.Message}");
            return; // stay on this test rather than lose the answer
        }
        GetTree().Quit(Next);
    }

    // The game as it looks now, beside the results, named after the test.
    private string SavePicture()
    {
        try
        {
            string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_results))!, "playtest-pictures");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"{_id}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            Image image = GetTree().Root.GetTexture().GetImage();
            return image.SavePng(file) == Error.Ok ? file : "";
        }
        catch (IOException)
        {
            return "";
        }
    }
}
