using System.Collections.Generic;
using Godot;

namespace Yorehold;

/// <summary>
/// The log in the bottom right corner: every line the world says, rolls spelled out, newest at the
/// bottom. The header folds it down to one row and back.
/// </summary>
public partial class LogPanel : PanelContainer
{
    [Export] public int KeptLines { get; set; } = 200;
    [Export] public float OpenHeight { get; set; } = 210;
    [Export] public float FoldedHeight { get; set; } = 48;
    [Export] public Color MissColor { get; set; } = Palette.Smoke;
    [Export] public Color HitColor { get; set; } = Palette.Straw;
    [Export] public Color DownColor { get; set; } = Palette.Red;
    [Export] public Color GoodColor { get; set; } = Palette.Leaf;

    private readonly List<string> _lines = new();
    // the scene has all of these
    private Button _header = null!;
    private Control _body = null!;
    private RichTextLabel _text = null!;
    private bool _folded;

    public bool Folded => _folded;

    private RichTextLabel? _mirror;

    /// <summary>A second place the lines are written (the chat column's Combat log tab), given all kept lines when set.</summary>
    public RichTextLabel? Mirror
    {
        get => _mirror;
        set
        {
            _mirror = value;
            if (value == null)
            {
                return;
            }
            value.Clear();
            foreach (string kept in _lines)
            {
                Write(value, kept);
            }
        }
    }

    public override void _Ready()
    {
        _header = GetNode<Button>("Rows/Header");
        _body = GetNode<Control>("Rows/Body");
        _text = GetNode<RichTextLabel>("Rows/Body/Text");
        _header.Pressed += () => Fold(!_folded);
        Fold(false);
    }

    public void Fold(bool folded)
    {
        _folded = folded;
        _body.Visible = !folded;
        _header.Text = folded ? "Log  (show)" : "Log  (hide)";
        // it hangs from its bottom edge, so folding pulls the top down to the header; open, it is
        // as tall as its lines up to OpenHeight, so two lines aren't a large empty box
        float open = Mathf.Min(OpenHeight, FoldedHeight + 8 + _text.GetContentHeight());
        OffsetTop = OffsetBottom - (folded ? FoldedHeight : Mathf.Max(FoldedHeight, open));
    }

    // the text lays its new line out on the next frame; the height follows it then
    private void Fit() => Callable.From(() => Fold(_folded)).CallDeferred();

    /// <summary>How far its bottom edge sits above the screen's: over the hotbar in a fight, in the corner otherwise.</summary>
    public void Rest(float above)
    {
        if (OffsetBottom != -above)
        {
            OffsetBottom = -above;
            Fold(_folded);
        }
    }

    public void Clear()
    {
        _lines.Clear();
        _text.Clear();
        _mirror?.Clear();
        Fit();
    }

    public void Add(string line)
    {
        _lines.Add(line);
        Fit();
        if (_lines.Count > KeptLines)
        {
            _lines.RemoveRange(0, KeptLines / 4);
            Mirror = _mirror;
            _text.Clear();
            foreach (string kept in _lines)
            {
                Write(_text, kept);
            }
            return;
        }
        Write(_text, line);
        if (_mirror != null)
        {
            Write(_mirror, line);
        }
    }

    private void Write(RichTextLabel text, string line)
    {
        if (text.GetParsedText().Length > 0)
        {
            text.Newline();
        }
        // tinted by the words the rules' own lines use; a line worded another way just stays plain
        Color? color = line.Contains("goes down") ? DownColor
            : line.Contains(" - miss") ? MissColor
            : line.Contains(" - hit") || line.Contains("CRITICAL") ? HitColor
            : line.Contains("recovers") || line.Contains("gets back up") ? GoodColor
            : null;
        if (color is Color tint)
        {
            text.PushColor(tint);
        }
        // the dice working ("1d20+1: [19] + 1 = ") in faint ink, so "Alice initiative 20" reads first
        int at = 0;
        foreach (System.Text.RegularExpressions.Match working in DiceWorking().Matches(line))
        {
            text.AddText(line[at..working.Index]);
            text.PushColor(MissColor);
            text.AddText(working.Value);
            text.Pop();
            at = working.Index + working.Length;
        }
        text.AddText(line[at..]);
        if (color != null)
        {
            text.Pop();
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\d*d\d+[^:\[]*:\s*\[[^\]]*\][^=]*=\s*")]
    private static partial System.Text.RegularExpressions.Regex DiceWorking();
}
