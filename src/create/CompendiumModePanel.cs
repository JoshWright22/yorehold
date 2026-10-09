using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Compendium mode of Create, a layout over a CompendiumEditor, as the design draws it: the kinds
/// as tabs over a list of what this adventure made and what it uses from the game, the picked
/// entry's form in the middle, and the page players will see on the right. The form is built from
/// the kind's FormSchema, so a field added to create/compendium.json shows here without code.
/// Typing in a box is one undo step until the box is left.
/// </summary>
public partial class CompendiumModePanel : Control
{
    private CreatePackage? _package;
    private CompendiumEditor? _editor;
    private ToolColumn _list = null!;
    private ToolColumn _form = null!;
    private RichTextLabel _page = null!;
    private Label _error = null!;
    private HBoxContainer _body = null!;
    private string _kind = "";
    // the entry being edited (its path), or one of the game's own being looked at (its id)
    private string _picked = "";
    private string _elsewhere = "";
    private string _search = "";
    private string _hint = "";
    private string _pageShown = "";

    public override void _Ready()
    {
        _body = new HBoxContainer();
        _body.AddThemeConstantOverride("separation", 0);
        AddChild(_body);
        _body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _list = Column(250, Palette.Night, 0, 1);
        var middle = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var middlePanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        middlePanel.AddThemeStyleboxOverride("panel", Box(Palette.Night, 0, 0, 16));
        middlePanel.AddChild(middle);
        _body.AddChild(middlePanel);
        _form = new ToolColumn { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        middle.AddChild(_form);

        var right = new PanelContainer { CustomMinimumSize = new Vector2(320, 0) };
        StyleBoxFlat rightBox = Box(Palette.Ink, 1, 0, 16);
        // room on its right for the chat's tab, which stays at the screen's edge
        rightBox.ContentMarginRight = 44;
        right.AddThemeStyleboxOverride("panel", rightBox);
        _body.AddChild(right);
        var rightRows = new VBoxContainer();
        right.AddChild(rightRows);
        rightRows.AddChild(new Label { Text = "HOW PLAYERS SEE IT", ThemeTypeVariation = "CapsLabel" });
        _page = new RichTextLabel { ThemeTypeVariation = "PageText", BbcodeEnabled = true, ScrollActive = true, SizeFlagsVertical = SizeFlags.ExpandFill };
        rightRows.AddChild(_page);

        _error = new Label { ThemeTypeVariation = "WarnLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        AddChild(_error);
        _error.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide, LayoutPresetMode.Minsize, 16);
    }

    private ToolColumn Column(float width, Color fill, int left, int right)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
        StyleBoxFlat box = Box(fill, left, right, 8);
        panel.AddThemeStyleboxOverride("panel", box);
        _body.AddChild(panel);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        var column = new ToolColumn { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(column);
        return column;
    }

    private static StyleBoxFlat Box(Color fill, int left, int right, int margin)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = Palette.Iron, BorderWidthLeft = left, BorderWidthRight = right };
        box.SetContentMarginAll(margin);
        return box;
    }

    /// <summary>The package whose definitions are shown. Called every frame.</summary>
    public void Present(CreatePackage package)
    {
        _package = package;
        CompendiumEditor? editor = package.CompendiumEditor();
        if (!ReferenceEquals(editor, _editor))
        {
            _editor = editor;
            _picked = "";
            _elsewhere = "";
            _hint = "";
            _kind = editor?.Kinds.FirstOrDefault(k => k.Form.Id == "creature")?.Form.Id ?? editor?.Kinds.FirstOrDefault()?.Form.Id ?? "";
            _list.Invalidate();
            _form.Invalidate();
        }
        _body.Visible = editor != null;
        _error.Visible = editor == null;
        _error.Text = package.CompendiumError.Length > 0 ? package.CompendiumError : "Nothing to edit.";
        if (editor == null)
        {
            return;
        }
        // an undo can take away the entry that was picked
        if (_picked.Length > 0 && editor.Find(_picked) == null)
        {
            _picked = "";
        }
        int changes = package.History.Changes;
        _list.Build($"{_kind}|{_search}|{_picked}|{_elsewhere}|{changes}", () => BuildList(editor, package));
        int? picked = editor.Find(_picked);
        string signature = picked is int at
            ? $"{editor.Entries[at].Path}|{string.Join(",", editor.KindOf(editor.Entries[at].Kind)?.Form.Unlisted(editor.Entries[at].Value) ?? new List<string>())}"
            : $"elsewhere:{_kind}:{_elsewhere}";
        _form.Build(signature, () => BuildForm(editor, package, picked));
        ShowPage(editor, package, picked);
    }

    private CompendiumEditor.Kind? Kind(CompendiumEditor editor) => editor.KindOf(_kind);

    private static string LabelOf(CompendiumEditor.Kind kind) => kind.Form.Label.Length > 0 ? kind.Form.Label : kind.Form.Id;

    private static string NameOf(JsonObject value, string id) => FormJson.IsString(value["name"], out string? name) && name.Length > 0 ? name : id;

    // ---------------------------------------------------------------- the list

    private void BuildList(CompendiumEditor editor, CreatePackage package)
    {
        // the kinds as tabs, as many rows as they need
        var tabs = new HFlowContainer();
        tabs.AddThemeConstantOverride("h_separation", 2);
        tabs.AddThemeConstantOverride("v_separation", 2);
        _list.AddChild(tabs);
        foreach (CompendiumEditor.Kind kind in editor.Kinds)
        {
            string id = kind.Form.Id;
            Button tab = _list.Toggle(LabelOf(kind), () => _kind == id, () =>
            {
                _kind = id;
                _picked = "";
                _elsewhere = "";
            }, tabs, "TabButton");
            tab.SizeFlagsHorizontal = SizeFlags.Fill;
            tab.Alignment = HorizontalAlignment.Center;
            // a tab is as wide as its name in the flow
            tab.ClipText = false;
            tab.CustomMinimumSize = new Vector2(0, 26);
        }
        _list.Gap(6);

        HBoxContainer find = _list.Row();
        var search = new LineEdit { Text = _search, PlaceholderText = "Search, or a new id", ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 30) };
        search.TextChanged += typed => _search = typed;
        find.AddChild(search);
        if (_search.Length > 0)
        {
            // keep typing where it was after the list is made again
            search.CallDeferred(Control.MethodName.GrabFocus);
            search.CaretColumn = _search.Length;
        }
        ToolColumn.Narrow(_list.Act("New", () => New(editor, package), () => Kind(editor) != null && package.CompendiumFolders.ContainsKey(_kind), find), 52);
        _list.Gap(8);

        CompendiumEditor.Kind? current = Kind(editor);
        if (current == null)
        {
            return;
        }
        bool Shown(string id, string name) => _search.Length == 0 || id.Contains(_search, StringComparison.OrdinalIgnoreCase) || name.Contains(_search, StringComparison.OrdinalIgnoreCase);
        HashSet<int> changed = editor.Changed().ToHashSet();
        var own = editor.Of(_kind).Where(i => Shown(editor.Entries[i].Id, NameOf(editor.Entries[i].Value, editor.Entries[i].Id))).ToList();
        _list.AddChild(new Label { Text = "MADE FOR THIS ADVENTURE", ThemeTypeVariation = "CapsLabel" });
        foreach (int i in own)
        {
            CompendiumEditor.Entry entry = editor.Entries[i];
            int errors = editor.Problems(i).Count(p => p.Error);
            string mark = errors > 0 ? $"{errors} err" : changed.Contains(i) ? "changed" : Meta(entry.Value);
            string path = entry.Path;
            ListRow(NameOf(entry.Value, entry.Id), mark, errors > 0 ? Palette.Red : Palette.Ash, _picked == path, true, () =>
            {
                _picked = path;
                _elsewhere = "";
            });
        }
        if (own.Count == 0)
        {
            _list.Dim(_search.Length > 0 ? "None match." : "None yet: New makes one.");
        }
        _list.Gap(8);

        // the game's own of this kind that the package hasn't one of its own for
        var made = editor.Of(_kind).Select(i => editor.Entries[i].Id).ToHashSet(StringComparer.Ordinal);
        List<string> game = editor.Options().GetValueOrDefault(current.Form.Folder, new List<string>()).Where(id => !made.Contains(id) && Shown(id, id)).ToList();
        if (game.Count > 0)
        {
            _list.AddChild(new Label { Text = "USED FROM ELSEWHERE", ThemeTypeVariation = "CapsLabel" });
            foreach (string id in game.Take(200))
            {
                string picked = id;
                ListRow(id, "Built-in", Palette.Ash, _elsewhere == id && _picked.Length == 0, false, () =>
                {
                    _picked = "";
                    _elsewhere = picked;
                });
            }
        }
        _list.Gap(12);
        _list.Act("Copy the picked one", () => Copy(editor), () => _picked.Length > 0);
        _list.Act("From Foundry...", PickFoundryFile, () => package.CompendiumFolders.Count > 0);
        _list.Live(() => _hint, "WarnLabel");
    }

    // A row of the list: a name to pick, and a short note on the right (its level, "Built-in", a problem).
    private void ListRow(string name, string note, Color noteColor, bool picked, bool own, Action pick)
    {
        HBoxContainer row = _list.Row();
        var button = new Button { Text = name, Flat = true, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.AddThemeColorOverride("font_color", picked ? Palette.Straw : own ? Palette.Bone : Palette.Ash);
        button.AddThemeColorOverride("font_hover_color", Palette.Bone);
        if (picked)
        {
            var mark = new StyleBoxFlat { BgColor = Palette.Dusk, BorderColor = Palette.Straw, BorderWidthLeft = 2 };
            mark.SetContentMarginAll(4);
            button.AddThemeStyleboxOverride("normal", mark);
            button.AddThemeStyleboxOverride("hover", mark);
        }
        button.Pressed += pick;
        row.AddChild(button);
        var meta = new Label { Text = note, ThemeTypeVariation = "NumberLabel", VerticalAlignment = VerticalAlignment.Center };
        meta.AddThemeFontSizeOverride("font_size", 12);
        meta.AddThemeColorOverride("font_color", noteColor);
        row.AddChild(meta);
    }

    // What the list says beside an entry: its level, if it has one.
    private static string Meta(JsonObject value) =>
        value["level"] is JsonValue level && level.TryGetValue(out double n) ? "L" + n.ToString(CultureInfo.InvariantCulture) : "";

    private void New(CompendiumEditor editor, CreatePackage package)
    {
        if (!package.CompendiumFolders.TryGetValue(_kind, out string? folder))
        {
            return;
        }
        // what was typed in the search box names it, when it can be an id
        string id = FoundryImport.Slug(_search.Trim());
        int? made = editor.Add(_kind, folder, _search.Trim().Length > 0 && CompendiumEditor.ValidId(id) ? id : "");
        if (made is int entry)
        {
            _picked = editor.Entries[entry].Path;
            _elsewhere = "";
            _search = "";
            _hint = "";
        }
        else
        {
            _hint = "That id is taken.";
        }
    }

    private void Copy(CompendiumEditor editor)
    {
        if (editor.Find(_picked) is int from && editor.Copy(from) is int made)
        {
            _picked = editor.Entries[made].Path;
            _hint = "";
        }
    }

    // One of the game's own brought into this adventure to change, as one undoable change.
    private void CopyIn(CompendiumEditor editor, CreatePackage package)
    {
        if (package.GameEntry(_kind, _elsewhere) is not JsonObject value || !package.CompendiumFolders.TryGetValue(_kind, out string? folder))
        {
            _hint = "That one can't be read.";
            return;
        }
        if (editor.AddAll(new[] { (_kind, folder, value) }).Count > 0)
        {
            _hint = "There is one with that id already.";
            return;
        }
        _picked = editor.Entries.LastOrDefault(e => e.Kind == _kind && e.Id == _elsewhere)?.Path ?? "";
        _elsewhere = "";
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
        var entries = new List<(string, string, JsonObject)>();
        foreach ((string path, JsonObject value) in import.Files)
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

    // ---------------------------------------------------------------- the form

    private void BuildForm(CompendiumEditor editor, CreatePackage package, int? picked)
    {
        if (picked is not int index || editor.KindOf(editor.Entries[index].Kind) is not CompendiumEditor.Kind kind)
        {
            if (_elsewhere.Length > 0)
            {
                _form.Heading(_elsewhere);
                _form.Dim("Built into the game. Bring a copy into this adventure to change it here; the game's own stays as it is.");
                _form.Gap(6);
                _form.Act("Copy into this adventure", () => CopyIn(editor, package));
                return;
            }
            _form.Heading("Nothing picked");
            _form.Dim("Pick one on the left, or type an id in the search box and press New.");
            return;
        }
        string path = editor.Entries[index].Path;
        // read again on every call: an edit replaces the entry
        JsonObject Value() => editor.Find(path) is int at ? editor.Entries[at].Value : new JsonObject();
        int At() => editor.Find(path) ?? -1;

        HBoxContainer head = _form.Row();
        _form.Heading(NameOf(editor.Entries[index].Value, editor.Entries[index].Id), head);
        Label where = _form.Dim(path, head);
        where.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        where.HorizontalAlignment = HorizontalAlignment.Right;
        where.AutowrapMode = TextServer.AutowrapMode.Off;
        where.ClipText = true;
        _form.Gap(4);

        // short fields side by side, as the design's rows of boxes; long ones the whole width under them
        Dictionary<string, List<string>> lists = editor.Options();
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 8);
        _form.AddChild(grid);
        var wide = new List<FormField>();
        foreach (FormField field in kind.Form.Fields)
        {
            if (field.Type is FormField.Kind.Json or FormField.Kind.List || field.Key == "description")
            {
                wide.Add(field);
                continue;
            }
            Field(editor, field, lists, Value, At, Labelled(grid, field));
        }
        foreach (FormField field in wide)
        {
            _form.Gap(4);
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 2);
            _form.AddChild(box);
            box.AddChild(Title(field));
            if (field.Key == "description")
            {
                Description(editor, field, Value, At, box);
            }
            else
            {
                Field(editor, field, lists, Value, At, box);
            }
            Help(field, box);
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
        string found = "";
        _form.Live(() =>
        {
            List<CompendiumEditor.Problem> problems = At() >= 0 ? editor.Problems(At()) : new();
            found = string.Join("\n", problems.Select(p => (p.Error ? "Error: " : "Look: ") + p.Text));
            return found;
        }, "WarnLabel");
        _form.Live(() => found.Length == 0 ? (kind.Reader.Length > 0 ? $"Nothing wrong: the game's {kind.Reader} reader takes it." : "Nothing wrong.") : "");
    }

    // A field's name in plain words: its label, else its key spread out ("armorClass" is "Armor class").
    private static string Plain(FormField field)
    {
        string words = field.Label.Length > 0 ? field.Label
            : string.Concat(field.Key.Select((c, i) => char.IsUpper(c) && i > 0 ? " " + char.ToLowerInvariant(c) : c.ToString()));
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static Label Title(FormField field)
    {
        var title = new Label { Text = Plain(field) + (field.Required ? " *" : ""), ThemeTypeVariation = field.Required ? "" : "DimLabel", AutowrapMode = TextServer.AutowrapMode.Off, ClipText = true };
        title.AddThemeFontSizeOverride("font_size", 13);
        return title;
    }

    private static void Help(FormField field, Container into)
    {
        if (field.Help.Length > 0)
        {
            var help = new Label { Text = field.Help, ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            help.AddThemeFontSizeOverride("font_size", 12);
            into.AddChild(help);
        }
    }

    // A field's box in the grid: its name over it, its help under it.
    private static VBoxContainer Labelled(GridContainer grid, FormField field)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(150, 0) };
        box.AddThemeConstantOverride("separation", 2);
        grid.AddChild(box);
        box.AddChild(Title(field));
        return box;
    }

    private void Field(CompendiumEditor editor, FormField field, Dictionary<string, List<string>> lists, Func<JsonObject> Value, Func<int> At, VBoxContainer box)
    {
        List<string> choices = field.Type == FormField.Kind.Choice ? field.Choices(lists) : new List<string>();
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 3);
        box.AddChild(row);
        if (field.Type == FormField.Kind.Flag)
        {
            _form.Toggle("yes", () => Value()[field.Key]?.GetValueKind() == JsonValueKind.True, () =>
            {
                bool on = Value()[field.Key]?.GetValueKind() == JsonValueKind.True;
                editor.EndTyping();
                editor.SetValue(At(), field.Key, JsonValue.Create(!on));
                editor.EndTyping();
            }, row, "ChipButton");
            ToolColumn.Narrow(_form.Act("Clear", () =>
            {
                editor.EndTyping();
                editor.SetValue(At(), field.Key, null);
                editor.EndTyping();
            }, () => Value().ContainsKey(field.Key) && !field.Required, row), 56);
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
            ToolColumn.Narrow(_form.Act("<", () => Step(-1), null, row), 26);
            Label shown = _form.Live(() =>
            {
                string current = field.Text(Value());
                return current.Length == 0 ? "(none)" : names.Contains(current) ? current : current + " (not offered)";
            }, "", row);
            shown.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            shown.HorizontalAlignment = HorizontalAlignment.Center;
            shown.AutowrapMode = TextServer.AutowrapMode.Off;
            shown.ClipText = true;
            ToolColumn.Narrow(_form.Act(">", () => Step(1), null, row), 26);
        }
        else if (field.Type is FormField.Kind.Integer)
        {
            // a whole number steps by one either side of its box, as the design's ability boxes do
            void Step(int by)
            {
                string current = field.Text(Value());
                double now = double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : field.Min ?? 0;
                editor.EndTyping();
                _hint = editor.SetField(At(), field.Key, (now + by).ToString(CultureInfo.InvariantCulture), out string why) ? "" : why;
                editor.EndTyping();
            }
            ToolColumn.Narrow(_form.Act("-", () => Step(-1), null, row), 26);
            NumberBox(editor, field, Value, At, row);
            ToolColumn.Narrow(_form.Act("+", () => Step(1), null, row), 26);
        }
        else
        {
            NumberBox(editor, field, Value, At, row);
        }
        if (box.GetParent() is GridContainer)
        {
            Help(field, box);
        }
    }

    private void NumberBox(CompendiumEditor editor, FormField field, Func<JsonObject> Value, Func<int> At, HBoxContainer row)
    {
        LineEdit box = _form.Field(() => field.Text(Value()), typed =>
        {
            if (typed == field.Text(Value()))
            {
                return;
            }
            _hint = editor.SetField(At(), field.Key, typed, out string why) ? "" : why;
        }, editor.EndTyping, field.Type == FormField.Kind.Json ? "JSON" : field.Type == FormField.Kind.List ? "a, b, c" : "", row);
        if (field.Type is FormField.Kind.Integer or FormField.Kind.Number)
        {
            box.AddThemeFontOverride("font", GetThemeFont("font", "NumberLabel"));
            box.Alignment = HorizontalAlignment.Center;
        }
    }

    // The words shown to players: a box of several lines, like the design's.
    private void Description(CompendiumEditor editor, FormField field, Func<JsonObject> Value, Func<int> At, VBoxContainer box)
    {
        var text = new TextEdit { Text = field.Text(Value()), CustomMinimumSize = new Vector2(0, 90), WrapMode = TextEdit.LineWrappingMode.Boundary };
        text.TextChanged += () =>
        {
            if (text.Text != field.Text(Value()))
            {
                _hint = editor.SetField(At(), field.Key, text.Text, out string why) ? "" : why;
            }
        };
        text.FocusExited += () =>
        {
            editor.EndTyping();
            text.Text = field.Text(Value());
        };
        box.AddChild(text);
    }

    // ---------------------------------------------------------------- how players see it

    // The page as the game's own data screens lay one out: name, a type line, its numbers, its
    // scores, then its words. Made from the entry's JSON and its form, so any kind gets one.
    private void ShowPage(CompendiumEditor editor, CreatePackage package, int? picked)
    {
        JsonObject? value = picked is int at ? editor.Entries[at].Value : _elsewhere.Length > 0 ? package.GameEntry(_kind, _elsewhere) : null;
        CompendiumEditor.Kind? kind = picked is int p ? editor.KindOf(editor.Entries[p].Kind) : Kind(editor);
        string page = value == null || kind == null ? "" : Page(value, kind);
        if (page != _pageShown)
        {
            _pageShown = page;
            _page.Text = page;
        }
    }

    private static string Page(JsonObject value, CompendiumEditor.Kind kind)
    {
        string id = FormJson.IsString(value[kind.Form.IdKey], out string? key) ? key : "";
        var page = new BookPage();
        page.Title(NameOf(value, id));
        var sub = new List<string> { kind.Form.Id };
        foreach (string word in new[] { "size", "type", "kind", "school", "slot" })
        {
            if (FormJson.IsString(value[word], out string? said) && said.Length > 0)
            {
                sub.Add(said);
            }
        }
        if (value["level"] is JsonValue level && level.TryGetValue(out double n))
        {
            sub.Add("level " + n.ToString(CultureInfo.InvariantCulture));
        }
        page.Sub(string.Join(", ", sub)).Rule();
        var stats = kind.Form.Fields.Where(f => f.Type is FormField.Kind.Integer or FormField.Kind.Number && f.Key != "level" && value[f.Key] is JsonValue)
            .Select(f => (Plain(f), f.Text(value))).ToArray();
        if (stats.Length > 0)
        {
            page.Stats(stats);
        }
        if (value["abilities"] is JsonObject scores && scores.Count > 0)
        {
            page.Table(scores.Select(s => s.Key.ToUpperInvariant()).ToList(), scores.Select(s => s.Value?.ToJsonString() ?? "").ToList());
        }
        if (FormJson.IsString(value["damage"], out string? damage) && damage.Length > 0)
        {
            page.Stat("Damage", damage + (FormJson.IsString(value["damageType"], out string? type) ? " " + type : ""));
        }
        if (FormJson.IsString(value["description"], out string? words) && words.Length > 0)
        {
            page.Gap().Text(words);
        }
        return page.ToString();
    }
}
