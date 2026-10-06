using System;
using System.Collections.Generic;
using Godot;

namespace Yorehold;

/// <summary>
/// A side column of Create's tools and settings. The mode says what is in it with a signature
/// each frame; it is made again only when the signature changes, and in between its labels,
/// toggles, greyed buttons and text boxes follow their values. A text box being typed in keeps
/// what is typed.
/// </summary>
public partial class ToolColumn : VBoxContainer
{
    private string _shown = "\u0000";
    private readonly List<(LineEdit Box, Func<string> Value)> _fields = new();
    private readonly List<(Button Button, Func<bool> On)> _toggles = new();
    private readonly List<(Button Button, Func<bool> Enabled)> _enables = new();
    private readonly List<(Label Label, Func<string> Text)> _labels = new();
    private readonly List<(Button Button, Func<string> Text)> _texts = new();

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 3);
    }

    /// <summary>Makes the column again with build when the signature differs from the last; follows the values either way.</summary>
    public void Build(string signature, Action build)
    {
        if (signature != _shown)
        {
            _shown = signature;
            _fields.Clear();
            _toggles.Clear();
            _enables.Clear();
            _labels.Clear();
            _texts.Clear();
            foreach (Node child in GetChildren())
            {
                RemoveChild(child);
                child.QueueFree();
            }
            build();
        }
        Follow();
    }

    /// <summary>Makes it again on the next Build, whatever the signature.</summary>
    public void Invalidate() => _shown = "\u0000";

    public Label Heading(string text, Container? into = null) => Add(new Label { Text = text.ToUpperInvariant(), ThemeTypeVariation = "TitleLabel" }, into);

    public Label Dim(string text, Container? into = null) => Add(new Label { Text = text, ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart }, into);

    public Label Live(Func<string> text, string variation = "DimLabel", Container? into = null)
    {
        Label label = Add(new Label { ThemeTypeVariation = variation, AutowrapMode = TextServer.AutowrapMode.WordSmart }, into);
        _labels.Add((label, text));
        return label;
    }

    public Button Toggle(string text, Func<bool> on, Action press, Container? into = null, string variation = "RowButton")
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            ThemeTypeVariation = variation,
            Alignment = HorizontalAlignment.Left,
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 24),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
        };
        button.Pressed += () =>
        {
            press();
            button.SetPressedNoSignal(on());
        };
        _toggles.Add((button, on));
        return Add(button, into);
    }

    public Button Act(string text, Action press, Func<bool>? enabled = null, Container? into = null)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 26), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += press;
        if (enabled != null)
        {
            _enables.Add((button, enabled));
        }
        return Add(button, into);
    }

    /// <summary>A button whose words follow a value.</summary>
    public Button Act(Func<string> text, Action press, Func<bool>? enabled = null, Container? into = null)
    {
        Button button = Act(text(), press, enabled, into);
        _texts.Add((button, text));
        return button;
    }

    /// <summary>A button kept to its own width in a row, like "-" and "+".</summary>
    public static Button Narrow(Button button, float width = 30)
    {
        button.SizeFlagsHorizontal = SizeFlags.Fill;
        button.CustomMinimumSize = new Vector2(width, 26);
        return button;
    }

    public HBoxContainer Row()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 3);
        AddChild(row);
        return row;
    }

    /// <summary>A text box over a value: it shows the value unless it is being typed in, and says each change.</summary>
    public LineEdit Field(Func<string> value, Action<string> changed, Action? done = null, string placeholder = "", Container? into = null)
    {
        var box = new LineEdit { PlaceholderText = placeholder, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 26) };
        box.TextChanged += changed.Invoke;
        box.TextSubmitted += _ => box.ReleaseFocus();
        box.FocusExited += () =>
        {
            done?.Invoke();
            box.Text = value();
        };
        box.Text = value();
        _fields.Add((box, value));
        return Add(box, into);
    }

    public Control Gap(int height = 6) => Add(new Control { CustomMinimumSize = new Vector2(0, height) }, null);

    private void Follow()
    {
        foreach ((LineEdit box, Func<string> value) in _fields)
        {
            if (!box.HasFocus())
            {
                string now = value();
                if (box.Text != now)
                {
                    box.Text = now;
                }
            }
        }
        foreach ((Button button, Func<bool> on) in _toggles)
        {
            button.SetPressedNoSignal(on());
        }
        foreach ((Button button, Func<bool> enabled) in _enables)
        {
            button.Disabled = !enabled();
        }
        foreach ((Button button, Func<string> text) in _texts)
        {
            string now = text();
            if (button.Text != now)
            {
                button.Text = now;
            }
        }
        foreach ((Label label, Func<string> text) in _labels)
        {
            string now = text();
            if (label.Text != now)
            {
                label.Text = now;
            }
        }
    }

    private T Add<T>(T control, Container? into) where T : Control
    {
        (into ?? (Container)this).AddChild(control);
        return control;
    }
}
