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
        // it hangs from its bottom edge, so folding pulls the top down to the header
        OffsetTop = OffsetBottom - (folded ? FoldedHeight : OpenHeight);
    }

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
    }

    public void Add(string line)
    {
        _lines.Add(line);
        if (_lines.Count > KeptLines)
        {
            _lines.RemoveRange(0, KeptLines / 4);
            _text.Clear();
            foreach (string kept in _lines)
            {
                Write(kept);
            }
            return;
        }
        Write(line);
    }

    private void Write(string line)
    {
        if (_text.GetParsedText().Length > 0)
        {
            _text.Newline();
        }
        // tinted by the words the rules' own lines use; a line worded another way just stays plain
        Color? color = line.Contains("goes down") ? DownColor
            : line.Contains(" - miss") ? MissColor
            : line.Contains(" - hit") || line.Contains("CRITICAL") ? HitColor
            : line.Contains("recovers") || line.Contains("gets back up") ? GoodColor
            : null;
        if (color is Color tint)
        {
            _text.PushColor(tint);
        }
        // the dice working ("1d20+1: [19] + 1 = ") in faint ink, so "Alice initiative 20" reads first
        int at = 0;
        foreach (System.Text.RegularExpressions.Match working in DiceWorking().Matches(line))
        {
            _text.AddText(line[at..working.Index]);
            _text.PushColor(MissColor);
            _text.AddText(working.Value);
            _text.Pop();
            at = working.Index + working.Length;
        }
        _text.AddText(line[at..]);
        if (color != null)
        {
            _text.Pop();
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\d*d\d+[^:\[]*:\s*\[[^\]]*\][^=]*=\s*")]
    private static partial System.Text.RegularExpressions.Regex DiceWorking();
}
