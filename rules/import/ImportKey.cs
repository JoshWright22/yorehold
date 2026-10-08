using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// A book's answer key: what someone who read it says a good import must hold, a line at a time
/// (place 3; a locked way between 5 and 6; a fight in 3 with one orc and one rat; Jeffries says
/// "My name is Jeffries"). An import's score counts the lines its outline meets, which is the one
/// measure that knows what the book means and not only how it is laid out. Keys are written by
/// hand, one per book, as "book-name.key.json" in the scores folder; see CONTENT.md.
/// </summary>
public sealed class ImportKey
{
    public const string Format = "yorehold.import-key";
    public const int Version = 1;
    public const string Ending = ".key.json";

    private static readonly string[] Kinds = { "place", "way", "fight", "hero", "person", "says", "creature", "item", "chest", "quest" };

    private readonly List<ContentNode> _lines = new();

    public string Book { get; private set; } = "";
    /// <summary>Pictures in the book that aren't for the game (a credits page, an advert), as source.json names them.</summary>
    public List<string> NotPictures { get; } = new();
    public int Count => _lines.Count;

    /// <summary>The file a book's key is kept in: the scores folder, named as the book's import folder is.</summary>
    public static string FileFor(string scoresFolder, string book) =>
        Path.Combine(scoresFolder, OutlineBuilder.Slug(BookLayout.CleanTitle(Path.GetFileNameWithoutExtension(book))) + Ending);

    /// <summary>The key for a book, or null when nobody has written one.</summary>
    public static ImportKey? Find(string scoresFolder, string book)
    {
        string file = FileFor(scoresFolder, book);
        return File.Exists(file) ? Parse(Path.GetFileName(file), File.ReadAllText(file)) : null;
    }

    public static ImportKey Parse(string file, string text)
    {
        ContentNode root = ContentNode.Parse(file, text).RequireObject("an answer key");
        root.Only("format", "version", "book", "notPictures", "expect");
        if (root.Text("format", "") != Format)
        {
            throw root.Fail("format", $"is \"{Format}\"");
        }
        if (root.Int("version", 1, 1) > Version)
        {
            throw root.Fail("version", "was written by a newer version of the game");
        }
        var key = new ImportKey { Book = root.Text("book", "", 300) };
        key.NotPictures.AddRange(root.Texts("notPictures"));
        ContentNode expect = root.At("expect");
        if (!expect.IsArray)
        {
            throw expect.Fail("is a list of lines");
        }
        foreach (ContentNode line in expect.Items())
        {
            line.RequireObject("a line is an object");
            switch (KindOf(line))
            {
                case "place":
                    line.Only("place");
                    break;
                case "way":
                    line.Only("way", "how");
                    if (line.Texts("way").Count != 2)
                    {
                        throw line.Fail("way", "is the labels of the two places it joins, like [\"2\", \"3\"]");
                    }
                    if (line.Get("how") is ContentNode how && !Enum.TryParse<LinkWay>(how.AsText(), true, out _))
                    {
                        throw line.Fail("how", "is one of " + string.Join(", ", Enum.GetNames<LinkWay>().Select(n => n.ToLowerInvariant())));
                    }
                    break;
                case "fight":
                    line.Only("fight", "creatures");
                    foreach (KeyValuePair<string, ContentNode> creature in line.Get("creatures")?.Members() ?? Enumerable.Empty<KeyValuePair<string, ContentNode>>())
                    {
                        creature.Value.AsInt(1, 100);
                    }
                    break;
                case "hero":
                    line.Only("hero", "class", "picture");
                    line.Bool("picture", false);
                    break;
                case "person":
                    line.Only("person", "place", "talks");
                    line.Bool("talks", false);
                    break;
                case "says":
                    line.Only("says", "who");
                    break;
                case "chest":
                    line.Only("chest", "holds", "coins");
                    line.Texts("holds");
                    line.Int("coins", 0, 0);
                    break;
                default:
                    line.Only(KindOf(line));
                    break;
            }
            key._lines.Add(line);
        }
        return key;
    }

    // a line is named by its first field: {"fight": "3", ...} is a fight, {"person": "Jeffries", "place": "1"} a person
    private static string KindOf(ContentNode line)
    {
        string kind = line.Members().Select(m => m.Key).FirstOrDefault() ?? "";
        if (!Kinds.Contains(kind))
        {
            throw line.Fail("starts with one of " + string.Join(", ", Kinds));
        }
        if (kind != "way")
        {
            line.At(kind).AsText(400);
        }
        return kind;
    }

    /// <summary>Every line in plain words, and whether the outline meets it.</summary>
    public List<(string Line, bool Met)> Check(Outline outline) => _lines.Select(line => (Describe(line), Met(line, outline))).ToList();

    private static bool Met(ContentNode line, Outline o)
    {
        string kind = KindOf(line);
        string what = kind == "way" ? "" : line.At(kind).AsText();
        switch (kind)
        {
            case "place":
                return At(o, what).Count > 0;
            case "way":
            {
                List<string> ends = line.Texts("way");
                HashSet<string> a = At(o, ends[0]), b = At(o, ends[1]);
                string how = line.Text("how", "");
                return o.OfKind(OutlineKind.Link).Any(l =>
                    ((a.Contains(l.Text("from")) && b.Contains(l.Text("to"))) || (b.Contains(l.Text("from")) && a.Contains(l.Text("to"))))
                    && (how.Length == 0 || l.Text("way", "open") == how));
            }
            case "fight":
            {
                HashSet<string> here = At(o, what);
                List<JsonNode> foes = o.OfKind(OutlineKind.Encounter).Where(e => here.Contains(e.Text("place")))
                    .SelectMany(e => e.Data["creatures"] as JsonArray ?? new JsonArray()).OfType<JsonNode>().ToList();
                if (foes.Count == 0)
                {
                    return false;
                }
                foreach (KeyValuePair<string, ContentNode> wanted in line.Get("creatures")?.Members() ?? Enumerable.Empty<KeyValuePair<string, ContentNode>>())
                {
                    int count = foes.Where(f => Named(CreatureWords(o, f), wanted.Key)).Sum(f => f["count"]?.GetValue<int>() ?? 1);
                    if (count != wanted.Value.AsInt())
                    {
                        return false;
                    }
                }
                return true;
            }
            case "hero":
                return o.OfKind(OutlineKind.Hero).Any(h => Named(h.Text("name"), what)
                    && (line.Text("class", "") is not { Length: > 0 } wantedClass || h.Text("class") == wantedClass)
                    && (!line.Bool("picture", false) || h.Picture.Length > 0 || h.Text("image").Length > 0));
            case "person":
                return o.OfKind(OutlineKind.Npc).Any(n => Named(n.Text("name"), what)
                    && (line.Text("place", "") is not { Length: > 0 } label || At(o, label).Contains(n.Text("place")))
                    && (!line.Bool("talks", false) || (o.Find(n.Text("dialogue"))?.Data["nodes"] as JsonArray)?.Count > 0));
            case "says":
            {
                string who = line.Text("who", "");
                return o.OfKind(OutlineKind.Dialogue).SelectMany(d => d.Data["nodes"] as JsonArray ?? new JsonArray()).Any(n =>
                    ImportScore.Holds(n?["text"]?.ToString() ?? "", what) && (who.Length == 0 || Named(n?["speaker"]?.ToString() ?? "", who)));
            }
            case "creature":
                return o.OfKind(OutlineKind.Creature).Any(c => Named(c.Text("name") + " " + c.Id, what))
                    || o.OfKind(OutlineKind.Encounter).SelectMany(e => e.Data["creatures"] as JsonArray ?? new JsonArray()).OfType<JsonNode>().Any(f => Named(CreatureWords(o, f), what));
            case "item":
                return o.OfKind(OutlineKind.Item).Any(i => Named(i.Text("name") + " " + i.Id, what))
                    || o.OfKind(OutlineKind.Container).SelectMany(c => c.Texts("items")).Any(i => Named(i, what));
            case "chest":
            {
                HashSet<string> here = At(o, what);
                List<OutlineEntry> chests = o.OfKind(OutlineKind.Container).Where(c => here.Contains(c.Text("place"))).ToList();
                List<string> held = chests.SelectMany(c => c.Texts("items")).Select(i => i + " " + (o.Find(i)?.Text("name") ?? "")).ToList();
                return chests.Count > 0
                    && line.Texts("holds").All(wanted => held.Any(h => Named(h, wanted)))
                    // the key gives gold as the book does; the game counts copper, a hundred to the gold piece
                    && (!line.Has("coins") || chests.Sum(c => c.Data["coins"]?.GetValue<int>() ?? 0) == line.Int("coins", 0) * 100);
            }
            case "quest":
                return o.OfKind(OutlineKind.Quest).Any(q => Named(q.Text("title") + " " + q.Text("description"), what));
            default:
                return false;
        }
    }

    // the ids of the places the book labels this way
    private static HashSet<string> At(Outline o, string label) =>
        o.OfKind(OutlineKind.Place).Where(p => string.Equals(p.Text("label"), label, StringComparison.OrdinalIgnoreCase)).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

    // everything a foe in a fight is called: its creature's id and name, and its own name there
    private static string CreatureWords(Outline o, JsonNode foe)
    {
        string id = foe["creature"]?.ToString() ?? "";
        return $"{id} {foe["name"]} {o.Find(id)?.Text("name")}";
    }

    // "necklace|gem": any of the ways the key gives a name, as whole words
    private static bool Named(string text, string wanted) => wanted.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(w => ImportScore.Holds(text, w));

    private static string Describe(ContentNode line)
    {
        string kind = KindOf(line);
        string what = kind == "way" ? "" : line.At(kind).AsText();
        switch (kind)
        {
            case "place":
                return $"place {what}";
            case "way":
                return $"a way between {line.Texts("way")[0]} and {line.Texts("way")[1]}" + (line.Text("how", "") is { Length: > 0 } how ? $", {how}" : "");
            case "fight":
                string foes = string.Join(", ", (line.Get("creatures")?.Members() ?? Enumerable.Empty<KeyValuePair<string, ContentNode>>()).Select(c => $"{c.Value.AsInt()} {c.Key}"));
                return $"a fight in {what}" + (foes.Length > 0 ? $": {foes}" : "");
            case "hero":
                return $"the hero {what}" + (line.Text("class", "") is { Length: > 0 } c ? $", a {c}" : "") + (line.Bool("picture", false) ? ", with a picture" : "");
            case "person":
                return what + (line.Text("place", "") is { Length: > 0 } label ? $" in {label}" : "") + (line.Bool("talks", false) ? ", to talk to" : "");
            case "says":
                return (line.Text("who", "") is { Length: > 0 } who ? who : "someone") + $" says “{what}”";
            case "chest":
                var holds = line.Texts("holds");
                if (line.Has("coins"))
                {
                    holds.Add($"{line.Int("coins", 0)} gold");
                }
                return $"something to open in {what}" + (holds.Count > 0 ? $" holding {string.Join(", ", holds)}" : "");
            default:
                return $"the {kind} {what}";
        }
    }
}
