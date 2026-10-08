using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>Story mode's part of the open package: story.json and what the rest of the package offers it to point at.</summary>
public sealed partial class CreatePackage
{
    private StoryEditor? _story;
    private string _storySaved = "";
    private string _storyError = "";
    private bool _storyFresh;

    public string StoryError => _storyError;

    /// <summary>
    /// The story graph, read the first time it is asked for (an empty one when there is no
    /// story.json), with what the package has for it to point at read again after a save or
    /// StaleStory. Null if story.json can't be read.
    /// </summary>
    public StoryEditor? StoryEditor()
    {
        if (Manifest == null)
        {
            return null;
        }
        if (_story == null)
        {
            if (_storyError.Length > 0)
            {
                return null;
            }
            var files = new ContentFiles(PackagePath);
            var editor = new StoryEditor(_history);
            if (files.Exists("story.json"))
            {
                if (!editor.Load(files.ReadText("story.json"), out string error))
                {
                    _storyError = "story.json: " + error;
                    return null;
                }
            }
            else
            {
                editor.Create();
            }
            // compared in the editor's own form, so the file is only written once something changed
            _storySaved = editor.ToJson();
            _story = editor;
            _storyFresh = false;
        }
        if (!_storyFresh)
        {
            _story.SetCatalog(StoryCatalog());
            _storyFresh = true;
        }
        return _story;
    }

    /// <summary>The other modes' work may have changed what the graph can point at: read it again next time.</summary>
    public void StaleStory() => _storyFresh = false;

    /// <summary>What the package has for the graph to point at, from its files and the other modes' open editors, so a group placed a moment ago counts.</summary>
    public StoryEditor.Catalog StoryCatalog()
    {
        var catalog = new StoryEditor.Catalog();
        if (Manifest == null)
        {
            return catalog;
        }
        var files = new ContentFiles(PackagePath);
        ContentFiles all = PlayFiles();
        // a path a chapter names, in the chapter folder first and then from the root, as the game finds it
        string Resolve(string folder, string named)
        {
            if (!ContentFiles.IsContentPath(named))
            {
                return "";
            }
            return files.Exists(folder + "/" + named) ? folder + "/" + named : files.Exists(named) ? named : "";
        }
        // creature levels for the XP each fight is worth, the way Encounters mode proposes it
        var shared = new Compendium();
        try
        {
            shared.Load(all, RulesFolder.Default, "");
        }
        catch (ContentException)
        {
        }
        int perLevel = new EncountersEditor.Catalog().XpPerLevel;

        foreach (string folder in Manifest.Chapters)
        {
            if (ReadObject(files, folder + "/chapter.json") is not JsonObject j)
            {
                continue;
            }
            var chapter = new StoryEditor.Catalog.Chapter
            {
                Folder = folder,
                Id = FormJson.IsString(j["id"], out string? id) ? id : Leaf(folder),
                Title = FormJson.IsString(j["title"], out string? title) ? title : "",
            };

            // groups as Encounters mode has them if it is open, so ones not saved yet count
            if (_encounters.TryGetValue(folder, out EncountersTab? open))
            {
                EncountersEditor editor = open.Editor;
                for (int g = 0; g < editor.Groups.Count; g++)
                {
                    chapter.Groups.Add(new StoryEditor.Catalog.Group(editor.Groups[g].Id, editor.ProposedXp(g), editor.Groups[g].Creatures.Count));
                }
            }
            else
            {
                var local = new Compendium();
                try
                {
                    local.Load(all, RulesFolder.Default, "", folder);
                }
                catch (ContentException)
                {
                }
                foreach (JsonNode? e in j["encounters"] as JsonArray ?? new JsonArray())
                {
                    if (e is not JsonObject group || !FormJson.IsString(group["id"], out string? groupId))
                    {
                        continue;
                    }
                    int levels = 0, creatures = 0;
                    foreach (JsonNode? p in group["creatures"] as JsonArray ?? new JsonArray())
                    {
                        string creature = p is JsonObject o && FormJson.IsString(o["creature"], out string? c) ? c : "";
                        levels += local.Creatures.TryGetValue(creature, out CreatureDefinition? found) ? Math.Max(1, found.Level) : 1;
                        creatures++;
                    }
                    chapter.Groups.Add(new StoryEditor.Catalog.Group(groupId, levels * perLevel, creatures));
                }
            }

            chapter.Dialogues = DialogueFilesOf(folder);
            if (FormJson.IsString(j["quests"], out string? quests) && Resolve(folder, quests) is { Length: > 0 } questPath)
            {
                try
                {
                    QuestJournal journal = QuestJournal.Read(ContentNode.Read(files, questPath));
                    chapter.Quests.AddRange(journal.Quests.Select(q => new StoryEditor.Catalog.Quest(q.Id, q.Title)));
                }
                catch (ContentException)
                {
                }
            }
            // the ending as Cutscene mode has it if it is open
            if (_hooks.TryGetValue(folder, out CutsceneHooks? hooks))
            {
                chapter.Ending = hooks.Cleared.Length == 0 ? "" : hooks.Resolve(hooks.Cleared);
            }
            else if (j["endings"] is JsonObject endings && FormJson.IsString(endings["cleared"], out string? cleared))
            {
                chapter.Ending = Resolve(folder, cleared);
            }
            catalog.Chapters.Add(chapter);
        }

        // travel between chapters, by chapter id; "from" and "to" may be ids or objects with one
        if (ReadObject(files, "adventure.json") is JsonObject adventure)
        {
            catalog.Adventure = true;
            static string IdOf(JsonNode? side) => FormJson.IsString(side, out string? id) ? id
                : side is JsonObject o && FormJson.IsString(o["chapter"], out string? chapter) ? chapter : "";
            foreach (JsonNode? t in adventure["transitions"] as JsonArray ?? new JsonArray())
            {
                if (t is JsonObject travel && travel.ContainsKey("from") && travel.ContainsKey("to"))
                {
                    catalog.Travel.Add((IdOf(travel["from"]), IdOf(travel["to"])));
                }
            }
        }
        return catalog;
    }

    private void CloseStory()
    {
        _story = null;
        _storySaved = "";
        _storyError = "";
        _storyFresh = false;
    }

    private bool StoryToSave(List<Changed> changed)
    {
        if (_story == null)
        {
            return true;
        }
        string text = _story.ToJson();
        if (text == _storySaved)
        {
            return true;
        }
        if (_story.Problems().FirstOrDefault(p => p.Error) is Yorehold.Rules.StoryEditor.Problem wrong)
        {
            Status = "story.json not saved: " + wrong.Text;
            return false;
        }
        changed.Add(new Changed("story.json", text, () => _storySaved = text));
        return true;
    }

    private void StoryProblems(List<CreateProblem> found)
    {
        if (_storyError.Length > 0)
        {
            found.Add(new CreateProblem("story.json", _storyError, true));
        }
        if (_story != null)
        {
            found.AddRange(_story.Problems().Select(p => new CreateProblem("story.json", "story: " + p.Text, p.Error)));
        }
    }
}
