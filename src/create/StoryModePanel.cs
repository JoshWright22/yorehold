using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Yorehold.Rules;
using Kind = Yorehold.Rules.StoryEditor.Kind;

namespace Yorehold;

/// <summary>
/// Story mode of Create, a layout over a StoryEditor like the C++ client's: new nodes, the node
/// list and the suggestions on the left, the graph in the middle, the picked node or link on the
/// right. Delete takes the picked link or node out, Escape stops linking. Typing in a box is one
/// undo step until the box is left.
/// </summary>
public partial class StoryModePanel : HBoxContainer
{
    private StoryEditor? _editor;
    private ToolColumn _list = null!;
    private ToolColumn _fields = null!;
    private StoryGraphView _graph = null!;
    private Label _error = null!;
    private string _hint = "";

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 0);
        _list = Column(260);
        _graph = new StoryGraphView { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(_graph);
        _fields = Column(290);
        _error = new Label { ThemeTypeVariation = "WarnLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        _graph.AddChild(_error);
        _error.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide, LayoutPresetMode.Minsize, 16);

        _graph.NodeClicked += node =>
        {
            if (_editor == null)
            {
                return;
            }
            if (_graph.Linking && _graph.Node is int from && from != node)
            {
                int? link = _editor.AddLink(from, node);
                _hint = link == null ? "Those two are linked that way already." : "";
                _graph.Linking = false;
                if (link != null)
                {
                    _graph.Node = null;
                    _graph.Link = link;
                }
                return;
            }
            Pick(node);
        };
        _graph.LinkClicked += link =>
        {
            _graph.Node = null;
            _graph.Link = link;
            _graph.Linking = false;
            _hint = "";
        };
        _graph.BackgroundClicked += () => _graph.Linking = false;
    }

    /// <summary>The package's story graph, or null with why. Called every frame.</summary>
    public void Present(StoryEditor? editor, string error)
    {
        if (!ReferenceEquals(editor, _editor))
        {
            _editor = editor;
            _graph.Editor = editor;
            _graph.Node = null;
            _graph.Link = null;
            _graph.Linking = false;
            _hint = "";
            _list.Invalidate();
            _fields.Invalidate();
        }
        _error.Visible = editor == null;
        _error.Text = error.Length > 0 ? error : "Nothing to edit.";
        if (editor == null)
        {
            return;
        }
        // an undo can take away what was picked
        if (_graph.Node is int n && n >= editor.Nodes.Count)
        {
            _graph.Node = null;
        }
        if (_graph.Link is int l && l >= editor.Links.Count)
        {
            _graph.Link = null;
        }
        if (_graph.Node == null)
        {
            _graph.Linking = false;
        }
        List<StoryEditor.Suggestion> suggestions = editor.Suggestions();
        _list.Build($"{string.Join("|", editor.Nodes.Select(x => x.Id + x.Kind + StoryEditor.NameOf(x)))}#{_graph.Node}#{string.Join("|", suggestions.Select(s => s.Key))}#{editor.Dismissed.Count}",
            () => BuildList(editor, suggestions));
        string picked = _graph.Link is int link ? $"link {link}"
            : _graph.Node is int node ? $"node {node} {editor.Nodes[node].Kind} {editor.Nodes[node].Chapter.Length == 0} {editor.Links.Count}" : "none";
        _fields.Build(picked, () => BuildFields(editor));
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_editor == null || !IsVisibleInTree() || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        if (key.Keycode == Key.Escape && _graph.Linking)
        {
            _graph.Linking = false;
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Delete)
        {
            if (_graph.Link is int link)
            {
                _editor.RemoveLink(link);
                _graph.Link = null;
            }
            else if (_graph.Node is int node)
            {
                _editor.RemoveNode(node);
                _graph.Node = null;
            }
            GetViewport().SetInputAsHandled();
        }
    }

    private ToolColumn Column(float width)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(panel);
        var box = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(box);
        var column = new ToolColumn { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddChild(column);
        return column;
    }

    private void Pick(int? node)
    {
        _graph.Node = node;
        _graph.Link = null;
        _graph.Linking = false;
        _hint = "";
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string LeafOf(string path) => path[(path.LastIndexOf('/') + 1)..];

    // ---------------------------------------------------------------- the list

    private void BuildList(StoryEditor editor, List<StoryEditor.Suggestion> suggestions)
    {
        HBoxContainer head = _list.Row();
        _list.Heading("Nodes", head);
        Label count = _list.Live(() => $"{editor.Nodes.Count}, {editor.Links.Count} {(editor.Links.Count == 1 ? "link" : "links")}", "DimLabel", head);
        count.AutowrapMode = TextServer.AutowrapMode.Off;
        count.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        count.HorizontalAlignment = HorizontalAlignment.Right;
        // new nodes go in the middle of what the graph shows
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 3);
        grid.AddThemeConstantOverride("v_separation", 3);
        _list.AddChild(grid);
        foreach (Kind kind in StoryEditor.Kinds)
        {
            _list.Act(Capital(StoryEditor.KindName(kind)), () =>
            {
                float spread = editor.Nodes.Count % 5 * 16;
                if (editor.AddNode(kind, (new Vector2(160 + spread, 120 + spread) - _graph.Pan).ToRules()) is int added)
                {
                    Pick(added);
                }
            }, null, grid);
        }
        for (int i = 0; i < editor.Nodes.Count; i++)
        {
            int index = i;
            _list.Toggle(StoryEditor.NameOf(editor.Nodes[i]), () => _graph.Node == index, () =>
            {
                Pick(index);
                _graph.Show(index);
            }).Icon = MapModePanel.Swatch(StoryGraphView.ColorOf(editor.Nodes[i].Kind));
        }
        if (editor.Nodes.Count == 0)
        {
            _list.Dim("None yet. Add one above, or take a suggestion below.");
        }
        _list.Gap();

        HBoxContainer suggested = _list.Row();
        _list.Heading("Suggestions", suggested);
        Label number = _list.Dim(suggestions.Count.ToString(CultureInfo.InvariantCulture), suggested);
        number.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        number.AutowrapMode = TextServer.AutowrapMode.Off;
        ToolColumn.Narrow(_list.Act("Take all", () => editor.AcceptAll(), () => suggestions.Count > 0, suggested), 80);
        foreach (StoryEditor.Suggestion suggestion in suggestions)
        {
            _list.Dim(suggestion.Text).ThemeTypeVariation = "";
            HBoxContainer buttons = _list.Row();
            _list.Act("Take", () => editor.Accept(suggestion.Key), null, buttons);
            _list.Act("Not this", () => editor.Dismiss(suggestion.Key), null, buttons);
        }
        if (suggestions.Count == 0)
        {
            _list.Dim("Nothing to suggest.");
        }
        if (editor.Dismissed.Count > 0)
        {
            _list.Act($"Bring back {editor.Dismissed.Count} turned down", () => editor.RestoreDismissed());
        }
    }

    // ---------------------------------------------------------------- the picked node or link

    private void BuildFields(StoryEditor editor)
    {
        if (_graph.Link is int link)
        {
            BuildLink(editor, link);
        }
        else if (_graph.Node is int node)
        {
            BuildNode(editor, node);
        }
        else
        {
            _fields.Heading("Nothing picked");
            _fields.Dim("Click a node or a link on the graph.");
        }
        _fields.Gap();
        _fields.Live(() => _hint, "WarnLabel");
    }

    private void BuildNode(StoryEditor editor, int index)
    {
        StoryEditor.Node Now() => editor.Nodes[Math.Min(index, editor.Nodes.Count - 1)];
        StoryEditor.Node node = Now();
        _fields.Heading(Capital(StoryEditor.KindName(node.Kind)));
        Stepper("Kind", () => StoryEditor.KindName(Now().Kind), step =>
        {
            int at = StoryEditor.Kinds.ToList().IndexOf(Now().Kind);
            editor.SetKind(index, StoryEditor.Kinds[((at + step) % StoryEditor.Kinds.Count + StoryEditor.Kinds.Count) % StoryEditor.Kinds.Count]);
        });
        _fields.Dim("Id");
        _fields.Field(() => Now().Id, typed =>
        {
            _hint = typed == Now().Id || editor.RenameNode(index, typed) ? "" : "Ids are a-z, 0-9, - and _, and not taken.";
        }, editor.EndTyping, "id");
        _fields.Dim("Title");
        _fields.Field(() => Now().Title, typed => editor.SetTitle(index, typed), editor.EndTyping, "untitled");
        _fields.Dim("Notes");
        _fields.Field(() => Now().Text, typed => editor.SetText(index, typed), editor.EndTyping, "for the writer");
        _fields.Gap();

        // the chapter it happens in, and what it stands for there
        List<string> Folders() => new[] { "" }.Concat(editor.Names.Chapters.Select(c => c.Folder)).ToList();
        Stepper("Chapter", () => Now().Chapter.Length == 0 ? "not made yet" : CreatePackage.Leaf(Now().Chapter), step =>
        {
            List<string> folders = Folders();
            int at = Math.Max(0, folders.IndexOf(Now().Chapter));
            editor.SetChapter(index, folders[((at + step) % folders.Count + folders.Count) % folders.Count]);
        });
        if (StoryEditor.RefField(node.Kind).Length > 0)
        {
            List<string> Options()
            {
                List<string> options = new[] { "" }.Concat(editor.RefOptions(index)).ToList();
                if (Now().Ref.Length > 0 && !options.Contains(Now().Ref))
                {
                    options.Add(Now().Ref);
                }
                return options;
            }
            Stepper(Capital(StoryEditor.RefField(node.Kind)), () => Now().Ref.Length == 0 ? "(none)" : LeafOf(Now().Ref), step =>
            {
                List<string> options = Options();
                int at = Math.Max(0, options.IndexOf(Now().Ref));
                editor.SetRef(index, options[((at + step) % options.Count + options.Count) % options.Count]);
            });
        }
        if (node.Kind is Kind.Encounter or Kind.Quest)
        {
            _fields.Dim("XP");
            _fields.Field(() => Now().Xp?.ToString(CultureInfo.InvariantCulture) ?? "", typed =>
            {
                bool fine = typed.Trim().Length == 0 ? editor.SetXp(index, null)
                    : int.TryParse(typed.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int xp) && editor.SetXp(index, xp);
                _hint = fine ? "" : "XP is a whole number from 0.";
            }, editor.EndTyping, "none");
        }
        if (node.Kind == Kind.Quest)
        {
            _fields.Dim("Steps, split by ;");
            _fields.Field(() => string.Join("; ", Now().Steps), typed =>
            {
                List<string> steps = typed.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                _hint = editor.SetSteps(index, steps) ? "" : "Up to 50 steps, none empty.";
            }, editor.EndTyping, "none");
            _fields.Live(() => string.Join("\n", Now().Steps.Select((s, i) => $"{i + 1}. {s}")), "");
        }
        if (node.Kind == Kind.Scene && node.Chapter.Length == 0)
        {
            _fields.Dim("Map it needs, width and height");
            HBoxContainer size = _fields.Row();
            LineEdit? heightBox = null;
            LineEdit widthBox = _fields.Field(() => Now().MapWidth == 0 ? "" : Now().MapWidth.ToString(CultureInfo.InvariantCulture), typed => Size(typed, heightBox?.Text ?? ""), editor.EndTyping, "width", size);
            heightBox = _fields.Field(() => Now().MapHeight == 0 ? "" : Now().MapHeight.ToString(CultureInfo.InvariantCulture), typed => Size(widthBox.Text, typed), editor.EndTyping, "height", size);
            void Size(string width, string height)
            {
                int.TryParse(width.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int w);
                int.TryParse(height.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int h);
                _hint = editor.SetMapSize(index, w, h) ? "" : "A map is 8 to 200 each way.";
            }
        }
        _fields.Gap();
        HBoxContainer buttons = _fields.Row();
        _fields.Toggle("Link to...", () => _graph.Linking, () => _graph.Linking = !_graph.Linking, buttons, "TabButton").Alignment = HorizontalAlignment.Center;
        _fields.Act("Remove", () =>
        {
            editor.RemoveNode(index);
            Pick(null);
        }, null, buttons);
        _fields.Gap();

        // where it goes from here
        for (int l = 0; l < editor.Links.Count; l++)
        {
            StoryEditor.Link link = editor.Links[l];
            if (link.From != node.Id && link.To != node.Id)
            {
                continue;
            }
            int at = l;
            _fields.Act(link.From == node.Id ? "to " + link.To : "from " + link.From, () =>
            {
                _graph.Node = null;
                _graph.Link = at;
            }).Alignment = HorizontalAlignment.Left;
        }
    }

    private void BuildLink(StoryEditor editor, int index)
    {
        StoryEditor.Link Now() => editor.Links[Math.Min(index, editor.Links.Count - 1)];
        _fields.Heading("Link");
        _fields.Act("From " + Now().From, () =>
        {
            if (editor.Find(Now().From) is int from)
            {
                Pick(from);
            }
        }).Alignment = HorizontalAlignment.Left;
        _fields.Act("To " + Now().To, () =>
        {
            if (editor.Find(Now().To) is int to)
            {
                Pick(to);
            }
        }).Alignment = HorizontalAlignment.Left;
        _fields.Gap();
        _fields.Dim("What takes the story there");
        _fields.Field(() => Now().Text, typed => editor.SetLinkText(index, typed), editor.EndTyping, "nothing yet");
        _fields.Dim("Needs flags");
        _fields.Field(() => string.Join(", ", Now().When), typed =>
        {
            List<string> flags = typed.Split(',').Select(f => f.Trim()).Where(f => f.Length > 0).ToList();
            _hint = editor.SetLinkWhen(index, flags) ? "" : "A flag can't be empty or there twice.";
        }, editor.EndTyping, "none");
        _fields.Gap();
        _fields.Act("Remove link", () =>
        {
            editor.RemoveLink(index);
            _graph.Link = null;
        });
    }

    // "Name  <  text  >": the arrows step through a list.
    private void Stepper(string name, Func<string> text, Action<int> step)
    {
        _fields.Dim(name);
        HBoxContainer row = _fields.Row();
        ToolColumn.Narrow(_fields.Act("<", () => step(-1), null, row), 28);
        Label shown = _fields.Live(text, "", row);
        shown.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        shown.HorizontalAlignment = HorizontalAlignment.Center;
        shown.AutowrapMode = TextServer.AutowrapMode.Off;
        shown.ClipText = true;
        ToolColumn.Narrow(_fields.Act(">", () => step(1), null, row), 28);
    }
}
