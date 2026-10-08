using System;
using System.Collections.Generic;
using System.Linq;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The adventures to start, New adventure's page: the game's own, the ones made in Create and the
/// books imported, as a list with tabs for each, the picked one's page on the right and its cover
/// filling the window behind. Start goes to that adventure's lobby.
/// </summary>
public sealed class AdventuresPanel
{
    private static readonly DataColumn[] Columns = { new("Adventure", 230), new("Rules", 90), new("From", 90), new("Chapters", 70, true), new("Levels", 60) };

    /// <summary>Start was pressed on an adventure: its package folder, "" for the game's own.</summary>
    public event Action<string>? StartPressed;
    /// <summary>The picked adventure's cover changed: the content it is in and its path ("" for none).</summary>
    public event Action<ContentFiles?, string>? CoverPicked;

    public DataPanel View { get; }

    private List<AdventureListing> _list = new();
    private readonly Dictionary<string, ContentFiles> _files = new(StringComparer.Ordinal);
    private string _coverShown = "\u0000";

    public AdventuresPanel(DataPanel view)
    {
        View = view;
        view.ActionPressed += id =>
        {
            if (id == "start" && _list.Find(l => Key(l) == View.Picked) is AdventureListing picked && picked.Adventure != null)
            {
                StartPressed?.Invoke(picked.Package);
            }
        };
    }

    private static string Key(AdventureListing listing) => listing.Package.Length == 0 ? "game" : listing.Package;

    public void Read()
    {
        _list = AdventureLibrary.List(Places.GameContent(), Places.CreateFolder());
        _files.Clear();
        _coverShown = "\u0000";
        View.Reset();
    }

    // the content an adventure plays from, with the players' art packs: its cover may be in either
    private ContentFiles FilesOf(AdventureListing listing)
    {
        if (!_files.TryGetValue(Key(listing), out ContentFiles? files))
        {
            files = App.Content();
            if (listing.Package.Length > 0)
            {
                files.Add(listing.Package);
            }
            _files[Key(listing)] = files;
        }
        return files;
    }

    public void Refresh()
    {
        View.SetHead("New adventure", _list.Count == 1 ? "1 adventure" : $"{_list.Count} adventures");
        View.SetSources(Array.Empty<(string, string)>(), "");
        View.SetTabs(new[] { "All", AdventureLibrary.Game, AdventureLibrary.Made, AdventureLibrary.Imported });
        View.SetChips(Array.Empty<string>());
        View.SetColumns(Columns);
        View.SetRows(_list.Select((l, index) => new DataRow
        {
            Key = Key(l),
            Cells = new[] { l.Name, l.System, l.Source, l.Adventure?.ChapterFolders.Count.ToString() ?? "", l.Adventure == null ? "" : $"{l.Adventure.MinLevel}-{l.Adventure.MaxLevel}" },
            Sort = new IComparable?[] { l.Name, l.System, l.Source, l.Adventure?.ChapterFolders.Count ?? -1, l.Adventure?.MinLevel ?? 0 },
            Tags = new HashSet<string> { l.Source },
            Search = (l.System + " " + (l.Adventure?.Description ?? "")).Trim(),
            Dim = l.Adventure == null,
        }).ToList());

        AdventureListing? picked = _list.Find(l => Key(l) == View.Picked);
        string cover = picked?.Adventure?.Cover ?? "";
        if (Key(picked ?? _list[0]) + cover != _coverShown)
        {
            _coverShown = Key(picked ?? _list[0]) + cover;
            CoverPicked?.Invoke(picked == null ? null : FilesOf(picked), cover);
        }
        if (picked == null)
        {
            View.SetEntry(new BookPage().Note("Pick an adventure to see what it is.").ToString(), Array.Empty<DataAction>(), "");
            View.SetFoot("");
            return;
        }
        // the stats line is full, so the rules system goes with where it is from
        string from = picked.Source.ToLowerInvariant() + (picked.System.Length > 0 ? $", {picked.System} rules" : "");
        var page = new BookPage().Title(picked.Name).Sub(from).Rule();
        if (picked.Adventure is Adventure adventure)
        {
            page.Stats(("Levels", $"{adventure.MinLevel} to {adventure.MaxLevel}"), ("Party", adventure.RecommendedPartySize.ToString()),
                ("Chapters", adventure.ChapterFolders.Count.ToString()));
            if (adventure.Description.Length > 0)
            {
                page.Text(adventure.Description);
            }
            page.Gap().Text("Start opens its lobby, where each seat takes a character or makes one as it begins.");
        }
        else
        {
            page.Warn("It can't be played: " + picked.Problem);
        }
        View.SetEntry(page.ToString(), new[] { new DataAction("start", "Start", picked.Adventure != null, "It can't be read.") }, "");
        View.SetFoot("");
    }
}
