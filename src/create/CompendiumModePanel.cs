using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Compendium mode of Create, a layout over a CompendiumEditor: a data panel of every definition
/// in the package with a tab per kind, search, chips and sortable columns, and the picked entry's
/// form where the book page would be. The form is built from the kind's FormSchema, so a field
/// added to create/compendium.json shows here without code. Typing in a box is one undo step
/// until the box is left.
/// </summary>
public partial class CompendiumModePanel : Control
{
    private const string All = "All";
    private const string ChangedChip = "Changed";
    private const string ErrorChip = "Has errors";
    private const string ChapterChip = "Chapter's own";

    private static readonly DataColumn[] Columns = { new("Id", 150), new("Name", 150), new("Where", 110), new("Problems", 70, true) };

    private CreatePackage? _package;
    private CompendiumEditor? _editor;
    private DataPanel _list = null!;
    private ToolColumn _form = null!;
    private Label _error = null!;
    private string _newId = "";
    private string _hint = "";
    private int _rowsAt = -1;
    private List<DataRow> _rows = new();

    public override void _Ready()
    {
        _list = GD.Load<PackedScene>("res://scenes/hud/DataPanel.tscn").Instantiate<DataPanel>();
        AddChild(_list);
        _list.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _form = new ToolColumn { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_form);
        _list.UseForm(scroll, 420);
        _list.ActionPressed += Act;
        _error = new Label { ThemeTypeVariation = "WarnLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        AddChild(_error);
        _error.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide, LayoutPresetMode.Minsize, 16);
    }

    /// <summary>The package whose definitions are shown. Called every frame.</summary>
    public void Present(CreatePackage package)
    {
        _package = package;
        CompendiumEditor? editor = package.CompendiumEditor();
        if (!ReferenceEquals(editor, _editor))
        {
            _editor = editor;
            _rowsAt = -1;
            _newId = "";
            _hint = "";
            _list.Reset();
            _form.Invalidate();
        }
        _list.Visible = editor != null;
        _error.Visible = editor == null;
        _error.Text = package.CompendiumError.Length > 0 ? package.CompendiumError : "Nothing to edit.";
        if (editor == null)
        {
            return;
        }

        var tabs = new List<string> { All };
        tabs.AddRange(editor.Kinds.Select(LabelOf));
        _list.SetHead("Compendium", "the package's own definitions, edited through the forms in create/compendium.json");
        _list.SetSources(Array.Empty<(string, string)>(), "");
        _list.SetTabs(tabs);
        _list.SetChips(new[] { ChangedChip, ErrorChip, ChapterChip });
        _list.SetColumns(Columns);
        // problems run the game's readers, so the rows are only made again after an edit
        if (_rowsAt != package.History.Changes)
        {
            _rowsAt = package.History.Changes;
            _rows = Rows(editor);
        }
        _list.SetRows(_rows);

        int? picked = editor.Find(_list.Picked);
        CompendiumEditor.Kind? kind = TabKind(editor) ?? (picked is int p ? editor.KindOf(editor.Entries[p].Kind) : null);
        string folder = kind != null && package.CompendiumFolders.TryGetValue(kind.Form.Id, out string? f) ? f : "";
        var actions = new List<DataAction>
        {
            new("add", kind == null ? "Add" : "Add " + Singular(kind), kind != null && folder.Length > 0 && (_newId.Length == 0 || CompendiumEditor.ValidId(_newId)),
                kind == null ? "Pick a kind's tab first." : folder.Length == 0 ? "There is no ruleset folder in this package to add to." : "Ids are a-z, 0-9, - and _."),
            new("copy", "Copy", picked != null && (_newId.Length == 0 || CompendiumEditor.ValidId(_newId)), picked == null ? "Pick an entry to copy." : "Ids are a-z, 0-9, - and _."),
            new("foundry", "From Foundry...", package.CompendiumFolders.Count > 0, "There is no ruleset folder in this package to import into."),
        };
        _list.SetEntry("", actions, _hint);
        _list.SetFoot(folder.Length == 0 ? (kind == null ? "" : "No ruleset folder to add to") : $"New {Singular(kind!)} entries go in {folder}/");

        string signature = picked is int at
            ? $"{editor.Entries[at].Path}|{string.Join(",", editor.KindOf(editor.Entries[at].Kind)?.Form.Unlisted(editor.Entries[at].Value) ?? new List<string>())}"
            : "none";
        _form.Build(signature, () => BuildForm(editor, picked));
    }

    private static string LabelOf(CompendiumEditor.Kind kind) => kind.Form.Label.Length > 0 ? kind.Form.Label : kind.Form.Id;

    private static string Singular(CompendiumEditor.Kind kind) => kind.Form.Id;

    private CompendiumEditor.Kind? TabKind(CompendiumEditor editor) => editor.Kinds.FirstOrDefault(k => LabelOf(k) == _list.Tab);

    private List<DataRow> Rows(CompendiumEditor editor)
    {
        var rows = new List<DataRow>();
        HashSet<int> changed = editor.Changed().ToHashSet();
        for (int i = 0; i < editor.Entries.Count; i++)
        {
            CompendiumEditor.Entry entry = editor.Entries[i];
            CompendiumEditor.Kind? kind = editor.KindOf(entry.Kind);
            List<CompendiumEditor.Problem> problems = editor.Problems(i);
            int errors = problems.Count(p => p.Error);
            string folder = entry.Path[..Math.Max(0, entry.Path.LastIndexOf('/'))];
            // where it lives when it isn't the usual folder: a chapter's own, or a ruleset's
            string where = folder.StartsWith("chapters/", StringComparison.Ordinal) ? CreatePackage.Leaf(folder[..folder.LastIndexOf('/')])
                : kind?.Ruleset == true ? "ruleset" : "package";
            var tags = new HashSet<string>();
            if (kind != null)
            {
                tags.Add(LabelOf(kind));
            }
            if (changed.Contains(i))
            {
                tags.Add(ChangedChip);
            }
            if (errors > 0)
            {
                tags.Add(ErrorChip);
            }
            if (folder.StartsWith("chapters/", StringComparison.Ordinal))
            {
                tags.Add(ChapterChip);
            }
            string name = FormJson.IsString(entry.Value["name"], out string? n) ? n : "";
            rows.Add(new DataRow
            {
                Key = entry.Path,
                Cells = new[] { entry.Id + (changed.Contains(i) ? " *" : ""), name, where, problems.Count == 0 ? "" : errors > 0 ? $"{errors} err" : $"{problems.Count}" },
                Sort = new IComparable?[] { entry.Id, name, where, errors * 1000 + problems.Count },
                Tags = tags,
                Search = entry.Path,
            });
        }
        return rows;
    }

    private void Act(string id)
    {
        if (_editor == null || _package == null)
        {
            return;
        }
        int? picked = _editor.Find(_list.Picked);
        CompendiumEditor.Kind? kind = TabKind(_editor) ?? (picked is int p ? _editor.KindOf(_editor.Entries[p].Kind) : null);
        if (id == "foundry")
        {
            PickFoundryFile();
            return;
        }
        int? made = null;
        if (id == "add" && kind != null && _package.CompendiumFolders.TryGetValue(kind.Form.Id, out string? folder))
        {
            made = _editor.Add(kind.Form.Id, folder, _newId);
        }
        else if (id == "copy" && picked is int from)
        {
            made = _editor.Copy(from, _newId);
        }
        if (made is int entry)
        {
            _newId = "";
            _hint = "";
            _list.Pick(_editor.Entries[entry].Path);
        }
        else
        {
            _hint = "That id is taken.";
        }
    }

    // A Foundry export (an Item or Actor's JSON, or a list of them) chosen from disk.
    private void PickFoundryFile()
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true,
            Filters = new[] { "*.json ; Foundry export" }, Title = "A Foundry VTT export",
        };
        dialog.FileSelected += path =>
        {
            ImportFoundry(System.IO.File.ReadAllText(path));
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(900, 600));
    }

    // What the import made, into the folders the package keeps each kind in; what didn't fit goes to the log.
    private void ImportFoundry(string json)
    {
        if (_editor == null || _package == null)
        {
            return;
        }
        FoundryImport import = FoundryImport.Read(json);
        var entries = new List<(string, string, System.Text.Json.Nodes.JsonObject)>();
        foreach ((string path, System.Text.Json.Nodes.JsonObject value) in import.Files)
        {
            string folder = path[..path.IndexOf('/')];
            if (_editor.Kinds.FirstOrDefault(k => k.Form.Folder == folder) is CompendiumEditor.Kind kind
                && _package.CompendiumFolders.TryGetValue(kind.Form.Id, out string? into))
            {
                entries.Add((kind.Form.Id, into, value));
            }
            else
            {
                import.Report.Add($"{path}: this package has no folder for it");
            }
        }
        List<string> skipped = _editor.AddAll(entries);
        foreach (string line in import.Report.Concat(skipped.Select(p => p + ": that id is taken; left as it was")))
        {
            GD.Print("Foundry import: " + line);
        }
        int notes = import.Report.Count + skipped.Count;
        _hint = $"Brought in {entries.Count - skipped.Count} from Foundry" + (notes > 0 ? $"; {notes} notes in the log" : "") + ". Undo takes them back.";
    }

    private void BuildForm(CompendiumEditor editor, int? picked)
    {
        if (picked is not int index || editor.KindOf(editor.Entries[index].Kind) is not CompendiumEditor.Kind kind)
        {
            _form.Heading("Nothing picked");
            _form.Dim("Nothing of this kind here yet. Type an id under New id and press Add.");
            NewId();
            return;
        }
        string path = editor.Entries[index].Path;
        // read again on every call: an edit replaces the entry
        JsonObject Value() => editor.Find(path) is int at ? editor.Entries[at].Value : new JsonObject();
        int At() => editor.Find(path) ?? -1;

        HBoxContainer head = _form.Row();
        _form.Heading(editor.Entries[index].Id, head);
        Label where = _form.Dim(path, head);
        where.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        where.HorizontalAlignment = HorizontalAlignment.Right;
        where.AutowrapMode = TextServer.AutowrapMode.Off;
        where.ClipText = true;
        _form.Dim(LabelOf(kind) + (kind.Reader.Length > 0 ? $", checked by the game's {kind.Reader} reader" : ""));
        _form.Gap(4);

        Dictionary<string, List<string>> lists = editor.Options();
        foreach (FormField field in kind.Form.Fields)
        {
            HBoxContainer row = _form.Row();
            Label title = _form.Dim(field.Title + (field.Required ? " *" : ""), row);
            title.CustomMinimumSize = new Vector2(130, 0);
            title.AutowrapMode = TextServer.AutowrapMode.Off;
            title.ClipText = true;
            if (field.Required)
            {
                title.ThemeTypeVariation = "";
            }
            List<string> choices = field.Type == FormField.Kind.Choice ? field.Choices(lists) : new List<string>();
            if (field.Type == FormField.Kind.Flag)
            {
                _form.Toggle("yes", () => Value()[field.Key]?.GetValueKind() == System.Text.Json.JsonValueKind.True, () =>
                {
                    bool on = Value()[field.Key]?.GetValueKind() == System.Text.Json.JsonValueKind.True;
                    editor.EndTyping();
                    editor.SetValue(At(), field.Key, JsonValue.Create(!on));
                    editor.EndTyping();
                }, row, "ChipButton");
                Label state = _form.Live(() => Value().ContainsKey(field.Key) ? "" : "not set", "DimLabel", row);
                state.AutowrapMode = TextServer.AutowrapMode.Off;
                state.CustomMinimumSize = new Vector2(60, 0);
                ToolColumn.Narrow(_form.Act("Clear", () =>
                {
                    editor.EndTyping();
                    editor.SetValue(At(), field.Key, null);
                    editor.EndTyping();
                }, () => Value().ContainsKey(field.Key) && !field.Required, row), 60);
            }
            else if (choices.Count > 0)
            {
                // steps through what can be picked; "(none)" leaves it out when it isn't required
                List<string> names = choices.ToList();
                if (!field.Required && !names.Contains(""))
                {
                    names.Insert(0, "");
                }
                void Step(int by)
                {
                    string current = field.Text(Value());
                    int place = names.IndexOf(current);
                    string next = place < 0 ? names[0] : names[((place + by) % names.Count + names.Count) % names.Count];
                    editor.EndTyping();
                    _hint = editor.SetField(At(), field.Key, next, out string why) ? "" : why;
                    editor.EndTyping();
                }
                ToolColumn.Narrow(_form.Act("<", () => Step(-1), null, row), 28);
                Label shown = _form.Live(() =>
                {
                    string current = field.Text(Value());
                    return current.Length == 0 ? "(none)" : names.Contains(current) ? current : current + "  (not offered)";
                }, "", row);
                shown.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                shown.HorizontalAlignment = HorizontalAlignment.Center;
                shown.AutowrapMode = TextServer.AutowrapMode.Off;
                shown.ClipText = true;
                ToolColumn.Narrow(_form.Act(">", () => Step(1), null, row), 28);
            }
            else
            {
                _form.Field(() => field.Text(Value()), typed =>
                {
                    if (typed == field.Text(Value()))
                    {
                        return;
                    }
                    _hint = editor.SetField(At(), field.Key, typed, out string why) ? "" : why;
                }, editor.EndTyping, field.Type == FormField.Kind.Json ? "JSON" : field.Type == FormField.Kind.List ? "a, b, c" : "", row);
            }
            if (field.Help.Length > 0)
            {
                Label help = _form.Dim(field.Help);
                help.AddThemeFontSizeOverride("font_size", 12);
            }
        }

        List<string> others = kind.Form.Unlisted(Value());
        if (others.Count > 0)
        {
            _form.Gap();
            _form.Heading("Other fields, kept as written");
            foreach (string key in others)
            {
                HBoxContainer row = _form.Row();
                Label name = _form.Dim(key, row);
                name.CustomMinimumSize = new Vector2(130, 0);
                name.AutowrapMode = TextServer.AutowrapMode.Off;
                Label value = _form.Live(() => Value().TryGetPropertyValue(key, out JsonNode? v) ? CreateJson.Compact(v) : "", "DimLabel", row);
                value.AutowrapMode = TextServer.AutowrapMode.Off;
                value.ClipText = true;
                value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            }
        }

        _form.Gap();
        _form.Heading("Problems");
        string found = "";
        _form.Live(() =>
        {
            List<CompendiumEditor.Problem> problems = At() >= 0 ? editor.Problems(At()) : new();
            found = string.Join("\n", problems.Select(p => (p.Error ? "Error: " : "Look: ") + p.Text));
            return found;
        }, "WarnLabel");
        _form.Live(() => found.Length == 0 ? "Nothing wrong." : "");
        NewId();
    }

    private void NewId()
    {
        _form.Gap();
        HBoxContainer row = _form.Row();
        Label title = _form.Dim("New id", row);
        title.CustomMinimumSize = new Vector2(130, 0);
        title.AutowrapMode = TextServer.AutowrapMode.Off;
        _form.Field(() => _newId, typed =>
        {
            _newId = typed.Trim();
            _hint = _newId.Length == 0 || CompendiumEditor.ValidId(_newId) ? "" : "Ids are a-z, 0-9, - and _.";
        }, null, "blank for new-kind", row);
    }
}
