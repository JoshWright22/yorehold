using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Create > Import: pick a book (a PDF, a .txt or a .md), watch it read, then review the outline
/// on the start screen's panel: a tab per kind, the entry as a book page with where its words came
/// from, Keep or Drop on each, and Build to write the package and open it in the other modes. The
/// story model is the one named in the settings (or YOREHOLD_IMPORT_MODEL); with none, only what
/// the book's layout gives is drafted.
/// </summary>
public partial class CreateScreen
{
    private static readonly DataColumn[] ImportColumns = { new("Entry", 240), new("Kind", 90), new("Page", 50, true) };
    private static readonly OutlineKind[] ImportTabs =
    {
        OutlineKind.Place, OutlineKind.Link, OutlineKind.Encounter, OutlineKind.Creature, OutlineKind.Item, OutlineKind.Hero,
        OutlineKind.Npc, OutlineKind.Dialogue, OutlineKind.Quest, OutlineKind.Container, OutlineKind.Note,
    };

    private StoryImport? _import;
    private Task? _importTask;
    private bool _buildWhenRead;
    private FileDialog? _bookDialog;
    private readonly List<string> _imports = new();

    /// <summary>The story model the settings name, or the environment's; null when there is none.</summary>
    private static IStoryModel? StoryModel()
    {
        string address = System.Environment.GetEnvironmentVariable("YOREHOLD_IMPORT_MODEL") is { Length: > 0 } fromEnvironment ? fromEnvironment : App.Settings.StoryModel;
        if (address.Length == 0)
        {
            return null;
        }
        string name = System.Environment.GetEnvironmentVariable("YOREHOLD_IMPORT_MODEL_NAME") is { Length: > 0 } named ? named : App.Settings.StoryModelName;
        return new ChatModel(address, name.Length > 0 ? name : "default");
    }

    /// <summary>
    /// Reads a book into a new package. With build, it is built as soon as it is read and opened,
    /// with no review: how `-- --import file` runs it for check runs.
    /// </summary>
    public void Import(string book, bool build = false)
    {
        if (_importTask is { IsCompleted: false })
        {
            return;
        }
        if (!BookReader.CanRead(book))
        {
            _startNote = "Only a .pdf, .txt or .md book can be imported.";
            return;
        }
        string folder = StoryImport.FolderFor(Places.CreateFolder(), book);
        _import = new StoryImport(folder, App.Content());
        _buildWhenRead = build;
        _startNote = "";
        _start.Reset();
        _importTask = _import.Read(book, StoryModel());
    }

    private void PickBook()
    {
        if (_bookDialog == null)
        {
            _bookDialog = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Filters = new[] { "*.pdf, *.txt, *.md ; Books" },
                Title = "Import a book",
                UseNativeDialog = true,
            };
            _bookDialog.FileSelected += path => Import(path);
            AddChild(_bookDialog);
        }
        _bookDialog.PopupCentered(new Vector2I(900, 600));
    }

    private void ReviewImport(string folder)
    {
        try
        {
            _import = StoryImport.Open(folder, App.Content());
            _importTask = null;
            _start.Reset();
        }
        catch (Exception error) when (error is ContentException or IOException)
        {
            _startNote = "That import can't be read: " + error.Message;
        }
    }

    private void CloseImport()
    {
        _import = null;
        _importTask = null;
        _start.Reset();
        FindPackages();
    }

    // the review, or what the reading is doing
    private void FillImport()
    {
        StoryImport import = _import!;
        _start.SetHead("Import", "a book made into an adventure");
        _start.SetSources(Array.Empty<(string, string)>(), "");
        _start.SetColumns(ImportColumns);
        if (_importTask is { IsCompleted: false } || import.Outline == null)
        {
            _start.SetTabs(Array.Empty<string>());
            _start.SetChips(Array.Empty<string>());
            _start.SetRows(Array.Empty<DataRow>());
            string failed = _importTask is { IsFaulted: true } task ? task.Exception?.GetBaseException().Message ?? "it stopped" : "";
            var waiting = new BookPage().Title(Path.GetFileName(import.Package)).Sub(failed.Length > 0 ? "couldn't be read" : import.Stage).Rule();
            if (failed.Length > 0)
            {
                waiting.Warn(failed);
            }
            else
            {
                waiting.Text(StoryModel() == null
                    ? "No story model is set, so only what the book's layout shows is drafted: its numbered places, the passages to read out and the map."
                    : "The story model reads the book a few pages at a time. A book of twenty pages takes a few minutes.");
            }
            _start.SetEntry(waiting.Gap().Note(import.Package).ToString(), new[] { new DataAction("import-close", "Close") }, "");
            _start.SetFoot(import.Folder);
            return;
        }
        if (_buildWhenRead)
        {
            _buildWhenRead = false;
            BuildImport();
            return;
        }

        Outline outline = import.Outline;
        var tabs = new List<string> { "All" };
        tabs.AddRange(ImportTabs.Where(k => outline.OfKind(k).Any()).Select(KindTab));
        _start.SetTabs(tabs);
        _start.SetChips(new[] { "From the book", "Invented", "Dropped" });
        var rows = new List<DataRow>();
        foreach (OutlineEntry entry in outline.Entries)
        {
            bool dropped = import.Dropped.Contains(entry.Id);
            var tags = new HashSet<string> { KindTab(entry.Kind), entry.From.IsInvented ? "Invented" : "From the book" };
            if (dropped)
            {
                tags.Add("Dropped");
            }
            rows.Add(new DataRow
            {
                Key = entry.Id,
                Cells = new[] { Called(entry), Outline.KindName(entry.Kind), entry.From.IsInvented ? "" : entry.From.Page.ToString() },
                Sort = new IComparable?[] { Called(entry), Outline.KindName(entry.Kind), entry.From.IsInvented ? 0 : entry.From.Page },
                Tags = tags,
                Search = entry.Id + " " + entry.Data.ToJsonString(),
                Dim = dropped,
            });
        }
        _start.SetRows(rows);

        var actions = new List<DataAction>();
        var page = new BookPage();
        OutlineEntry? picked = outline.Find(_start.Picked);
        if (picked == null)
        {
            page.Title(outline.Title.Length > 0 ? outline.Title : "The outline").Sub("ready to review").Rule()
                .Stats(("Entries", outline.Entries.Count.ToString()), ("Places", outline.OfKind(OutlineKind.Place).Count().ToString()),
                    ("Fights", outline.OfKind(OutlineKind.Encounter).Count().ToString()), ("Dropped", import.Dropped.Count.ToString()))
                .Text("Pick an entry to see it and where its words came from. Drop what shouldn't be in the game; Build writes the rest as an adventure and opens it.");
        }
        else
        {
            Describe(page, picked, import.Dropped.Contains(picked.Id));
            actions.Add(import.Dropped.Contains(picked.Id) ? new DataAction("import-keep", "Keep") : new DataAction("import-drop", "Drop"));
        }
        actions.Add(new DataAction("import-build", "Build"));
        actions.Add(new DataAction("import-close", "Close"));
        _start.SetEntry(page.ToString(), actions, import.Problems.Count > 0 ? "Couldn't build: " + string.Join("; ", import.Problems) : "");
        _start.SetFoot(import.Folder);
    }

    private static string KindTab(OutlineKind kind) => kind switch
    {
        OutlineKind.Place => "Places",
        OutlineKind.Link => "Ways",
        OutlineKind.Encounter => "Fights",
        OutlineKind.Creature => "Creatures",
        OutlineKind.Item => "Items",
        OutlineKind.Hero => "Heroes",
        OutlineKind.Npc => "People",
        OutlineKind.Dialogue => "Talk",
        OutlineKind.Quest => "Quests",
        OutlineKind.Container => "Chests",
        OutlineKind.Note => "Notes",
        _ => "Other",
    };

    private static string Called(OutlineEntry entry)
    {
        string name = entry.Text("name") is { Length: > 0 } n ? n : entry.Text("title") is { Length: > 0 } t ? t : entry.Text("text") is { Length: > 0 } x ? x : entry.Id;
        if (entry.Kind == OutlineKind.Place && entry.Text("label") is { Length: > 0 } label)
        {
            name = $"{label}: {name}";
        }
        else if (entry.Kind == OutlineKind.Link)
        {
            name = $"{entry.Text("from")} to {entry.Text("to")} ({entry.Text("way", "open")})";
        }
        return name.Length > 80 ? name[..80] + "..." : name;
    }

    // the entry as a book page: what it is, where its words came from, then its fields
    private static void Describe(BookPage page, OutlineEntry entry, bool dropped)
    {
        page.Title(Called(entry)).Sub(Outline.KindName(entry.Kind) + (dropped ? ", dropped" : "")).Rule();
        if (entry.From.IsInvented)
        {
            page.Warn("Invented: the book doesn't say this. Check it before it goes in.");
        }
        else
        {
            page.Entry($"Page {entry.From.Page}", "“" + entry.From.Quote + "”");
        }
        if (entry.Picture.Length > 0)
        {
            page.Entry("Picture", entry.Picture);
        }
        foreach (string passage in entry.Texts("readAloud"))
        {
            page.Heading("Read out").Text(passage);
        }
        page.Heading("In the game");
        foreach (KeyValuePair<string, System.Text.Json.Nodes.JsonNode?> field in entry.Data)
        {
            if (field.Key is "readAloud" or "name" or "title")
            {
                continue;
            }
            string value = field.Value?.ToJsonString() ?? "";
            if (field.Value is System.Text.Json.Nodes.JsonValue text && text.TryGetValue(out string? words))
            {
                value = words;
            }
            page.Entry(field.Key, value.Length > 400 ? value[..400] + "..." : value);
        }
        page.Gap().Note(entry.Id);
    }

    private void ImportAction(string id)
    {
        if (_import == null)
        {
            return;
        }
        switch (id)
        {
            case "import-drop":
            case "import-keep":
                _import.SetDropped(_start.Picked, id == "import-drop");
                break;
            case "import-build":
                BuildImport();
                break;
            case "import-close":
                CloseImport();
                break;
        }
    }

    private void BuildImport()
    {
        StoryImport import = _import!;
        List<string> problems = import.Build();
        GD.Print(problems.Count == 0 ? $"Imported into {import.Package}" : "Import couldn't build: " + string.Join("; ", problems));
        if (problems.Count == 0)
        {
            _import = null;
            _importTask = null;
            Open(import.Package);
        }
    }
}
