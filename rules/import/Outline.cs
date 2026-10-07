using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>What an outline entry is: a kind of content file, a part of a chapter, or one of the outline's own kinds.</summary>
public enum OutlineKind
{
    Adventure,
    Chapter,
    Hero,
    Creature,
    Item,
    Npc,
    Dialogue,
    Quest,
    Place,
    Link,
    Encounter,
    Container,
    Trigger,
    Note,
}

/// <summary>How a link between two places is crossed.</summary>
public enum LinkWay
{
    Open,
    Door,
    Locked,
    Secret,
    Climb,
    Jump,
}

/// <summary>Where an entry's words came from: a page of the book and the words quoted, or nowhere (invented).</summary>
public sealed record OutlineSource(int Page, string Quote)
{
    public static readonly OutlineSource Invented = new(0, "");
    public bool IsInvented => Page == 0;
}

/// <summary>
/// One thing the book holds, in the shape of the game's own data. Data is that kind's content
/// file (a creature file, a dialogue file...) or, for the outline's own kinds, the fields
/// described in CONTENT.md; Picture is one of the book's pictures, "pictures/p7-1.png".
/// </summary>
public sealed class OutlineEntry
{
    public string Id { get; init; } = "";
    public OutlineKind Kind { get; init; }
    public JsonObject Data { get; init; } = new();
    public OutlineSource From { get; init; } = OutlineSource.Invented;
    public string Picture { get; init; } = "";
    /// <summary>The chapter entry it belongs to; empty for the first chapter, or for kinds that belong to the whole adventure.</summary>
    public string Chapter { get; init; } = "";

    public string Text(string key, string fallback = "") => Data[key] is JsonValue v && v.TryGetValue(out string? s) ? s : fallback;

    public List<string> Texts(string key) => Data[key] is JsonArray list
        ? list.Select(n => n is JsonValue v && v.TryGetValue(out string? s) ? s : "").Where(s => s.Length > 0).ToList()
        : new List<string>();
}

/// <summary>
/// import/outline.json: the book cut down to the game's own data (see CONTENT.md, Story import
/// files). The reader checks every entry with the game's readers where the entry is a content
/// file, and its own checks for the outline's kinds, so an outline that reads is one the builder
/// can turn into a package; what only the game's content can answer (a class or creature named
/// by id) is checked by Check against it.
/// </summary>
public sealed class Outline
{
    public const string Format = "yorehold.outline";
    public const int Version = 1;
    public const string FileName = "outline.json";

    public string Title { get; set; } = "";
    /// <summary>The rules the book was written for, as a table name in import/systems/ ("dnd-3.0"); empty when it has none.</summary>
    public string System { get; set; } = "";
    public List<OutlineEntry> Entries { get; } = new();

    public IEnumerable<OutlineEntry> OfKind(OutlineKind kind) => Entries.Where(e => e.Kind == kind);

    public OutlineEntry? Find(string id) => Entries.FirstOrDefault(e => e.Id == id);

    /// <summary>The kinds as written in the file ("creature", "npc"...).</summary>
    public static string KindName(OutlineKind kind) => kind.ToString().ToLowerInvariant();

    public static Outline Load(string importFolder)
    {
        string file = Path.Combine(importFolder, FileName);
        return Parse(FileName, File.ReadAllText(file));
    }

    public void Save(string importFolder)
    {
        Directory.CreateDirectory(importFolder);
        File.WriteAllText(Path.Combine(importFolder, FileName), ToJson());
    }

    public static Outline Parse(string file, string text) => Read(ContentNode.Parse(file, text));

    public static Outline Read(ContentNode root)
    {
        root.RequireObject("an outline is a JSON object");
        root.Only("format", "version", "title", "system", "entries");
        if (root.At("format").AsText() != Format)
        {
            throw root.Fail("format", $"is \"{Format}\"");
        }
        int version = root.At("version").AsInt(1, 1000);
        if (version > Version)
        {
            throw root.Fail("version", $"is {version}, newer than this game reads ({Version})");
        }
        var outline = new Outline { Title = root.Text("title", ""), System = root.Text("system", "") };
        ContentNode entries = root.At("entries");
        if (!entries.IsArray)
        {
            throw entries.Fail("is a list of entries");
        }
        foreach (ContentNode node in entries.Items())
        {
            OutlineEntry entry = ReadEntry(node);
            if (outline.Find(entry.Id) != null)
            {
                throw node.Fail("id", $"\"{entry.Id}\" is used by two entries");
            }
            outline.Entries.Add(entry);
        }
        outline.CheckLinks(entries);
        return outline;
    }

    private static OutlineEntry ReadEntry(ContentNode node)
    {
        node.RequireObject("an entry is an object");
        node.Only("id", "kind", "data", "from", "picture", "chapter");
        string id = node.At("id").AsId();
        string kindName = node.At("kind").AsText();
        if (!Enum.TryParse(kindName, true, out OutlineKind kind) || KindName(kind) != kindName)
        {
            throw node.Fail("kind", "is one of " + string.Join(", ", Enum.GetValues<OutlineKind>().Select(KindName)));
        }
        ContentNode data = node.At("data").RequireObject("data is an object");
        CheckData(kind, id, data);
        string picture = node.Text("picture", "");
        if (picture.Length > 0 && (!ContentFiles.IsContentPath(picture) || !picture.StartsWith("pictures/", StringComparison.Ordinal)))
        {
            throw node.Fail("picture", "is one of the book's pictures, like pictures/p7-1.png");
        }
        return new OutlineEntry
        {
            Id = id,
            Kind = kind,
            Data = (JsonObject)JsonNode.Parse(data.Raw())!,
            From = ReadSource(node),
            Picture = picture,
            Chapter = node.Get("chapter") is ContentNode chapter ? chapter.AsId() : "",
        };
    }

    private static OutlineSource ReadSource(ContentNode node)
    {
        if (node.Get("from") is not ContentNode source || (source.IsString && source.AsText() == "invented"))
        {
            return OutlineSource.Invented;
        }
        if (!source.IsObject)
        {
            throw node.Fail("from", "is \"invented\" or {\"page\", \"quote\"}");
        }
        source.Only("page", "quote");
        return new OutlineSource(source.At("page").AsInt(1, 100000), source.At("quote").AsText(4000));
    }

    // Content kinds go through the game's own readers; the outline's kinds are checked here.
    private static void CheckData(OutlineKind kind, string id, ContentNode data)
    {
        switch (kind)
        {
            case OutlineKind.Creature:
                SameId(data, id);
                CreatureDefinition.Read(WithId(data, id));
                break;
            case OutlineKind.Item:
                SameId(data, id);
                ItemDefinition.Read(WithId(data, id));
                break;
            case OutlineKind.Dialogue:
                SameId(data, id);
                Dialogue.Read(WithId(data, id));
                break;
            case OutlineKind.Quest:
                SameId(data, id);
                var journal = new JsonObject { ["quests"] = new JsonArray(JsonNode.Parse(WithId(data, id).Raw())) };
                QuestJournal.Read(new ContentNode(JsonDocument.Parse(CreateJson.Compact(journal)).RootElement.Clone(), data.File, data.Path));
                break;
            case OutlineKind.Adventure:
                data.Only("title", "description", "level");
                data.At("title").AsText(200);
                data.Int("level", 1, 1, 20);
                break;
            case OutlineKind.Chapter:
                data.Only("title", "intro", "level", "completeWhen");
                data.At("title").AsText(200);
                data.Texts("intro");
                data.Int("level", 1, 1, 20);
                data.Flags("completeWhen");
                break;
            case OutlineKind.Hero:
                data.Only("name", "class", "race", "color", "image", "description");
                data.At("name").AsText(80);
                data.At("class").AsId();
                if (data.Get("color") is ContentNode color)
                {
                    ContentParts.ColorFrom(color);
                }
                break;
            case OutlineKind.Npc:
                data.Only("name", "creature", "place", "dialogue", "merchant", "color", "description");
                data.At("name").AsText(80);
                data.At("place").AsId();
                if (data.Get("color") is ContentNode npcColor)
                {
                    ContentParts.ColorFrom(npcColor);
                }
                break;
            case OutlineKind.Place:
                data.Only("name", "label", "size", "readAloud", "description", "dark", "outdoors");
                data.At("name").AsText(120);
                data.Text("label", "", 20);
                ContentNode size = data.At("size");
                if (!size.IsArray || size.Count != 2 || size.Items().Any(s => !s.IsWhole || s.AsInt() < 2 || s.AsInt() > 60))
                {
                    throw size.Fail("is [width, height] in squares, each 2 to 60");
                }
                data.Texts("readAloud");
                data.Bool("dark", false);
                data.Bool("outdoors", false);
                break;
            case OutlineKind.Link:
                data.Only("from", "to", "way", "key", "check", "description");
                data.At("from").AsId();
                data.At("to").AsId();
                string way = data.Text("way", "open");
                if (!Enum.TryParse(way, true, out LinkWay parsed) || parsed.ToString().ToLowerInvariant() != way)
                {
                    throw data.Fail("way", "is one of " + string.Join(", ", Enum.GetNames<LinkWay>().Select(n => n.ToLowerInvariant())));
                }
                if (data.Get("key") is ContentNode key)
                {
                    key.AsId();
                }
                if (data.Get("check") is ContentNode check)
                {
                    check.Only("skill", "difficulty");
                    check.At("skill").AsId();
                    check.At("difficulty").AsInt(1, 60);
                }
                if (parsed == LinkWay.Locked && data.Get("key") == null && data.Get("check") == null)
                {
                    throw data.Fail("way", "a locked link needs a key or a check (or both)");
                }
                break;
            case OutlineKind.Encounter:
                data.Only("place", "creatures", "text", "set");
                data.At("place").AsId();
                ContentNode creatures = data.At("creatures");
                if (!creatures.IsArray || creatures.Count == 0)
                {
                    throw creatures.Fail("is a list of at least one creature");
                }
                foreach (ContentNode c in creatures.Items())
                {
                    c.Only("creature", "name", "count");
                    c.At("creature").AsId();
                    c.Text("name", "", 80);
                    c.Int("count", 1, 1, 20);
                }
                data.Flags("set");
                break;
            case OutlineKind.Container:
                data.Only("place", "name", "items", "coins", "locked", "key", "check");
                data.At("place").AsId();
                data.Text("name", "Chest", 80);
                data.Ids("items");
                data.Int("coins", 0, 0, 100000000);
                data.Bool("locked", false);
                if (data.Get("key") is ContentNode chestKey)
                {
                    chestKey.AsId();
                }
                if (data.Get("check") is ContentNode lockCheck)
                {
                    lockCheck.Only("skill", "difficulty");
                    lockCheck.At("skill").AsId();
                    lockCheck.At("difficulty").AsInt(1, 60);
                }
                break;
            case OutlineKind.Trigger:
                data.Only("when", "dialogue", "place");
                data.Flags("when");
                data.At("dialogue").AsId();
                break;
            case OutlineKind.Note:
                data.Only("text", "place", "why");
                data.At("text").AsText(4000);
                break;
        }
    }

    private static void SameId(ContentNode data, string id)
    {
        if (data.Get("id") is ContentNode own && own.AsText() != id)
        {
            throw data.Fail("id", $"is the entry's id, \"{id}\", or left out");
        }
    }

    // a content file carries its own id; an entry's data may leave it to the entry
    private static ContentNode WithId(ContentNode data, string id)
    {
        if (data.Has("id"))
        {
            return data;
        }
        var copy = (JsonObject)JsonNode.Parse(data.Raw())!;
        copy["id"] = id;
        return new ContentNode(JsonDocument.Parse(CreateJson.Compact(copy)).RootElement.Clone(), data.File, data.Path);
    }

    // entries that name other entries: places, chapters and conversations must be in the outline
    private void CheckLinks(ContentNode entries)
    {
        List<ContentNode> nodes = entries.Items().ToList();
        if (Entries.Count(e => e.Kind == OutlineKind.Adventure) > 1)
        {
            throw entries.Fail("has more than one adventure entry");
        }
        for (int i = 0; i < Entries.Count; i++)
        {
            OutlineEntry entry = Entries[i];
            ContentNode data = nodes[i].At("data");
            if (entry.Chapter.Length > 0 && Find(entry.Chapter)?.Kind != OutlineKind.Chapter)
            {
                throw nodes[i].Fail("chapter", $"\"{entry.Chapter}\" is not a chapter entry");
            }
            foreach (string key in new[] { "place", "from", "to" })
            {
                if (entry.Kind is OutlineKind.Npc or OutlineKind.Encounter or OutlineKind.Container or OutlineKind.Link or OutlineKind.Note or OutlineKind.Trigger
                    && entry.Text(key) is { Length: > 0 } place && Find(place)?.Kind != OutlineKind.Place)
                {
                    throw data.Fail(key, $"\"{place}\" is not a place entry");
                }
            }
            if (entry.Kind is OutlineKind.Npc or OutlineKind.Trigger && entry.Text("dialogue") is { Length: > 0 } talk
                && Find(talk)?.Kind != OutlineKind.Dialogue)
            {
                throw data.Fail("dialogue", $"\"{talk}\" is not a dialogue entry");
            }
        }
    }

    /// <summary>
    /// What only the game's content can say: classes, creatures and items named by id that the
    /// outline doesn't hold itself. Empty when all is well; the builder refuses an outline with any.
    /// </summary>
    public List<string> Check(Compendium game)
    {
        bool classExists(string id) => game.Class(id) != null;
        var problems = new List<string>();
        bool Creature(string id) => Find(id)?.Kind == OutlineKind.Creature || game.Creature(id) != null;
        bool Item(string id) => Find(id)?.Kind == OutlineKind.Item || game.Item(id) != null;
        foreach (OutlineEntry e in Entries)
        {
            switch (e.Kind)
            {
                case OutlineKind.Hero when !classExists(e.Text("class")):
                    problems.Add($"{e.Id}: no class \"{e.Text("class")}\"");
                    break;
                case OutlineKind.Npc when e.Text("creature") is { Length: > 0 } c && !Creature(c):
                    problems.Add($"{e.Id}: no creature \"{c}\"");
                    break;
                case OutlineKind.Encounter:
                    foreach (JsonNode? c in e.Data["creatures"] as JsonArray ?? new JsonArray())
                    {
                        string id = c?["creature"]?.GetValue<string>() ?? "";
                        if (!Creature(id))
                        {
                            problems.Add($"{e.Id}: no creature \"{id}\"");
                        }
                    }
                    break;
                case OutlineKind.Container:
                    problems.AddRange(e.Texts("items").Where(i => !Item(i)).Select(i => $"{e.Id}: no item \"{i}\""));
                    break;
                case OutlineKind.Link when e.Text("key") is { Length: > 0 } key && !Item(key):
                    problems.Add($"{e.Id}: no item \"{key}\" for the key");
                    break;
            }
        }
        return problems;
    }

    public string ToJson()
    {
        var entries = new JsonArray();
        foreach (OutlineEntry e in Entries)
        {
            var entry = new JsonObject { ["id"] = e.Id, ["kind"] = KindName(e.Kind) };
            if (e.Chapter.Length > 0)
            {
                entry["chapter"] = e.Chapter;
            }
            entry["data"] = e.Data.DeepClone();
            entry["from"] = e.From.IsInvented ? "invented" : new JsonObject { ["page"] = e.From.Page, ["quote"] = e.From.Quote };
            if (e.Picture.Length > 0)
            {
                entry["picture"] = e.Picture;
            }
            entries.Add(entry);
        }
        var root = new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["title"] = Title,
            ["system"] = System,
            ["entries"] = entries,
        };
        return CreateJson.Write(root) + "\n";
    }
}
