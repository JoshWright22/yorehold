using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The Voice view of Dialogue mode, a layout over the package's VoiceImporter like the C++
/// client's: the conversation's nodes on the left with how far each one's voice is, the picked
/// one's line, recording, buttons and words on the right. Imports run off the main thread.
/// </summary>
public partial class VoicePanel : HBoxContainer
{
    private ToolColumn _nodes = null!;
    private ToolColumn _line = null!;
    private int _picked;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 0);
        _nodes = Column(260, false);
        _line = Column(0, true);
    }

    private ToolColumn Column(float width, bool grow)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        if (grow)
        {
            panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }
        AddChild(panel);
        var box = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(box);
        var column = new ToolColumn { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddChild(column);
        return column;
    }

    /// <summary>One conversation's lines. Called every frame while the view is up.</summary>
    public void Present(CreatePackage package, DialogueEditor conversation)
    {
        if (package.VoiceImporter() is not VoiceImporter voices || conversation.Nodes.Count == 0)
        {
            return;
        }
        package.VoiceJob.Finish(voices);
        _picked = Math.Min(_picked, conversation.Nodes.Count - 1);
        ContentFiles files = package.PackageFiles();

        var marks = new List<string>();
        foreach (DialogueEditor.Node node in conversation.Nodes)
        {
            string stem = VoiceImporter.Stem(conversation.Id, node.Id);
            VoiceImporter.Line line = voices.LineOf(files, stem);
            marks.Add(line.Error.Length > 0 ? "broken" : line.Voice != null ? (voices.Problems(stem, node.Text).Count == 0 ? "ok" : "check") : line.Audio.Length > 0 ? "to import" : "-");
        }
        _nodes.Build($"{conversation.Id}|{_picked}|{string.Join("|", conversation.Nodes.Select((n, i) => n.Id + "=" + marks[i]))}", () =>
        {
            _nodes.Heading("Lines");
            _nodes.Dim("- no recording, to import, ok, check");
            for (int i = 0; i < conversation.Nodes.Count; i++)
            {
                int index = i;
                _nodes.Toggle($"{conversation.Nodes[i].Id}   {marks[i]}", () => _picked == index, () => _picked = index);
            }
        });

        DialogueEditor.Node picked = conversation.Nodes[_picked];
        string pickedStem = VoiceImporter.Stem(conversation.Id, picked.Id);
        VoiceImporter.Line current = voices.LineOf(files, pickedStem);
        _line.Build($"{pickedStem}|{current.Audio}|{current.Voice?.ToJson()}|{VoiceWords.Split(picked.Text).Count == 0}", () => BuildLine(package, voices, conversation, _picked));
    }

    private void BuildLine(CreatePackage package, VoiceImporter voices, DialogueEditor conversation, int index)
    {
        DialogueEditor.Node Now() => conversation.Nodes[Math.Min(index, conversation.Nodes.Count - 1)];
        string stem = VoiceImporter.Stem(conversation.Id, Now().Id);
        VoiceImporter.Line Line() => voices.LineOf(package.PackageFiles(), stem);
        VoiceImporter.Line line = Line();

        _line.Heading(Now().Speaker.Length == 0 ? Now().Id : $"{Now().Id}  ({Now().Speaker})");
        _line.Live(() => Now().Text.Length == 0 ? "No line written yet: an import suggests one." : Now().Text, "");
        _line.Dim(line.Audio.Length == 0 ? $"No recording. Put one at voice/{stem}.{voices.Options.Extensions.FirstOrDefault() ?? "wav"} in the package." : "Recording: " + line.Audio);

        var buttons = new HFlowContainer();
        buttons.AddThemeConstantOverride("h_separation", 4);
        buttons.AddThemeConstantOverride("v_separation", 4);
        _line.AddChild(buttons);
        bool Written() => VoiceWords.Split(Now().Text).Count > 0;
        Button Act(string text, Action press, Func<bool> enabled)
        {
            Button button = _line.Act(text, press, enabled, buttons);
            button.SizeFlagsHorizontal = SizeFlags.Fill;
            button.CustomMinimumSize = new Vector2(110, 26);
            return button;
        }
        Act(line.Voice != null ? "Import again" : "Import", () => package.VoiceJob.Start(voices, package.PackageFiles(), stem, Now().Text),
            () => Line().Audio.Length > 0 && !package.VoiceJob.Busy && VoiceTranscription.BuiltIn);
        Act("Look again", () => voices.LineOf(package.PackageFiles(), stem, true), () => !package.VoiceJob.Busy);
        Act("Match to line", () => voices.Match(stem, Now().Text), () => Line().Voice is VoiceLine v && Written() && VoiceImporter.Differs(v, Now().Text));
        Act("Use as line", () =>
        {
            if (Line().Voice is VoiceLine v)
            {
                conversation.SetText(Math.Min(index, conversation.Nodes.Count - 1), v.Text);
            }
        }, () => Line().Voice != null && !Written());
        Act("Copy SRT", () => DisplayServer.ClipboardSet(Line().Voice?.ToSrt() ?? ""), () => Line().Voice != null);
        Act("Copy VTT", () => DisplayServer.ClipboardSet(Line().Voice?.ToVtt() ?? ""), () => Line().Voice != null);

        if (!VoiceTranscription.BuiltIn)
        {
            _line.Dim("This build can't listen to recordings yet: Import waits for the speech model. Voice files made elsewhere open, match and save here.");
        }
        _line.Live(() => package.VoiceJob.Status, "DimLabel");
        _line.Live(() => string.Join("\n", voices.Problems(stem, Now().Text).Select(p => p.Text)), "WarnLabel");
        if (line.Voice is not VoiceLine voice)
        {
            return;
        }
        if (!Written())
        {
            _line.Dim("Heard: " + voice.Text).ThemeTypeVariation = "";
        }
        _line.Gap();
        var strip = new VoiceStrip();
        _line.AddChild(strip);
        strip.Show(voice, voices.Options.FlagBelow);
        double length = voice.Words.Count == 0 ? 0 : voice.Words[^1].End;
        _line.Dim($"{voice.Model}, {voice.Words.Count} words, {length.ToString("0.00", CultureInfo.InvariantCulture)} s");

        // one row per word, as a tight table
        var table = new GridContainer { Columns = 4 };
        table.AddThemeConstantOverride("h_separation", 16);
        table.AddThemeConstantOverride("v_separation", 0);
        _line.AddChild(table);
        void Cell(string text, string variation, Color? color = null)
        {
            var label = new Label { Text = text, ThemeTypeVariation = variation };
            label.AddThemeFontSizeOverride("font_size", 14);
            if (color is Color c)
            {
                label.AddThemeColorOverride("font_color", c);
            }
            table.AddChild(label);
        }
        foreach (string head in new[] { "Word", "Start", "End", "Sure" })
        {
            Cell(head, "DimLabel");
        }
        foreach (VoiceWord word in voice.Words)
        {
            Color color = VoiceStrip.ColorOf(word, voices.Options.FlagBelow);
            Cell(word.Text, "", color);
            Cell(word.Start.ToString("0.00", CultureInfo.InvariantCulture), "CellLabel");
            Cell(word.End.ToString("0.00", CultureInfo.InvariantCulture), "CellLabel");
            Cell(word.Matched ? word.Confidence.ToString("0.00", CultureInfo.InvariantCulture) : "guessed", "", color);
        }
    }
}
