using System.Collections.Generic;
using System.Globalization;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Screenshot runs for check.ps1 -Shot. Does nothing unless the game was started with user
/// arguments after "--":  --shot file.png  [--frames N]  [--script file.txt]
/// It plays the input script frame by frame, saves the picture on the last frame and quits.
/// An autoload, so it keeps counting when the scene changes.
/// </summary>
public partial class ShotRunner : Node
{
    private string _shot = "";
    private int _frames = 300;
    private List<InputStep> _script = new();
    private int _frame;
    private Vector2 _mouse;
    private MouseButtonMask _held;
    private readonly List<string> _pendingShots = new();
    private bool _failed;

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string scriptFile = "";
        for (int i = 0; i + 1 < args.Length; i++)
        {
            switch (args[i])
            {
                case "--shot": _shot = args[++i]; break;
                case "--frames": _frames = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--script": scriptFile = args[++i]; break;
            }
        }

        if (_shot == "")
        {
            SetProcess(false);
            return;
        }

        if (scriptFile != "")
        {
            if (!System.IO.File.Exists(scriptFile))
            {
                GD.PushError($"Input script not found: {scriptFile}");
                GetTree().Quit(1);
                return;
            }
            _script = InputScript.Parse(System.IO.File.ReadAllText(scriptFile));
        }

        // Scripted events must reach the game before it handles the frame.
        ProcessPriority = int.MinValue;
        RenderingServer.FramePostDraw += AfterDraw;
        GD.Print($"Shot run: {_frames} frames, {_script.Count} script steps, saving {_shot}");
    }

    public override void _ExitTree()
    {
        if (_shot != "")
        {
            RenderingServer.FramePostDraw -= AfterDraw;
        }
    }

    public override void _Process(double delta)
    {
        _frame++;
        foreach (InputStep step in _script)
        {
            if (step.Frame == _frame)
            {
                Play(step);
            }
        }
        if (_frame == _frames)
        {
            _pendingShots.Add(_shot);
        }
    }

    // The viewport only holds this frame's picture once drawing is done.
    private void AfterDraw()
    {
        foreach (string file in _pendingShots)
        {
            Save(file);
        }
        _pendingShots.Clear();
        if (_frame >= _frames)
        {
            RenderingServer.FramePostDraw -= AfterDraw;
            _shot = "";
            GetTree().Quit(_failed ? 1 : 0);
        }
    }

    private void Play(InputStep step)
    {
        switch (step.Command)
        {
            case "move":
            {
                Vector2 to = new(Number(step.A), Number(step.B));
                var motion = new InputEventMouseMotion
                {
                    Position = to,
                    GlobalPosition = to,
                    Relative = to - _mouse,
                    ButtonMask = _held,
                };
                _mouse = to;
                Input.ParseInputEvent(motion);
                break;
            }
            case "down":
            case "up":
            {
                MouseButton button = step.A == "right" ? MouseButton.Right : step.A == "middle" ? MouseButton.Middle : MouseButton.Left;
                MouseButtonMask bit = (MouseButtonMask)(1 << ((int)button - 1));
                bool pressed = step.Command == "down";
                _held = pressed ? _held | bit : _held & ~bit;
                Input.ParseInputEvent(new InputEventMouseButton
                {
                    ButtonIndex = button,
                    Pressed = pressed,
                    Position = _mouse,
                    GlobalPosition = _mouse,
                    ButtonMask = _held,
                });
                break;
            }
            case "wheel":
            {
                // Godot has no wheel amount event: a notch is a press and release of a wheel button.
                float amount = Number(step.A);
                MouseButton button = amount < 0 ? MouseButton.WheelDown : MouseButton.WheelUp;
                foreach (bool pressed in new[] { true, false })
                {
                    Input.ParseInputEvent(new InputEventMouseButton
                    {
                        ButtonIndex = button,
                        Pressed = pressed,
                        Factor = Mathf.Abs(amount),
                        Position = _mouse,
                        GlobalPosition = _mouse,
                        ButtonMask = _held,
                    });
                }
                break;
            }
            case "key":
            case "keyup":
            {
                Key key = KeyFromName(step.A);
                if (key == Key.None)
                {
                    GD.PushError($"Input script: unknown key '{step.A}' on frame {step.Frame}");
                    _failed = true;
                    break;
                }
                Input.ParseInputEvent(new InputEventKey
                {
                    Keycode = key,
                    PhysicalKeycode = key,
                    Pressed = step.Command == "key",
                });
                break;
            }
            case "text":
                foreach (char letter in step.A)
                {
                    foreach (bool pressed in new[] { true, false })
                    {
                        Input.ParseInputEvent(new InputEventKey { Unicode = letter, Pressed = pressed });
                    }
                }
                break;
            case "shot":
                _pendingShots.Add(step.A);
                break;
            default:
                GD.PushError($"Input script: unknown command '{step.Command}' on frame {step.Frame}");
                _failed = true;
                break;
        }
    }

    private static float Number(string word)
    {
        return float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0f;
    }

    // Scripts written for the C++ client use SDL key names; these are the ones Godot spells differently.
    private static readonly Dictionary<string, string> KeyNames = new()
    {
        ["Return"] = "Enter",
        ["Left Ctrl"] = "Ctrl",
        ["Right Ctrl"] = "Ctrl",
        ["Left Shift"] = "Shift",
        ["Right Shift"] = "Shift",
        ["Left Alt"] = "Alt",
        ["Right Alt"] = "Alt",
        ["Keypad Enter"] = "Kp Enter",
    };

    private static Key KeyFromName(string word)
    {
        // Underscores stand in for spaces in key names: "key Left_Ctrl".
        string name = word.Replace('_', ' ');
        if (KeyNames.TryGetValue(name, out string? other))
        {
            name = other;
        }
        return OS.FindKeycodeFromString(name);
    }

    private void Save(string file)
    {
        // Paths in scripts are written from the workspace folder (".dev/name.png"), one above the project.
        if (!System.IO.Path.IsPathRooted(file))
        {
            file = System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", file);
        }
        file = System.IO.Path.GetFullPath(file);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);

        Error result = GetViewport().GetTexture().GetImage().SavePng(file);
        if (result != Error.Ok)
        {
            GD.PushError($"Could not save screenshot {file}: {result}");
            _failed = true;
            return;
        }
        GD.Print($"Saved {file}");
    }
}
