using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The credits, a small link at the foot of the title: the game's own entries from ui/credits.json
/// and the engine, whose page carries its MIT licence and the notices of the libraries inside it.
/// The engine part is asked of the engine itself, so it always matches the build that is running.
/// </summary>
public sealed class CreditsPanel
{
    private sealed record Row(string Key, string Name, string Kind, string By, string Licence, string Text, List<string> Copyright, string LicenceText);

    private static readonly DataColumn[] Columns = { new("Name", 210), new("Kind", 70), new("Licence", 150) };

    public DataPanel View { get; }

    private readonly List<Row> _rows = new();
    private bool _read;

    public CreditsPanel(DataPanel view)
    {
        View = view;
    }

    public void Refresh()
    {
        if (!_read)
        {
            _read = true;
            Read();
        }
        View.SetHead("Credits", $"{_rows.Count} entries");
        View.SetSources(Array.Empty<(string, string)>(), "");
        View.SetTabs(new[] { "All" }.Concat(_rows.Select(r => r.Kind).Distinct()).ToArray());
        View.SetChips(Array.Empty<string>());
        View.SetColumns(Columns);
        View.SetRows(_rows.Select((r, index) => new DataRow
        {
            Key = r.Key,
            Cells = new[] { r.Name, r.Kind, r.Licence },
            Sort = new IComparable?[] { index }, // the game first, then the engine, then what is inside it
            Tags = new HashSet<string> { r.Kind },
            Search = r.By + " " + string.Join(" ", r.Copyright),
        }).ToList());

        Row? picked = _rows.Find(r => r.Key == View.Picked);
        if (picked == null)
        {
            View.SetEntry(new BookPage().Note("Nothing matches.").ToString(), Array.Empty<DataAction>(), "");
            return;
        }
        var page = new BookPage().Title(picked.Name).Sub(picked.By.Length > 0 ? $"{picked.Kind.ToLowerInvariant()}, by {picked.By}" : picked.Kind.ToLowerInvariant()).Rule();
        page.Stat("Licence", picked.Licence);
        page.Text(picked.Text);
        if (picked.Copyright.Count > 0)
        {
            page.Gap().Heading("Copyright");
            foreach (string line in picked.Copyright)
            {
                page.Text(line);
            }
        }
        if (picked.LicenceText.Length > 0)
        {
            page.Gap().Heading("Licence text").Text(picked.LicenceText);
        }
        View.SetEntry(page.ToString(), Array.Empty<DataAction>(), "");
        View.SetFoot("This game uses Godot Engine, available under the MIT licence.");
    }

    private void Read()
    {
        try
        {
            foreach (CreditEntry entry in Credits.Read(ContentNode.Read(App.Content(), "ui/credits.json")))
            {
                string by = entry.By;
                string text = entry.Name == "Yorehold" ? $"Version {Rules.Version.Text}. {entry.Text}" : entry.Text;
                _rows.Add(new Row("game:" + entry.Name, entry.Name, entry.Kind, by, entry.Licence, text, new List<string>(), ""));
            }
        }
        catch (ContentException error)
        {
            GD.PushWarning($"Couldn't read the credits: {error.Message}");
        }

        // the libraries inside the engine aren't rows of their own (Josh, 10/7), but their notices
        // still go with the game: one block on the engine's page
        Godot.Collections.Dictionary licences = Engine.GetLicenseInfo();
        var notices = new List<string>();
        var used = new List<string>();
        foreach (Godot.Collections.Dictionary component in Engine.GetCopyrightInfo())
        {
            string name = component["name"].AsString();
            if (name == "Godot Engine")
            {
                continue;
            }
            foreach (Godot.Collections.Dictionary part in component["parts"].AsGodotArray<Godot.Collections.Dictionary>())
            {
                notices.AddRange(part["copyright"].AsStringArray().Select(line => $"{name}: {line}"));
                // "Expat and Zlib" names two texts; anything the engine has no text for is left as its name
                used.AddRange(part["license"].AsString().Split(new[] { " and ", " or " }, StringSplitOptions.RemoveEmptyEntries));
            }
        }
        string texts = string.Join("\n\n", used.Distinct().Where(l => licences.ContainsKey(l)).Select(l => $"{l}\n{licences[l].AsString()}"));
        Godot.Collections.Dictionary version = Engine.GetVersionInfo();
        _rows.Add(new Row("engine", "Godot Engine", "Engine", "Juan Linietsky, Ariel Manzur and the Godot Engine contributors", "MIT",
            $"The engine the game runs on, version {version["string"].AsString()}, with the libraries inside it. godotengine.org",
            notices.Distinct().ToList(), Engine.GetLicenseText() + (texts.Length > 0 ? "\n\n" + texts : "")));
    }
}
