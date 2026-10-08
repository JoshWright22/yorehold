using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// How much of a book an import got into the game, part by part: its pictures, its words, its
/// numbered places, the passages to read out, the ways between places, the shape of its map and
/// what people say. Each part counts against something the book itself shows (a picture on a page,
/// a shaded box, a number on the map), so the count needs no one to have read the book. What only
/// a reader can say (how many fights, who is in them) is counted against the book's answer key
/// when there is one (ImportKey). Kept as import/score.json; see CONTENT.md.
/// </summary>
public sealed class ImportScore
{
    public const string Format = "yorehold.import-score";
    public const int Version = 1;
    public const string FileName = "score.json";
    public const string HistoryFile = "history.jsonl";

    /// <summary>Words in a row that must match for a passage to count as the book's own.</summary>
    private const int Run = 3;
    /// <summary>The share of a passage's runs that must be found for it to count as there.</summary>
    private const double There = 0.6;
    /// <summary>A line of talk longer than this many words is a passage, not something said.</summary>
    public const int LongLine = 60;

    /// <summary>One part of the score: Found of Of, what is missing, and plain facts with no count.</summary>
    public sealed record Part(string Id, string Name, int Found, int Of, List<string> Missing, List<string> Facts)
    {
        public bool Scored => Of > 0;
        public int Percent => Of > 0 ? (int)Math.Round(100.0 * Found / Of) : 0;
    }

    /// <summary>What the builder said of its own work: its report lines and where it put each place's room.</summary>
    public sealed record BuildReport(List<(string Entry, string Text)> Lines, Dictionary<string, (string Chapter, int X, int Y, int W, int H)> Rooms)
    {
        public static BuildReport? Load(string importFolder)
        {
            string file = Path.Combine(importFolder, OutlineBuilder.ReportFile);
            if (!File.Exists(file) || JsonNode.Parse(File.ReadAllText(file)) is not JsonObject root)
            {
                return null;
            }
            var report = new BuildReport(new(), new(StringComparer.Ordinal));
            foreach (JsonNode? line in root["lines"] as JsonArray ?? new JsonArray())
            {
                report.Lines.Add((line?["entry"]?.ToString() ?? "", line?["text"]?.ToString() ?? ""));
            }
            foreach (KeyValuePair<string, JsonNode?> room in root["rooms"] as JsonObject ?? new JsonObject())
            {
                if (room.Value is JsonObject at && at["at"] is JsonArray { Count: 4 } box)
                {
                    report.Rooms[room.Key] = (at["chapter"]?.ToString() ?? "", box[0]!.GetValue<int>(), box[1]!.GetValue<int>(), box[2]!.GetValue<int>(), box[3]!.GetValue<int>());
                }
            }
            return report;
        }
    }

    public string Book { get; private set; } = "";
    /// <summary>The story model's run as its log's first line says it; empty when none ran.</summary>
    public string ModelRun { get; private set; } = "";
    public bool ModelRan => ModelRun.Length > 0;
    public bool Built { get; private set; }
    public bool HasKey { get; private set; }
    public List<Part> Parts { get; } = new();

    /// <summary>The mean of the counted parts, 0 to 100.</summary>
    public int Overall => Parts.Any(p => p.Scored) ? (int)Math.Round(Parts.Where(p => p.Scored).Average(p => p.Percent)) : 0;

    public Part? Find(string id) => Parts.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// The score of the import in a folder. Kept is its outline without what the writer dropped;
    /// package is the built package's folder, or null before it is built (the map's shape and
    /// which pictures were copied are then not known).
    /// </summary>
    public static ImportScore Of(string importFolder, Outline kept, string? package, ImportKey? key)
    {
        string log = Path.Combine(importFolder, StoryImport.ModelLogFile);
        if (!File.Exists(log))
        {
            // what the tests' look-by-hand run writes (BookReaderTests)
            log = Path.Combine(importFolder, "model-run.txt");
        }
        string modelRun = File.Exists(log) ? File.ReadLines(log).FirstOrDefault() ?? "ran" : "";
        HashSet<string>? copied = null;
        if (package != null && File.Exists(Path.Combine(package, "content.json")))
        {
            string pictures = Path.Combine(package, "pictures");
            copied = Directory.Exists(pictures)
                ? Directory.GetFiles(pictures).Select(f => Path.GetFileNameWithoutExtension(f)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        return Of(SourceBook.Load(importFolder), kept, copied != null ? BuildReport.Load(importFolder) : null, modelRun, key, copied);
    }

    /// <summary>The same from what is already read; copied is the names (no folder, no ending) of the pictures in the built package.</summary>
    public static ImportScore Of(SourceBook book, Outline kept, BuildReport? report, string modelRun, ImportKey? key, IReadOnlySet<string>? copied)
    {
        var score = new ImportScore { Book = book.File, ModelRun = modelRun, Built = copied != null, HasKey = key != null };
        var game = new Corpus(kept.Entries.Where(e => e.Kind != OutlineKind.Note).SelectMany(e => Strings(e.Data)));
        var notes = new Corpus(kept.OfKind(OutlineKind.Note).SelectMany(e => Strings(e.Data)));
        var talk = new Corpus(kept.OfKind(OutlineKind.Dialogue).SelectMany(e => Strings(e.Data)));
        score.Parts.Add(Pictures(book, kept, key, copied));
        score.Parts.Add(BookWords(book, game, notes));
        score.Parts.Add(Places(book, kept));
        score.Parts.Add(ReadOut(book, game));
        score.Parts.Add(Ways(kept, report));
        score.Parts.Add(Shape(kept, report));
        score.Parts.Add(Spoken(book, kept, talk, report));
        score.Parts.Add(Cast(kept));
        if (key != null)
        {
            List<(string Line, bool Met)> lines = key.Check(kept);
            score.Parts.Add(new Part("key", "The answer key", lines.Count(l => l.Met), lines.Count,
                lines.Where(l => !l.Met).Select(l => l.Line).ToList(), new List<string>()));
        }
        return score;
    }

    private static Part Pictures(SourceBook book, Outline kept, ImportKey? key, IReadOnlySet<string>? copied)
    {
        Dictionary<string, string> names = BookLayout.PictureNames(book);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (OutlineEntry e in kept.Entries.Where(e => e.Kind != OutlineKind.Note))
        {
            used.Add(e.Picture);
            used.Add(e.Text("mapPicture"));
            used.Add(e.Text("image"));
        }
        var missing = new List<string>();
        int found = 0, of = 0;
        foreach (SourceBook.Picture picture in book.Pictures)
        {
            if (key != null && key.NotPictures.Contains(picture.File))
            {
                continue;
            }
            of++;
            bool there = copied != null ? copied.Contains(Path.GetFileNameWithoutExtension(picture.File)) : used.Contains(picture.File);
            if (there)
            {
                found++;
                continue;
            }
            string name = names.TryGetValue(picture.File, out string? called) ? $", by the heading {called}" : "";
            missing.Add($"page {picture.Page}: {picture.File}{name}, {(used.Contains(picture.File) ? "named by an entry but not copied" : "no entry uses it")}");
        }
        var facts = new List<string>();
        if (book.Skipped.Count > 0)
        {
            facts.Add($"the reader left out {book.Skipped.Count}: {string.Join("; ", book.Skipped.Take(4))}");
        }
        if (key != null && key.NotPictures.Count > 0)
        {
            facts.Add($"{key.NotPictures.Count} the key says aren't for the game are not counted");
        }
        return new Part("pictures", "Pictures", found, of, missing, facts);
    }

    // every paragraph of the book, by how much of it turns up in what the game shows or says
    private static Part BookWords(SourceBook book, Corpus game, Corpus notes)
    {
        double found = 0;
        int of = 0, noted = 0;
        var gaps = new List<(int Words, string Line)>();
        foreach (SourceBook.Page page in book.Pages)
        {
            foreach (SourceBook.Block block in page.Blocks.Where(b => b.Kind == "text"))
            {
                List<string> words = Words(block.Text);
                if (words.Count < 8)
                {
                    continue; // page numbers, captions and table crumbs
                }
                of += words.Count;
                double share = game.Share(words);
                found += share * words.Count;
                if (share >= There)
                {
                    continue;
                }
                bool asNote = notes.Share(words) >= There;
                if (asNote)
                {
                    noted += words.Count;
                }
                gaps.Add((words.Count, $"page {page.Number}: “{Start(block.Text)}” ({words.Count} words{(asNote ? ", kept as a note" : "")})"));
            }
        }
        var facts = new List<string>();
        if (noted > 0)
        {
            facts.Add($"{noted} words are kept only as notes, which the game doesn't play");
        }
        facts.Add("a book's credits, rules and advice are counted too, so this never reaches the whole");
        return new Part("words", "The book's words in the game", (int)Math.Round(found), of,
            gaps.OrderByDescending(g => g.Words).Take(8).Select(g => g.Line).ToList(), facts);
    }

    private static Part Places(SourceBook book, Outline kept)
    {
        List<BookLayout.NumberedPlace> numbered = BookLayout.Places(book);
        List<OutlineEntry> places = kept.OfKind(OutlineKind.Place).ToList();
        if (numbered.Count == 0)
        {
            return new Part("places", "Places", 0, 0, new(), new() { $"{places.Count} places; the book numbers none, so there is nothing to count them against" });
        }
        var labels = places.Select(p => p.Text("label").ToUpperInvariant()).ToHashSet();
        return new Part("places", "Numbered places", numbered.Count(n => labels.Contains(n.Label)), numbered.Count,
            numbered.Where(n => !labels.Contains(n.Label)).Select(n => $"page {n.Page}: {n.Heading}").ToList(),
            places.Count > numbered.Count ? new() { $"{places.Count - numbered.Count} more places than the book numbers" } : new());
    }

    private static Part ReadOut(SourceBook book, Corpus game)
    {
        var missing = new List<string>();
        int found = 0, of = 0;
        foreach (SourceBook.Page page in book.Pages)
        {
            foreach (SourceBook.Block block in page.Blocks.Where(b => b.Kind == "text" && b.Box == "shaded"))
            {
                List<string> words = Words(block.Text);
                if (words.Count < 3)
                {
                    continue;
                }
                of++;
                if (game.Share(words) >= There)
                {
                    found++;
                }
                else
                {
                    missing.Add($"page {page.Number}: “{Start(block.Text)}”");
                }
            }
        }
        return new Part("readout", "Passages to read out", found, of, missing,
            of == 0 ? new() { "the book sets no passages apart in shaded boxes" } : new());
    }

    private static Part Ways(Outline kept, BuildReport? report)
    {
        List<OutlineEntry> places = kept.OfKind(OutlineKind.Place).ToList();
        List<OutlineEntry> links = kept.OfKind(OutlineKind.Link).ToList();
        var joined = links.SelectMany(l => new[] { l.Text("from"), l.Text("to") }).ToHashSet(StringComparer.Ordinal);
        var facts = new List<string>();
        if (links.Count > 0)
        {
            facts.Add(string.Join(", ", links.GroupBy(l => l.Text("way", "open")).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Count()} {g.Key}")));
        }
        if (report != null)
        {
            Count(facts, report, "a corridor joins them", "couldn't sit side by side and are joined by a corridor");
            Count(facts, report, "no walk from the start reaches it", "can't be walked to from the start");
            Count(facts, report, "is played as", "ways are played as something simpler (a jump, a climb, a secret door)");
        }
        return new Part("ways", "Ways between places", places.Count(p => joined.Contains(p.Id)), places.Count > 1 ? places.Count : 0,
            places.Count > 1 ? places.Where(p => !joined.Contains(p.Id)).Select(p => $"{Label(p)}: the outline gives no way in; the builder joins it to the place before").ToList() : new(),
            facts);
    }

    // do the rooms lie the way the book's map draws them: for each two places, left or right, over or under
    private static Part Shape(Outline kept, BuildReport? report)
    {
        List<OutlineEntry> places = kept.OfKind(OutlineKind.Place).ToList();
        var facts = new List<string>
        {
            "rooms are plain boxes cut from each place's size; the walls the book's map draws are not read yet",
        };
        int standIn = places.Count(p => p.Data["size"] is JsonArray { Count: 2 } s && s[0]!.GetValue<int>() == 8 && s[1]!.GetValue<int>() == 8);
        if (standIn > 0)
        {
            facts.Add($"{standIn} of {places.Count} places are 8 by 8, the size given when the book's isn't known");
        }
        var spots = new List<(OutlineEntry Place, double X, double Y)>();
        foreach (OutlineEntry p in places)
        {
            if (p.Data["mapAt"] is JsonArray { Count: 2 } at)
            {
                spots.Add((p, at[0]!.GetValue<double>(), at[1]!.GetValue<double>()));
            }
        }
        if (!kept.OfKind(OutlineKind.Chapter).Any(c => c.Text("mapPicture").Length > 0))
        {
            facts.Insert(0, "no picture in the book was found to be its map");
            return new Part("shape", "The map's shape", 0, 0, new(), facts);
        }
        facts.Insert(0, $"{spots.Count} of {places.Count} places have their number found on the book's map");
        if (report == null)
        {
            facts.Add("not built yet, so where the rooms lie isn't known");
            return new Part("shape", "The map's shape", 0, 0, new(), facts);
        }
        int found = 0, of = 0;
        var wrong = new List<string>();
        for (int i = 0; i < spots.Count; i++)
        {
            for (int j = i + 1; j < spots.Count; j++)
            {
                if (!report.Rooms.TryGetValue(spots[i].Place.Id, out var a) || !report.Rooms.TryGetValue(spots[j].Place.Id, out var b) || a.Chapter != b.Chapter)
                {
                    continue;
                }
                bool across = Agrees(spots[j].X - spots[i].X, b.X + b.W / 2.0 - (a.X + a.W / 2.0), ref found, ref of);
                bool down = Agrees(spots[j].Y - spots[i].Y, b.Y + b.H / 2.0 - (a.Y + a.H / 2.0), ref found, ref of);
                if (!across || !down)
                {
                    wrong.Add($"{Label(spots[i].Place)} and {Label(spots[j].Place)} lie {(across ? "over and under" : "left and right")} each other the wrong way round");
                }
            }
        }
        return new Part("shape", "The map's shape", found, of, wrong.Take(8).ToList(), facts);
    }

    // numbers nearly level on the map say nothing about which is left of which
    private static bool Agrees(double onMap, double inGame, ref int found, ref int of)
    {
        if (Math.Abs(onMap) < 0.08)
        {
            return true;
        }
        of++;
        bool same = Math.Sign(onMap) == Math.Sign(inGame);
        if (same)
        {
            found++;
        }
        return same;
    }

    // a paragraph that opens with a quotation mark is someone speaking
    private static Part Spoken(SourceBook book, Outline kept, Corpus talk, BuildReport? report)
    {
        var missing = new List<string>();
        int found = 0, of = 0;
        foreach (SourceBook.Page page in book.Pages)
        {
            foreach (SourceBook.Block block in page.Blocks.Where(b => b.Kind == "text" && b.Box.Length == 0))
            {
                string text = block.Text.TrimStart();
                List<string> words = Words(text);
                if (text.Length == 0 || text[0] is not ('“' or '"') || words.Count < 4)
                {
                    continue;
                }
                of++;
                if (talk.Share(words) >= There)
                {
                    found++;
                }
                else
                {
                    missing.Add($"page {page.Number}: {Start(text)}");
                }
            }
        }
        var bookText = new Corpus(book.Pages.SelectMany(p => p.Blocks).Select(b => b.Text));
        int lines = 0, fromBook = 0, tooLong = 0, deadEnds = 0;
        List<OutlineEntry> talks = kept.OfKind(OutlineKind.Dialogue).ToList();
        foreach (OutlineEntry dialogue in talks)
        {
            JsonArray nodes = dialogue.Data["nodes"] as JsonArray ?? new JsonArray();
            if (!nodes.Any(n => n?["choices"] is JsonArray { Count: > 0 }))
            {
                deadEnds++;
            }
            foreach (JsonNode? node in nodes)
            {
                List<string> words = Words(node?["text"]?.ToString() ?? "");
                lines++;
                fromBook += bookText.Share(words) >= There ? 1 : 0;
                tooLong += words.Count > LongLine ? 1 : 0;
            }
        }
        var facts = new List<string>();
        if (lines > 0)
        {
            facts.Add($"{lines} lines in {talks.Count} conversations; {fromBook} are the book's own words, {lines - fromBook} are written by the model");
        }
        else
        {
            facts.Add("no conversations");
        }
        if (tooLong > 0)
        {
            facts.Add($"{tooLong} lines run over {LongLine} words, a passage rather than something said");
        }
        if (deadEnds > 0)
        {
            facts.Add($"{deadEnds} conversations give the player nothing to answer");
        }
        int readByRoom = Math.Max(0, kept.OfKind(OutlineKind.Place).Count(p => p.Texts("readAloud").Count > 0) - 1);
        if (readByRoom > 0)
        {
            facts.Add($"{readByRoom} passages are read out as narration on entering their room");
        }
        if (report != null)
        {
            Count(facts, report, "has nothing to say in the book", "people have a stand-in line, the book giving them nothing to say");
            Count(facts, report, "nobody starts this conversation yet", "conversations are started by no one");
        }
        return new Part("spoken", "What people say", found, of, missing.Take(8).ToList(), facts);
    }

    // counts with nothing in the layout to hold them to; the answer key is what judges these
    private static Part Cast(Outline kept)
    {
        int Count(OutlineKind kind) => kept.OfKind(kind).Count();
        List<OutlineEntry> heroes = kept.OfKind(OutlineKind.Hero).ToList();
        var fought = kept.OfKind(OutlineKind.Encounter).Select(e => e.Text("place")).ToHashSet(StringComparer.Ordinal);
        List<OutlineEntry> places = kept.OfKind(OutlineKind.Place).ToList();
        var facts = new List<string>
        {
            $"{Count(OutlineKind.Encounter)} fights in {fought.Count} of {places.Count} places, {Count(OutlineKind.Creature)} creatures of the book's own",
            $"{heroes.Count} heroes ({heroes.Count(h => h.Picture.Length > 0)} with a picture), {Count(OutlineKind.Npc)} people, {Count(OutlineKind.Quest)} quests",
            $"{Count(OutlineKind.Item)} items, {Count(OutlineKind.Container)} chests, {Count(OutlineKind.Note)} notes on what the game can't play",
        };
        if (Count(OutlineKind.Encounter) == 0)
        {
            facts.Add("no fights at all: the book has no fight boxes the layout can read (foes with hit point boxes); the story model finds the rest");
        }
        return new Part("cast", "Fights, people and things", 0, 0, new(), facts);
    }

    private static void Count(List<string> facts, BuildReport report, string said, string means)
    {
        int count = report.Lines.Count(l => l.Text.Contains(said, StringComparison.Ordinal));
        if (count > 0)
        {
            facts.Add($"{count} {means}");
        }
    }

    private static string Label(OutlineEntry place) => place.Text("label") is { Length: > 0 } label ? $"{label}: {place.Text("name")}" : place.Text("name");

    private static string Start(string text)
    {
        string line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= 70 ? line : line[..70].TrimEnd() + "...";
    }

    private static IEnumerable<string> Strings(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject fields:
                return fields.SelectMany(f => Strings(f.Value));
            case JsonArray list:
                return list.SelectMany(Strings);
            case JsonValue value when value.TryGetValue(out string? text):
                return new[] { text };
            default:
                return Array.Empty<string>();
        }
    }

    /// <summary>A text as its words, small letters and no marks, so "Orc’s" and "orcs" are one word.</summary>
    public static List<string> Words(string text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(char.ToLowerInvariant(c));
            }
            else if (c is not ('\'' or '’') && word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }
        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }
        return words;
    }

    /// <summary>Whether the words of a phrase stand together, in order, somewhere in a text.</summary>
    public static bool Holds(string text, string phrase)
    {
        List<string> wanted = Words(phrase);
        return wanted.Count > 0 && (" " + string.Join(' ', Words(text)) + " ").Contains(" " + string.Join(' ', wanted) + " ", StringComparison.Ordinal);
    }

    // Texts to look passages up in. A passage is there by the share of its runs of three words that
    // are, which lets a model's small changes through and keeps a retelling out.
    private sealed class Corpus
    {
        private readonly HashSet<string> _runs = new(StringComparer.Ordinal);
        private readonly StringBuilder _all = new(" ");

        public Corpus(IEnumerable<string> texts)
        {
            foreach (string text in texts)
            {
                List<string> words = Words(text);
                for (int i = 0; i + Run <= words.Count; i++)
                {
                    _runs.Add(string.Join(' ', words.GetRange(i, Run)));
                }
                _all.Append(string.Join(' ', words)).Append(" | ");
            }
        }

        public double Share(List<string> words)
        {
            if (words.Count == 0)
            {
                return 0;
            }
            if (words.Count < Run)
            {
                return _all.ToString().Contains(" " + string.Join(' ', words) + " ", StringComparison.Ordinal) ? 1 : 0;
            }
            int runs = words.Count - Run + 1, found = 0;
            for (int i = 0; i < runs; i++)
            {
                found += _runs.Contains(string.Join(' ', words.GetRange(i, Run))) ? 1 : 0;
            }
            return (double)found / runs;
        }
    }

    public JsonObject ToJson()
    {
        var parts = new JsonArray();
        foreach (Part part in Parts)
        {
            var entry = new JsonObject { ["id"] = part.Id, ["name"] = part.Name };
            if (part.Scored)
            {
                entry["found"] = part.Found;
                entry["of"] = part.Of;
            }
            entry["missing"] = CreateJson.Texts(part.Missing);
            entry["facts"] = CreateJson.Texts(part.Facts);
            parts.Add(entry);
        }
        return new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["book"] = Book,
            ["modelRun"] = ModelRun,
            ["built"] = Built,
            ["key"] = HasKey,
            ["overall"] = Overall,
            ["parts"] = parts,
        };
    }

    public void Save(string importFolder)
    {
        Directory.CreateDirectory(importFolder);
        File.WriteAllText(Path.Combine(importFolder, FileName), CreateJson.Write(ToJson()) + "\n");
    }

    /// <summary>One line added to the scores folder's history, so a change to the import can be held against the runs before it.</summary>
    public void AddToHistory(string scoresFolder, string package)
    {
        var parts = new JsonObject();
        foreach (Part part in Parts.Where(p => p.Scored))
        {
            parts[part.Id] = new JsonArray(part.Found, part.Of);
        }
        var line = new JsonObject
        {
            ["at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["book"] = Book,
            ["package"] = Path.GetFileName(package),
            ["modelRun"] = ModelRun,
            ["overall"] = Overall,
            ["parts"] = parts,
        };
        Directory.CreateDirectory(scoresFolder);
        File.AppendAllText(Path.Combine(scoresFolder, HistoryFile), CreateJson.Compact(line) + "\n");
    }

    /// <summary>The headline: what to say first to someone who has just imported a book.</summary>
    public string Headline() => ModelRan
        ? $"Import score {Overall} of 100{(HasKey ? "" : " (no answer key for this book)")}"
        : $"Import score {Overall} of 100{(HasKey ? "" : " (no answer key for this book)")}. No story model ran: only what the book's layout shows was read";

    /// <summary>The whole score as plain lines, for a log.</summary>
    public List<string> Lines()
    {
        var lines = new List<string> { Headline() };
        foreach (Part part in Parts)
        {
            lines.Add(part.Scored ? $"  {part.Name}: {part.Found} of {part.Of}" : $"  {part.Name}");
            lines.AddRange(part.Facts.Select(f => "      " + f));
            lines.AddRange(part.Missing.Select(m => "    - " + m));
        }
        return lines;
    }
}
