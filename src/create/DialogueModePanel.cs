using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Dialogue mode of Create, a layout over a DialogueEditor like the C++ client's: which file along
/// the top, the nodes on the left, the picked node and its replies in the middle, the picked reply
/// (or the node's own flags and actions) on the right. Typing in a box is one undo step until the
/// box is left. Delete takes the picked reply off.
/// </summary>
public partial class DialogueModePanel : VBoxContainer
{
    private CreatePackage? _package;
    private DialogueEditor? _editor;
    private string _chapter = "\u0000";
    private List<string> _listed = new();
    private Label _file = null!;
    private Label _count = null!;
    private Button _back = null!;
    private Button _on = null!;
    private Label _error = null!;
    private HBoxContainer _body = null!;
    private ToolColumn _nodes = null!;
    private ToolColumn _node = null!;
    private ToolColumn _reply = null!;
    private Button _voiceView = null!;
    private VoicePanel _voice = null!;
    private bool _voicing;

    private int _picked;
    private int? _choice;
    private string _hint = "";

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 0);
        var bar = new PanelContainer();
        AddChild(bar);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 3);
        bar.AddChild(row);
        _back = Small(row, "<", () => Step(-1));
        _file = new Label { CustomMinimumSize = new Vector2(320, 0), ClipText = true };
        row.AddChild(_file);
        _on = Small(row, ">", () => Step(1));
        _count = new Label { ThemeTypeVariation = "DimLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(_count);
        // the recorded lines of the same conversation, in place of its nodes
        _voiceView = new Button { Text = "Voice", ToggleMode = true, ThemeTypeVariation = "TabButton", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(90, 26) };
        _voiceView.Toggled += on => _voicing = on;
        row.AddChild(_voiceView);
        Button made = Small(row, "New conversation", () =>
        {
            _package?.NewDialogue();
            _chapter = "\u0000";
        });
        made.CustomMinimumSize = new Vector2(150, 26);

        _error = new Label { ThemeTypeVariation = "WarnLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        AddChild(_error);
        _body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 0);
        AddChild(_body);
        _nodes = Column(200, false);
        _node = Column(0, true);
        _reply = Column(300, false);
        _voice = new VoicePanel { SizeFlagsVertical = SizeFlags.ExpandFill, Visible = false };
        AddChild(_voice);
    }

    /// <summary>The package whose chapter's conversations are shown. Called every frame.</summary>
    public void Present(CreatePackage package)
    {
        _package = package;
        if (package.PackagePath + "|" + package.Chapter != _chapter)
        {
            // another package or chapter, or a file made: list them again and keep the open one if it is there
            _chapter = package.PackagePath + "|" + package.Chapter;
            _listed = package.DialogueFiles();
            if (!_listed.Contains(package.DialoguePath))
            {
                package.OpenDialogue(_listed.FirstOrDefault() ?? "");
            }
        }
        string path = package.DialoguePath;
        // the conversation by its name, not its file; < and > step through the chapter's others
        _file.Text = path.Length == 0 ? "No conversations yet" : "Conversation: " + System.IO.Path.GetFileNameWithoutExtension(path);
        _count.Text = _listed.Count < 2 ? "" : $"   {_listed.IndexOf(path) + 1} of {_listed.Count}";
        _back.Disabled = _on.Disabled = _listed.Count < 2;

        DialogueEditor? editor = package.DialogueEditor();
        if (!ReferenceEquals(editor, _editor))
        {
            _editor = editor;
            _picked = 0;
            _choice = null;
            _hint = "";
            _nodes.Invalidate();
            _node.Invalidate();
            _reply.Invalidate();
        }
        _voiceView.SetPressedNoSignal(_voicing);
        _body.Visible = editor != null && !_voicing;
        _voice.Visible = editor != null && _voicing;
        _error.Visible = editor == null;
        _error.Text = package.Chapter.Length == 0 ? "This package has no chapter to write conversations for."
            : path.Length == 0 ? "This chapter has no conversations. New conversation starts one in its dialogue folder."
            : package.DialogueError.Length > 0 ? package.DialogueError : "This file can't be read.";
        if (editor == null)
        {
            return;
        }
        if (_voicing)
        {
            _voice.Present(package, editor);
            return;
        }

        // an undo can take away the node or reply that was picked
        if (_picked >= editor.Nodes.Count)
        {
            _picked = editor.Nodes.Count - 1;
            _choice = null;
        }
        if (_choice is int c && c >= editor.Nodes[_picked].Choices.Count)
        {
            _choice = null;
        }
        DialogueEditor.Node node = editor.Nodes[_picked];
        _nodes.Build($"{_picked}|{editor.Start}|{string.Join("|", editor.Nodes.Select(n => n.Id))}", () => BuildNodes(editor));
        _node.Build($"{_picked}|{_choice}|{string.Join("|", node.Choices.Select(r => r.Text))}", () => BuildNode(editor));
        _reply.Build($"{_picked}|{_choice}|{(_choice is int at ? node.Choices[at].Check != null : false)}", () => BuildReply(editor));
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_editor != null && !_voicing && IsVisibleInTree() && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Delete } && _choice is int c)
        {
            _editor.RemoveChoice(_picked, c);
            _choice = null;
            GetViewport().SetInputAsHandled();
        }
    }

    private void Step(int by)
    {
        if (_package == null || _listed.Count < 2)
        {
            return;
        }
        int at = Math.Max(0, _listed.IndexOf(_package.DialoguePath));
        _package.OpenDialogue(_listed[((at + by) % _listed.Count + _listed.Count) % _listed.Count]);
    }

    private ToolColumn Column(float width, bool grow)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        if (grow)
        {
            panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }
        _body.AddChild(panel);
        var box = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(box);
        var column = new ToolColumn { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddChild(column);
        return column;
    }

    private static Button Small(HBoxContainer row, string text, Action press)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(28, 26) };
        button.Pressed += press;
        row.AddChild(button);
        return button;
    }

    // ---------------------------------------------------------------- nodes

    private void BuildNodes(DialogueEditor editor)
    {
        _nodes.Dim("Its name, for people and triggers to start it by");
        _nodes.Field(() => editor.Id, typed =>
        {
            _hint = typed == editor.Id || editor.SetId(typed) ? "" : "An id is 1 to 64 characters, no spaces.";
        }, editor.EndTyping, "id");
        _nodes.Gap();
        _nodes.Heading("Lines");
        _nodes.Dim("Each is something said, with the player's replies. > is the first.");
        for (int i = 0; i < editor.Nodes.Count; i++)
        {
            int index = i;
            string id = editor.Nodes[i].Id;
            _nodes.Toggle((id == editor.Start ? "> " : "   ") + id, () => _picked == index, () =>
            {
                _picked = index;
                _choice = null;
                _hint = "";
            });
        }
        HBoxContainer row = _nodes.Row();
        _nodes.Act("Add", () =>
        {
            if (editor.AddNode() is int added)
            {
                _picked = added;
                _choice = null;
            }
        }, null, row);
        _nodes.Act("Remove", () =>
        {
            editor.RemoveNode(_picked);
            _choice = null;
        }, () => editor.Nodes.Count > 1, row);
        _nodes.Act("Start here", () => editor.SetStart(_picked), () => _picked < editor.Nodes.Count && editor.Nodes[_picked].Id != editor.Start);
    }

    // ---------------------------------------------------------------- the picked node

    private void BuildNode(DialogueEditor editor)
    {
        int index = _picked;
        DialogueEditor.Node Now() => editor.Nodes[Math.Min(index, editor.Nodes.Count - 1)];

        HBoxContainer heads = _node.Row();
        _node.Dim("Its name, for replies to go to", heads).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _node.Dim("Who says it", heads).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        HBoxContainer names = _node.Row();
        _node.Field(() => Now().Id, typed =>
        {
            bool fine = typed == Now().Id || editor.RenameNode(index, typed);
            _hint = fine ? "" : typed.Length == 0 ? "A node needs an id." : "Another node has that id, or it has a space.";
        }, editor.EndTyping, "id", names);
        _node.Field(() => Now().Speaker, typed => editor.SetSpeaker(index, typed), editor.EndTyping, "nobody", names);

        HBoxContainer lineHead = _node.Row();
        _node.Dim("What they say", lineHead).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Label from = _node.Live(() =>
        {
            int links = editor.LinksTo(Now().Id);
            return Now().Id == editor.Start ? "the start" : links == 0 ? "no reply leads here" : links == 1 ? "1 reply leads here" : $"{links} replies lead here";
        }, "DimLabel", lineHead);
        from.HorizontalAlignment = HorizontalAlignment.Right;
        from.AutowrapMode = TextServer.AutowrapMode.Off;
        _node.Field(() => Now().Text, typed => editor.SetText(index, typed), editor.EndTyping, "what they say");

        // the whole line as it will read, since the box shows only a part of a long one
        var page = new PanelContainer { ThemeTypeVariation = "TipPanel" };
        _node.AddChild(page);
        _node.Live(() => Now().Text.Length == 0 ? "(no line yet)" : Now().Speaker.Length == 0 ? Now().Text : $"{Now().Speaker}: {Now().Text}", "", page);
        _node.Gap();

        _node.Heading("Replies");
        DialogueEditor.Node node = Now();
        if (node.Choices.Count == 0)
        {
            _node.Dim("None, so this is the last line.");
        }
        for (int i = 0; i < node.Choices.Count; i++)
        {
            int at = i;
            _node.Toggle($"{i + 1}. {node.Choices[i].Text}", () => _choice == at, () =>
            {
                _choice = _choice == at ? null : at;
                _hint = "";
            });
            _node.Live(() => at < Now().Choices.Count ? About(Now().Choices[at]) : "");
        }
        HBoxContainer buttons = _node.Row();
        _node.Act("Add reply", () =>
        {
            if (editor.AddChoice(index) is int added)
            {
                _choice = added;
            }
        }, null, buttons);
        _node.Act("Up", () =>
        {
            if (_choice is int c && editor.MoveChoice(index, c, -1))
            {
                _choice = c - 1;
            }
        }, () => _choice is > 0, buttons);
        _node.Act("Down", () =>
        {
            if (_choice is int c && editor.MoveChoice(index, c, 1))
            {
                _choice = c + 1;
            }
        }, () => _choice is int c && c + 1 < Now().Choices.Count, buttons);
        _node.Act("Remove", () =>
        {
            if (_choice is int c)
            {
                editor.RemoveChoice(index, c);
                _choice = null;
            }
        }, () => _choice != null, buttons);
    }

    // Where a reply goes and what it needs and does, for the list.
    private static string About(DialogueEditor.Choice c)
    {
        string about = c.Check is DialogueCheck check
            ? $"{check.Skill} check {check.Difficulty}: goes to {(check.Success.Length == 0 ? "the end" : check.Success)}, or {(check.Failure.Length == 0 ? "the end" : check.Failure)} on a fail"
            : c.Next.Length == 0 ? "ends the conversation" : "goes to " + c.Next;
        if (c.Require.Count > 0)
        {
            about += "   needs " + string.Join(", ", c.Require);
        }
        if (c.Forbid.Count > 0)
        {
            about += "   not if " + string.Join(", ", c.Forbid);
        }
        if (c.Flags.Set.Count > 0)
        {
            about += "   sets " + string.Join(", ", c.Flags.Set);
        }
        if (c.Flags.Actions.Count > 0)
        {
            about += "   do " + string.Join(", ", c.Flags.Actions);
        }
        return "      " + about;
    }

    // ---------------------------------------------------------------- the picked reply, or the node's own flags

    private void BuildReply(DialogueEditor editor)
    {
        int index = _picked;
        if (_choice is not int at)
        {
            _reply.Heading("When " + editor.Nodes[index].Id + " is said");
            _reply.Dim("What changes in the story as this line is said. Pick a reply to edit that instead.");
            FlagFields(editor, () => editor.Nodes[Math.Min(index, editor.Nodes.Count - 1)].Flags, flags => editor.SetNodeFlags(index, flags));
            _reply.Gap();
            _reply.Live(() => _hint, "WarnLabel");
            return;
        }
        DialogueEditor.Choice Now() => editor.Nodes[index].Choices[Math.Min(at, editor.Nodes[index].Choices.Count - 1)];

        HBoxContainer head = _reply.Row();
        _reply.Heading($"Reply {at + 1}", head).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ToolColumn.Narrow(_reply.Act("The line's own", () => _choice = null, null, head), 110);
        _reply.Dim("Its name");
        _reply.Field(() => Now().Id, typed =>
        {
            bool fine = typed == Now().Id || editor.SetChoiceId(index, at, typed);
            _hint = fine ? "" : "Another reply here has that id, or it has a space.";
        }, editor.EndTyping, "id");
        _reply.Dim("What the player says");
        _reply.Field(() => Now().Text, typed =>
        {
            bool fine = typed == Now().Text || editor.SetChoiceText(index, at, typed);
            _hint = fine ? "" : "A reply needs some words.";
        }, editor.EndTyping, "...");
        _reply.Gap();

        // where it goes: a node, the end, or a roll that picks between two
        List<string> Targets() => new[] { "" }.Concat(editor.Nodes.Select(n => n.Id)).ToList();
        static string Shown(string id) => id.Length == 0 ? "the end" : id;
        _reply.Toggle("Skill check", () => Now().Check != null, () =>
        {
            editor.EndTyping();
            if (Now().Check == null)
            {
                string skill = editor.Names.Skills.FirstOrDefault() ?? "persuasion";
                editor.SetCheck(index, at, new DialogueCheck(skill, 12, "", ""));
            }
            else
            {
                editor.SetCheck(index, at, null);
            }
        }, null, "ChipButton");
        if (Now().Check == null)
        {
            Stepper("Goes to", () => Shown(Now().Next), step => editor.SetNext(index, at, StepName(Targets(), Now().Next, step)));
        }
        else
        {
            DialogueCheck Check() => Now().Check ?? new DialogueCheck("", 0, "", "");
            List<string> Skills()
            {
                List<string> skills = editor.Names.Skills.ToList();
                if (!skills.Contains(Check().Skill))
                {
                    skills.Insert(0, Check().Skill);
                }
                return skills;
            }
            Stepper("Rolls", () => Check().Skill, step => editor.SetCheck(index, at, Check() with { Skill = StepName(Skills(), Check().Skill, step) }));
            HBoxContainer dc = _reply.Row();
            _reply.Dim("Difficulty", dc).CustomMinimumSize = new Vector2(64, 0);
            _reply.Field(() => Check().Difficulty.ToString(CultureInfo.InvariantCulture), typed =>
            {
                bool fine = int.TryParse(typed, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                    && (value == Check().Difficulty || editor.SetCheck(index, at, Check() with { Difficulty = value }));
                _hint = fine ? "" : "The difficulty is a whole number, 0 to 100000.";
            }, editor.EndTyping, "12", dc);
            Stepper("Pass", () => Shown(Check().Success), step => editor.SetCheck(index, at, Check() with { Success = StepName(Targets(), Check().Success, step) }));
            Stepper("Fail", () => Shown(Check().Failure), step => editor.SetCheck(index, at, Check() with { Failure = StepName(Targets(), Check().Failure, step) }));
        }
        _reply.Act(() => Now().Check != null ? "New line where it ends" : "New line after it", () =>
        {
            if (editor.Branch(index, at) is int added)
            {
                _picked = added;
                _choice = null;
            }
        }, () => Now().Check is not DialogueCheck k || k.Success.Length == 0 || k.Failure.Length == 0);
        _reply.Gap();

        HBoxContainer conditionHeads = _reply.Row();
        _reply.Dim("Shown only with flags", conditionHeads).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _reply.Dim("Hidden by flags", conditionHeads).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        HBoxContainer conditions = _reply.Row();
        LineEdit? forbidBox = null;
        LineEdit requireBox = _reply.Field(() => string.Join(", ", Now().Require), typed => Conditions(typed, forbidBox?.Text ?? ""), editor.EndTyping, "none", conditions);
        forbidBox = _reply.Field(() => string.Join(", ", Now().Forbid), typed => Conditions(requireBox.Text, typed), editor.EndTyping, "none", conditions);
        void Conditions(string require, string forbid)
        {
            List<string> needs = ListFrom(require), hides = ListFrom(forbid);
            bool fine = (needs.SequenceEqual(Now().Require) && hides.SequenceEqual(Now().Forbid)) || editor.SetConditions(index, at, needs, hides);
            _hint = fine ? "" : "A flag can't be both needed and hidden by.";
        }
        _reply.Gap();
        FlagFields(editor, () => Now().Flags, flags => editor.SetChoiceFlags(index, at, flags));
        _reply.Gap();
        _reply.Live(() => _hint, "WarnLabel");
    }

    // Set, clear and do boxes with the companion buttons under them.
    private void FlagFields(DialogueEditor editor, Func<DialogueEditor.Flags> flags, Func<DialogueEditor.Flags, bool> set)
    {
        void Typed(DialogueEditor.Flags changed)
        {
            bool fine = changed.Same(flags()) || set(changed);
            _hint = fine ? "" : "Flags are 1 to 64 characters, not both set and cleared.";
        }
        // a button press is its own undo step, apart from any typing before or after it
        void Clicked(DialogueEditor.Flags changed)
        {
            editor.EndTyping();
            Typed(changed);
            editor.EndTyping();
        }
        _reply.Dim("Story flags it sets, for doors, fights and other lines to wait for (comma between)");
        _reply.Field(() => string.Join(", ", flags().Set), typed => Typed(flags() with { Set = ListFrom(typed) }), editor.EndTyping, "none");
        _reply.Dim("Story flags it takes away");
        _reply.Field(() => string.Join(", ", flags().Clear), typed => Typed(flags() with { Clear = ListFrom(typed) }), editor.EndTyping, "none");
        _reply.Dim("What happens (the buttons below fill it in)");
        _reply.Field(() => string.Join(", ", flags().Actions), typed => Typed(flags() with { Actions = ListFrom(typed) }), editor.EndTyping, "nothing");

        // the companion actions, so nobody has to remember how they are spelled; approve steps the one being talked to by one each click
        DialogueEditor.Flags Approve(int by)
        {
            List<string> actions = flags().Actions.ToList();
            for (int i = 0; i < actions.Count; i++)
            {
                string[] parts = actions[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || parts[0] != "approve" || DialogueEditor.ApprovalChange(parts[1]) is not int now)
                {
                    continue;
                }
                if (now + by == 0)
                {
                    actions.RemoveAt(i);
                }
                else
                {
                    actions[i] = $"approve {now + by}";
                }
                return flags() with { Actions = actions };
            }
            actions.Add($"approve {by}");
            return flags() with { Actions = actions };
        }
        HBoxContainer row = _reply.Row();
        _reply.Act("Recruit", () => Clicked(flags() with { Actions = flags().Actions.Append("recruit").ToList() }), () => !flags().Actions.Contains("recruit"), row);
        _reply.Act("Dismiss", () => Clicked(flags() with { Actions = flags().Actions.Append("dismiss").ToList() }), () => !flags().Actions.Contains("dismiss"), row);
        HBoxContainer approve = _reply.Row();
        _reply.Act("Approve +1", () => Clicked(Approve(1)), null, approve);
        _reply.Act("Approve -1", () => Clicked(Approve(-1)), null, approve);
        Label problem = _reply.Live(() =>
        {
            foreach (string action in flags().Actions)
            {
                string why = DialogueEditor.ActionProblem(action, editor.Names, out _);
                if (why.Length > 0)
                {
                    return why;
                }
            }
            return "";
        });
        problem.ThemeTypeVariation = "WarnLabel";
    }

    // "Label  <  text  >": the arrows step through a list.
    private void Stepper(string name, Func<string> text, Action<int> step)
    {
        HBoxContainer row = _reply.Row();
        _reply.Dim(name, row).CustomMinimumSize = new Vector2(64, 0);
        ToolColumn.Narrow(_reply.Act("<", () => step(-1), null, row), 28);
        Label shown = _reply.Live(text, "", row);
        shown.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        shown.HorizontalAlignment = HorizontalAlignment.Center;
        shown.AutowrapMode = TextServer.AutowrapMode.Off;
        shown.ClipText = true;
        ToolColumn.Narrow(_reply.Act(">", () => step(1), null, row), 28);
    }

    // The name step places along from current in names, round at the ends.
    private static string StepName(IReadOnlyList<string> names, string current, int step)
    {
        if (names.Count == 0)
        {
            return current;
        }
        int index = Math.Max(0, names.ToList().IndexOf(current));
        return names[((index + step) % names.Count + names.Count) % names.Count];
    }

    // "a, b" to its names, blanks dropped.
    private static List<string> ListFrom(string text) => text.Split(',').Select(f => f.Trim()).Where(f => f.Length > 0).ToList();
}
