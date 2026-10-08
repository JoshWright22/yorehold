using System.Collections.Generic;
using System.Linq;
using System.Text;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The bbcode for an entry laid out like a page of a rules site: the name big in bone, a line of
/// what it is in italics, a rule, the numbers as bold labels, then the words. Ash on ink, from
/// the palette, the same as the website's entries.
/// </summary>
public sealed class BookPage
{
    private readonly StringBuilder _text = new();

    private static string Bright => Palette.Hex(Palette.Bone);
    private static string Line => Palette.Hex(Palette.Iron);
    private static string Faint => Palette.Hex(Palette.Smoke);

    public BookPage Title(string name)
    {
        _text.Append($"[font_size=22][color={Bright}][b]{Escape(name)}[/b][/color][/font_size]\n");
        return this;
    }

    public BookPage Sub(string what)
    {
        if (what.Length > 0)
        {
            _text.Append($"[i]{Escape(what)}[/i]\n");
        }
        return this;
    }

    public BookPage Rule()
    {
        _text.Append($"[hr color={Line} height=1 width=100%]\n");
        return this;
    }

    /// <summary>"Armor Class 16": the label bold, the value plain. Nothing for an empty value.</summary>
    public BookPage Stat(string label, string value)
    {
        if (value.Length > 0)
        {
            _text.Append($"[color={Bright}][b]{Escape(label)}[/b][/color] {Value(value)}\n");
        }
        return this;
    }

    /// <summary>Several stats on one line, apart.</summary>
    public BookPage Stats(params (string Label, string Value)[] stats)
    {
        List<string> parts = stats.Where(s => s.Value.Length > 0).Select(s => $"[color={Bright}][b]{Escape(s.Label)}[/b][/color] {Value(s.Value)}").ToList();
        if (parts.Count > 0)
        {
            _text.Append(string.Join("   ", parts)).Append('\n');
        }
        return this;
    }

    /// <summary>A small heading in capitals, like "ACTIONS".</summary>
    public BookPage Heading(string heading)
    {
        _text.Append($"[font_size=13][color={Faint}][b]{Escape(heading.ToUpperInvariant())}[/b][/color][/font_size]\n");
        return this;
    }

    public BookPage Text(string words)
    {
        if (words.Length > 0)
        {
            _text.Append(Escape(words)).Append('\n');
        }
        return this;
    }

    /// <summary>A paragraph that starts with a bold name, the way a stat block lists actions.</summary>
    public BookPage Entry(string name, string words)
    {
        _text.Append($"[b][i]{Escape(name)}.[/i][/b] {Escape(words)}\n");
        return this;
    }

    /// <summary>A stat whose value is pressed to change it; the page's reader gets meta back.</summary>
    public BookPage Switch(string label, string value, string meta)
    {
        _text.Append($"[color={Bright}][b]{Escape(label)}[/b][/color] [url={meta}]{Escape(value)}[/url]\n");
        return this;
    }

    /// <summary>A note in the faint ink, for what is not a rule (how many there are, where it came from).</summary>
    public BookPage Note(string words)
    {
        if (words.Length > 0)
        {
            _text.Append($"[color={Faint}]{Escape(words)}[/color]\n");
        }
        return this;
    }

    /// <summary>What is wrong, in red.</summary>
    public BookPage Warn(string words)
    {
        _text.Append($"[color={Palette.Hex(Palette.Red)}]{Escape(words)}[/color]\n");
        return this;
    }

    /// <summary>A grid of short cells under centred headings, like the six ability scores.</summary>
    public BookPage Table(IReadOnlyList<string> headings, IReadOnlyList<string> cells)
    {
        _text.Append($"[table={headings.Count}]");
        foreach (string heading in headings)
        {
            _text.Append($"[cell expand=1][center][b]{Escape(heading)}[/b][/center][/cell]");
        }
        foreach (string cell in cells)
        {
            _text.Append($"[cell expand=1][center]{Value(cell)}[/center][/cell]");
        }
        _text.Append("[/table]\n");
        return this;
    }

    public BookPage Gap()
    {
        _text.Append("[font_size=6] [/font_size]\n");
        return this;
    }

    public override string ToString() => _text.ToString();

    /// <summary>Content text with its square brackets kept from being read as bbcode.</summary>
    public static string Escape(string text) => text.Replace("[", "[lb]");

    // numbers on a page are in the monospace face, like a rule book's tables
    private static string Value(string text)
    {
        bool number = text.Length > 0 && (char.IsDigit(text[0]) || (text.Length > 1 && (text[0] == '+' || text[0] == '-') && char.IsDigit(text[1])));
        return number ? $"[code]{Escape(text)}[/code]" : Escape(text);
    }

    /// <summary>"Slashing damage 1d8, half on a save": what an effect's steps do, in a line each.</summary>
    public static List<string> EffectLines(Effect effect)
    {
        var lines = new List<string>();
        Describe(effect.Steps, lines);
        return lines;
    }

    private static void Describe(List<EffectStep> steps, List<string> lines)
    {
        foreach (EffectStep step in steps)
        {
            string onSave = step.OnSave switch
            {
                OnSave.Half => ", half on a save",
                OnSave.None => ", none on a save",
                _ => "",
            };
            string when = step.When.Length > 0 ? $" on a {Words(step.When)}" : "";
            string line = step.Kind switch
            {
                EffectKind.Damage => $"{(step.Type.Length > 0 ? Capital(step.Type) + " damage" : "Damage")} {Dice(step.Amount)}{when}{onSave}",
                EffectKind.Heal => $"Heals {Dice(step.Amount)}{when}",
                EffectKind.TempHp => $"{Dice(step.Amount)} temporary HP{when}",
                EffectKind.Condition => step.Remove ? $"Ends {Words(step.Id)}{when}" : $"{Capital(Words(step.Id))}{(step.Duration == 1 ? " for 1 round" : step.Duration > 0 ? $" for {step.Duration} rounds" : "")}{when}",
                EffectKind.Surface => $"Leaves {Words(step.Id)}{(step.Size > 0 ? $" ({step.Size:0.#} squares)" : "")}",
                EffectKind.Light => "Gives light",
                EffectKind.Move => $"Moves the target{(step.Size > 0 ? $" {step.Size:0.#} squares" : "")}",
                EffectKind.Summon => $"Calls {Words(step.Id)}",
                _ => "",
            };
            if (line.Length > 0)
            {
                lines.Add(line);
            }
            Describe(step.Steps, lines);
            foreach (EffectOption option in step.Options)
            {
                Describe(option.Steps, lines);
            }
        }
    }

    private static string Dice(string amount) => amount == "weapon" ? "as the weapon" : amount;

    // "saveFailed" reads "failed save", "stat-bonus" reads "stat bonus"
    private static string Words(string id) => id switch
    {
        "saveFailed" => "failed save",
        "saveSucceeded" => "made save",
        "crit" => "critical hit",
        _ => id.Replace('-', ' '),
    };

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
