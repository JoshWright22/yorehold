using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The credits: the game's own entries from ui/credits.json, the engine with its MIT licence text,
/// and every library the engine is built from with its copyright lines and licence. The engine
/// part is asked of the engine itself, so it always matches the build that is running.
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

        Godot.Collections.Dictionary version = Engine.GetVersionInfo();
        _rows.Add(new Row("engine", "Godot Engine", "Engine", "Juan Linietsky, Ariel Manzur and the Godot Engine contributors", "MIT",
            $"The engine the game runs on, version {version["string"].AsString()}. godotengine.org", new List<string>(), Engine.GetLicenseText()));

        Godot.Collections.Dictionary licences = Engine.GetLicenseInfo();
        foreach (Godot.Collections.Dictionary component in Engine.GetCopyrightInfo())
        {
            string name = component["name"].AsString();
            if (name == "Godot Engine")
            {
                continue; // it has its own row above
            }
            var copyright = new List<string>();
            var names = new List<string>();
            foreach (Godot.Collections.Dictionary part in component["parts"].AsGodotArray<Godot.Collections.Dictionary>())
            {
                foreach (string line in part["copyright"].AsStringArray())
                {
                    if (!copyright.Contains(line))
                    {
                        copyright.Add(line);
                    }
                }
                string licence = part["license"].AsString();
                if (!names.Contains(licence))
                {
                    names.Add(licence);
                }
            }
            // "Expat and Zlib" names two texts; anything the engine has no text for is left as its name
            var texts = new List<string>();
            foreach (string licence in names.SelectMany(n => n.Split(new[] { " and ", " or " }, StringSplitOptions.RemoveEmptyEntries)).Distinct())
            {
                if (licences.ContainsKey(licence))
                {
                    texts.Add($"{licence}\n{licences[licence].AsString()}");
                }
            }
            _rows.Add(new Row("lib:" + name, name, "Library", "", string.Join(", ", names), "Part of Godot Engine.", copyright, string.Join("\n\n", texts)));
        }
    }
}
