using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// A character sheet as a book page (SheetPage). The character screens show the sheet a draft or a
/// library file builds, or why there is none yet.
/// </summary>
public partial class SheetView : PanelContainer
{
    // the scene has it
    private RichTextLabel _text = null!;

    public override void _Ready()
    {
        _text = GetNode<RichTextLabel>("Text");
    }

    /// <summary>Just a name and a few lines, for a seat whose hero is rolled when the adventure starts.</summary>
    public void ShowText(string title, string text)
    {
        _text.Text = SheetPage.Text(title, text);
    }

    /// <summary>
    /// A built sheet, or the reason there is none. choices gives the background and feats by name;
    /// null for a sheet without them.
    /// </summary>
    public void ShowSheet(Ruleset rules, Compendium compendium, CharacterSheet? sheet, CharacterChoices? choices, string problem, bool carrying = true)
    {
        if (sheet != null)
        {
            _text.Text = SheetPage.Build(rules, compendium, sheet, choices, carrying);
            return;
        }
        string name = choices?.Name.Trim() ?? "";
        string why = problem.Length == 0 ? "Not ready yet." : char.ToUpperInvariant(problem[0]) + problem[1..];
        _text.Text = new BookPage().Title(name.Length == 0 ? "New character" : name).Rule().Warn(why).ToString();
    }

    public static string Signed(int n) => n >= 0 ? "+" + n : n.ToString();
}
