using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Yorehold.Rules;

/// <summary>
/// Stage two of story import with a story model: the book in chunks of pages, each sent with the
/// outline so far, and what comes back checked entry by entry with the outline's own reader. An
/// entry that fails goes back once with what is wrong; one that fails again is dropped and said
/// so. A quote the book doesn't have makes its entry invented. Prompts are the files
/// import/prompt-*.txt in the game's content.
/// </summary>
public sealed partial class StoryReader
{
    /// <summary>About how much of the book goes in one request, in characters (a few thousand tokens).</summary>
    public const int ChunkCharacters = 14000;

    public sealed record Dropped(string Entry, string Why);

    public sealed class Result
    {
        public Outline Outline { get; set; } = new();
        public List<Dropped> Dropped { get; } = new();
        public List<string> Notes { get; } = new();
        public int Calls { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }

    private readonly IStoryModel _model;
    private readonly string _systemPrompt;
    private readonly string _chunkPrompt;
    private readonly string _fixPrompt;

    /// <summary>system is the ruleset folder the adventure is built for: its creatures, items and classes are the ones the model is offered.</summary>
    public StoryReader(IStoryModel model, ContentFiles game, string system = RulesFolder.Default)
    {
        _model = model;
        var compendium = new Compendium();
        compendium.Load(game, system, "");
        OutlineSchemas schemas = OutlineSchemas.Load(game);
        var schemaText = new StringBuilder();
        foreach (OutlineKind kind in Enum.GetValues<OutlineKind>())
        {
            schemaText.Append(Outline.KindName(kind)).Append(": ").AppendLine(schemas.Of(kind));
        }
        _systemPrompt = game.ReadText("import/prompt-system.txt")
            .Replace("{{kinds}}", string.Join(", ", Enum.GetValues<OutlineKind>().Select(Outline.KindName)))
            .Replace("{{schemas}}", schemaText.ToString().TrimEnd())
            .Replace("{{gameCreatures}}", string.Join(", ", compendium.Creatures.Keys))
            .Replace("{{gameItems}}", string.Join(", ", compendium.Items.Keys))
            .Replace("{{gameClasses}}", string.Join(", ", compendium.Classes.Keys));
        _chunkPrompt = game.ReadText("import/prompt-chunk.txt");
        _fixPrompt = game.ReadText("import/prompt-fix.txt");
    }

    /// <summary>The schema every answer is asked to fit: a list of entries, each kind's data checked by the reader afterwards.</summary>
    public static string AnswerSchema()
    {
        var kinds = new JsonArray(Enum.GetValues<OutlineKind>().Select(k => (JsonNode?)Outline.KindName(k)).ToArray());
        var entry = new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("id", "kind", "data", "from"),
            ["properties"] = new JsonObject
            {
                ["id"] = new JsonObject { ["type"] = "string" },
                ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = kinds },
                ["data"] = new JsonObject { ["type"] = "object" },
                ["from"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("invented") },
                        new JsonObject
                        {
                            ["type"] = "object",
                            ["required"] = new JsonArray("page", "quote"),
                            ["properties"] = new JsonObject { ["page"] = new JsonObject { ["type"] = "integer" }, ["quote"] = new JsonObject { ["type"] = "string" } },
                        }),
                },
                ["picture"] = new JsonObject { ["type"] = "string" },
                ["chapter"] = new JsonObject { ["type"] = "string" },
            },
        };
        return CreateJson.Compact(new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("entries"),
            ["properties"] = new JsonObject { ["entries"] = new JsonObject { ["type"] = "array", ["items"] = entry } },
        });
    }

    /// <summary>The book's pages as the prompt shows them: one line per block, marked by what kind it is.</summary>
    public static List<(string Pages, string Text, List<SourceBook.Picture> Pictures)> Chunks(SourceBook book, int size = ChunkCharacters)
    {
        var chunks = new List<(string, string, List<SourceBook.Picture>)>();
        var text = new StringBuilder();
        var pictures = new List<SourceBook.Picture>();
        int first = -1, last = -1;
        void Flush()
        {
            if (text.Length > 0)
            {
                chunks.Add((first == last ? $"{first}" : $"{first} to {last}", text.ToString().TrimEnd(), pictures.ToList()));
            }
            text.Clear();
            pictures.Clear();
            first = -1;
        }
        foreach (SourceBook.Page page in book.Pages)
        {
            var lines = new StringBuilder($"[page {page.Number}]\n");
            foreach (SourceBook.Block block in page.Blocks)
            {
                string mark = block.Kind == "heading" ? "# " : block.Box == "shaded" ? "> " : block.Box == "framed" ? "| " : "";
                lines.Append(mark).Append(block.Text).Append('\n');
            }
            if (text.Length > 0 && text.Length + lines.Length > size)
            {
                Flush();
            }
            if (first < 0)
            {
                first = page.Number;
            }
            last = page.Number;
            text.Append(lines).Append('\n');
            pictures.AddRange(book.Pictures.Where(p => p.Page == page.Number));
        }
        Flush();
        return chunks;
    }

    public async Task<Result> Read(SourceBook book, Outline start, CancellationToken cancel = default)
    {
        var entries = new List<JsonObject>();
        foreach (OutlineEntry e in start.Entries)
        {
            entries.Add((JsonObject)JsonNode.Parse(Outline.EntryJson(e))!);
        }
        var result = new Result();
        Dictionary<string, string> names = BookLayout.PictureNames(book);
        string title = start.Title.Length > 0 ? start.Title : book.Title;
        foreach ((string pages, string text, List<SourceBook.Picture> pictures) in Chunks(book))
        {
            string pictureLines = pictures.Count == 0 ? "(none)" : string.Join("\n", pictures.Select(p =>
                names.TryGetValue(p.File, out string? name) ? $"{p.File} (page {p.Page}), set over or under the name {name}" : $"{p.File} (page {p.Page})"));
            string prompt = _chunkPrompt
                .Replace("{{outline}}", Summary(entries))
                .Replace("{{pictures}}", pictureLines)
                .Replace("{{pages}}", pages)
                .Replace("{{title}}", title)
                .Replace("{{text}}", text);
            List<(JsonObject Entry, string Why)> failed = await AskAndMerge(prompt, entries, book, result, cancel);
            if (failed.Count > 0)
            {
                var errors = new StringBuilder();
                foreach ((JsonObject entry, string why) in failed)
                {
                    errors.AppendLine(CreateJson.Compact(entry)).AppendLine("  wrong: " + why).AppendLine();
                }
                string fix = _fixPrompt.Replace("{{outline}}", Summary(entries)).Replace("{{errors}}", errors.ToString().TrimEnd());
                foreach ((JsonObject entry, string why) in await AskAndMerge(fix, entries, book, result, cancel))
                {
                    result.Dropped.Add(new Dropped(entry["id"]?.ToString() ?? "?", why));
                }
            }
        }
        GivePictures(entries, names);
        // what the layout found that the model can't see keeps when the model sent the entry again:
        // a chapter's map picture and where each place's number is on it
        foreach (OutlineEntry drafted in start.Entries)
        {
            foreach (string key in new[] { "mapPicture", "mapAt" })
            {
                if (drafted.Data[key] is JsonNode found && entries.FirstOrDefault(e => e["id"]?.ToString() == drafted.Id)?["data"] is JsonObject data && data[key] == null)
                {
                    data[key] = found.DeepClone();
                }
            }
        }
        var root = new JsonObject
        {
            ["format"] = Outline.Format,
            ["version"] = Outline.Version,
            ["title"] = title,
            ["system"] = start.System,
            ["entries"] = new JsonArray(entries.Select(e => (JsonNode?)e.DeepClone()).ToArray()),
        };
        result.Outline = Outline.Parse(Outline.FileName, CreateJson.Compact(root));
        return result;
    }

    // one request; every entry in the answer that the outline's reader takes goes in, the rest come back with why
    private async Task<List<(JsonObject Entry, string Why)>> AskAndMerge(string prompt, List<JsonObject> entries, SourceBook book, Result result, CancellationToken cancel)
    {
        StoryAnswer answer = await _model.Ask(new StoryRequest(_systemPrompt, prompt, "outline_entries", AnswerSchema()), cancel);
        result.Calls++;
        result.InputTokens += answer.InputTokens;
        result.OutputTokens += answer.OutputTokens;
        List<JsonObject> offered;
        try
        {
            offered = ((JsonNode.Parse(Unfenced(answer.Text))?["entries"] as JsonArray) ?? new JsonArray()).OfType<JsonObject>().Select(e => (JsonObject)e.DeepClone()).ToList();
        }
        catch (JsonException error)
        {
            result.Notes.Add("an answer was not JSON and was left out: " + error.Message);
            return new List<(JsonObject, string)>();
        }
        foreach (JsonObject entry in offered)
        {
            CheckQuote(entry, book, result);
        }
        // entries can name ones later in the same answer, so go round until nothing more fits
        var waiting = offered;
        var why = new Dictionary<JsonObject, string>();
        bool progress = true;
        while (progress && waiting.Count > 0)
        {
            progress = false;
            foreach (JsonObject entry in waiting.ToList())
            {
                string? problem = TryAdd(entry, entries);
                if (problem == null)
                {
                    waiting.Remove(entry);
                    progress = true;
                }
                else
                {
                    why[entry] = problem;
                }
            }
        }
        return waiting.Select(e => (e, why[e])).ToList();
    }

    // A hero, person or creature the model gave no picture gets the one captioned with its name,
    // if no one else has it: the layout knows whose a picture is better than the model guesses.
    private static void GivePictures(List<JsonObject> entries, Dictionary<string, string> names)
    {
        var taken = entries.Select(e => e["picture"]?.ToString() ?? "").Where(p => p.Length > 0).ToHashSet(StringComparer.Ordinal);
        foreach (JsonObject entry in entries.Where(e => e["kind"]?.ToString() is "hero" or "npc" or "creature" && e["picture"] == null))
        {
            string name = Flat(entry["data"]?["name"]?.ToString() ?? "");
            if (name.Length == 0)
            {
                continue;
            }
            foreach ((string picture, string caption) in names.OrderBy(n => n.Key, StringComparer.Ordinal))
            {
                string called = Flat(caption);
                // "Jezer the Ogre" is captioned JEZER; a caption is the whole name or its first word
                if (!taken.Contains(picture) && (called == name || called == name.Split(' ')[0]))
                {
                    entry["picture"] = picture;
                    taken.Add(picture);
                    break;
                }
            }
        }
    }

    private static string? TryAdd(JsonObject entry, List<JsonObject> entries)
    {
        string id = entry["id"]?.ToString() ?? "";
        var candidate = entries.Where(e => e["id"]?.ToString() != id).Select(e => (JsonNode?)e.DeepClone()).ToList();
        candidate.Add(entry.DeepClone());
        var root = new JsonObject { ["format"] = Outline.Format, ["version"] = Outline.Version, ["entries"] = new JsonArray(candidate.ToArray()) };
        try
        {
            Outline.Parse(Outline.FileName, CreateJson.Compact(root));
        }
        catch (ContentException error)
        {
            return error.Message;
        }
        int at = entries.FindIndex(e => e["id"]?.ToString() == id);
        if (at >= 0)
        {
            entries[at] = entry;
        }
        else
        {
            entries.Add(entry);
        }
        return null;
    }

    // a quote has to be the book's words; one it doesn't have makes the entry invented
    private static void CheckQuote(JsonObject entry, SourceBook book, Result result)
    {
        if (entry["from"] is not JsonObject from)
        {
            return;
        }
        string quote = Flat(from["quote"]?.ToString() ?? "");
        int page = from["page"] is JsonValue p && p.TryGetValue(out int n) ? n : 0;
        bool found = quote.Length > 0 && book.Pages.Any(pg => Math.Abs(pg.Number - page) <= 1 && Flat(string.Join(" ", pg.Blocks.Select(b => b.Text))).Contains(quote, StringComparison.Ordinal));
        if (!found)
        {
            entry["from"] = "invented";
            result.Notes.Add($"{entry["id"]}: its quote is not on page {page} of the book, so it is marked invented");
        }
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NotWord();

    // letters and digits only, so curly quotes, dashes and line breaks don't stop a match
    private static string Flat(string text) => NotWord().Replace(text.ToLowerInvariant(), " ").Trim();

    private static string Unfenced(string text)
    {
        string t = text.Trim();
        if (t.StartsWith("```", StringComparison.Ordinal))
        {
            int start = t.IndexOf('\n');
            int end = t.LastIndexOf("```", StringComparison.Ordinal);
            if (start > 0 && end > start)
            {
                return t[(start + 1)..end];
            }
        }
        return t;
    }

    private static string Summary(List<JsonObject> entries)
    {
        if (entries.Count == 0)
        {
            return "(empty)";
        }
        return string.Join("\n", entries.Select(e =>
        {
            JsonNode? data = e["data"];
            string name = data?["name"]?.ToString() ?? data?["title"]?.ToString() ?? data?["text"]?.ToString() ?? "";
            if (name.Length > 60)
            {
                name = name[..60] + "...";
            }
            return $"{e["id"]} {e["kind"]} {name}".TrimEnd();
        }));
    }
}
