using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Yorehold.Rules;

/// <summary>
/// What an adventure module's layout says about who and what is in it, read without a story
/// model, after BookLayout has found the numbered places:
/// - heroes: a short name under a picture with a "Human Fighter" line after it (pregenerated
///   characters), with that picture;
/// - foes and fights: a framed box in a place that lists foes with hit point boxes ("Orc #1:
///   o o o o o o"), its "13 or better" to hit and "roll 1 die" damage, the weapon the place's
///   text gives them, a fight in that place;
/// - talk: questions set as bullets ("Who are you?") each followed by a quoted answer, as a
///   conversation with the person the text names ("The old man's name is Jeffries");
/// - finds: a heading like "Look in the Sack" whose text gives gold or a known thing, as
///   something to open in that place; on a foe ("Examine the Ogre") it goes on the foe.
/// Everything carries the page and words it came from. A story model reads the rest.
/// </summary>
public static partial class BookCast
{
    /// <summary>Weapons the text may name, with the hands they take; damage comes from the fight box.</summary>
    private static readonly (string Word, string Name, int Hands)[] Weapons =
    {
        ("spear", "Spear", 1), ("knife", "Knife", 1), ("dagger", "Dagger", 1), ("axe", "Axe", 2), ("sword", "Sword", 1),
        ("club", "Club", 1), ("mace", "Mace", 1), ("bow", "Bow", 2), ("teeth", "Teeth", 0), ("bite", "Bite", 0),
        ("claws", "Claws", 0), ("fangs", "Fangs", 0),
    };

    /// <summary>Things found lying about that become items, by the word for them, with damage for weapons.</summary>
    private static readonly (string Pattern, string Name, string Damage, int Hands)[] Things =
    {
        (@"\bgreatsword\b", "Greatsword", "2d6", 2), (@"\bcrossbow bolts\b", "Crossbow bolts", "", 1), (@"\bcrossbow\b(?! bolts)", "Crossbow", "1d8", 2),
        (@"\bcoil of rope|\brope\b", "Rope", "", 1), (@"\btorches\b", "Torches", "", 1), (@"\bstatue\b", "Statue", "", 1),
        (@"\bnecklace\b", "Necklace", "", 1), (@"\bgem\b", "Gem", "", 1), (@"\bring\b", "Ring", "", 1), (@"\bscroll\b", "Scroll", "", 1),
        (@"\bwand\b", "Wand", "", 1), (@"\bshield\b", "Shield", "", 1),
    };

    private static readonly string[] Classes = { "barbarian", "bard", "cleric", "druid", "fighter", "monk", "paladin", "ranger", "rogue", "sorcerer", "warlock", "wizard" };

    [GeneratedRegex(@"^(?<name>[A-Za-z][A-Za-z' ]*?(?:\s*#\s*\d+)?)\s*:\s*(?<boxes>(?:[oO○□◯]\s*)+)$")]
    private static partial Regex FoeRow();

    [GeneratedRegex(@"^(?:[oO○□◯]\s*)+$")]
    private static partial Regex MoreBoxes();

    [GeneratedRegex(@"^(?:Search|Look in|Look under|Look into|Open|Check out|Check|Examine)\s+(?:the\s+)?(?<what>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Search();

    [GeneratedRegex(@"(?<n>\d+)\s+(?:gold|gp)\s*(?:pieces|coins)?", RegexOptions.IgnoreCase)]
    private static partial Regex Gold();

    [GeneratedRegex(@"name(?:'s|’s| is)\s+(?<name>[A-Z][a-z]+)")]
    private static partial Regex NameIs();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex Sentences();

    /// <summary>A block of a numbered place's part of the book, with its page.</summary>
    private sealed record Part(int Page, SourceBook.Block Block);

    /// <summary>A foe as a fight box lists it.</summary>
    private sealed record Foe(string Base, string Name, int Hp);

    /// <summary>
    /// Adds what the layout gives to a draft (BookLayout.Draft). Classes are the game's class ids;
    /// a hero whose class the game hasn't is left to the story model.
    /// </summary>
    public static void Add(SourceBook book, Outline draft, IReadOnlyCollection<string> classes)
    {
        var used = new HashSet<string>(draft.Entries.Select(e => e.Id), StringComparer.Ordinal);
        AddHeroes(book, draft, classes, used);
        AddIntro(book, draft);
        var ways = new HashSet<string>(StringComparer.Ordinal);
        foreach ((OutlineEntry place, List<Part> parts) in PlaceParts(book, draft))
        {
            Dictionary<string, OutlineEntry> foes = AddFight(draft, place, parts, used);
            AddTalk(draft, place, parts, used);
            AddFinds(draft, place, parts, foes, used);
            AddWays(draft, place, parts, ways, used);
        }
        AddStatBlocks(book, draft, used);
    }

    // Every stat block printed in the book becomes a creature, with its weapon attacks as items,
    // unless the outline already has a creature by that name.
    private static void AddStatBlocks(SourceBook book, Outline draft, HashSet<string> used)
    {
        var named = new HashSet<string>(draft.OfKind(OutlineKind.Creature).Select(c => c.Text("name", c.Id)), StringComparer.OrdinalIgnoreCase);
        foreach (SourceBook.Page page in book.Pages)
        {
            string text = string.Join("\n", page.Blocks.Select(b => b.Text));
            foreach (string block in StatBlockText.Find(text))
            {
                if (StatBlockText.Read(block) is not StatBlockText.Creature read || !named.Add(read.Name))
                {
                    continue;
                }
                string id = Unique(Slug(read.Name), used);
                var items = new JsonArray();
                foreach (StatBlockText.Attack attack in read.Attacks)
                {
                    string itemId = Unique(id + "-" + Slug(attack.Name), used);
                    var item = new JsonObject
                    {
                        ["name"] = attack.Name,
                        ["slot"] = "mainHand",
                        ["damage"] = attack.Dice,
                        ["attackAbility"] = attack.Ranged ? "dex" : "str",
                        ["hands"] = 0,
                        ["weight"] = 0,
                    };
                    if (attack.Type.Length > 0)
                    {
                        item["damageType"] = attack.Type;
                    }
                    draft.Entries.Add(new OutlineEntry { Id = itemId, Kind = OutlineKind.Item, Data = item, From = new OutlineSource(page.Number, Quote(attack.Name)) });
                    items.Add(itemId);
                }
                read.Data["items"] = items;
                draft.Entries.Add(new OutlineEntry { Id = id, Kind = OutlineKind.Creature, Data = read.Data, From = new OutlineSource(page.Number, Quote(read.Name)) });
            }
        }
    }

    // "Marsh Lurker" is "marsh-lurker"
    private static string Slug(string name) => OutlineBuilder.Slug(name);

    // the passages to read out before the first numbered place open the chapter
    private static void AddIntro(SourceBook book, Outline draft)
    {
        OutlineEntry? first = draft.OfKind(OutlineKind.Place).FirstOrDefault();
        OutlineEntry? chapter = draft.OfKind(OutlineKind.Chapter).FirstOrDefault();
        if (first == null || chapter == null)
        {
            return;
        }
        var intro = new JsonArray();
        foreach (SourceBook.Page page in book.Pages.Where(p => p.Number <= first.From.Page))
        {
            foreach (SourceBook.Block block in page.Blocks)
            {
                if (block.Kind == "heading" && block.Text.Trim() == first.From.Quote)
                {
                    break;
                }
                if (block.Kind == "text" && block.Box == "shaded")
                {
                    intro.Add(block.Text);
                }
            }
        }
        if (intro.Count > 0)
        {
            chapter.Data["intro"] = intro;
        }
    }

    // ---------------------------------------------------------------- ways between places

    [GeneratedRegex(@"\b(?:go|proceed|move on|continue|head)\s+(?:on\s+)?to\s+Area\s+(?<n>\d{1,3}[A-Za-z]?)", RegexOptions.IgnoreCase)]
    private static partial Regex GoTo();

    // "go to Area 3": a way from here to there, locked when the text around it says so
    private static void AddWays(Outline draft, OutlineEntry place, List<Part> parts, HashSet<string> ways, HashSet<string> used)
    {
        var section = new List<string>();
        foreach (Part part in parts)
        {
            if (part.Block.Kind == "heading")
            {
                section.Clear();
                continue;
            }
            if (part.Block.Box == "framed")
            {
                continue;
            }
            section.Add(part.Block.Text);
            foreach (Match go in GoTo().Matches(part.Block.Text))
            {
                OutlineEntry? to = draft.OfKind(OutlineKind.Place).FirstOrDefault(p => string.Equals(p.Text("label"), go.Groups["n"].Value, StringComparison.OrdinalIgnoreCase));
                if (to == null || to.Id == place.Id || !ways.Add(string.Join("|", new[] { place.Id, to.Id }.Order(StringComparer.Ordinal))))
                {
                    continue;
                }
                // "go to Area 4: Crossing the Crevice" names the room, not the way there
                string around = Regex.Replace(string.Join(" ", section), @"Area\s+\d{1,3}[A-Za-z]?(?::\s*[A-Z][\w’' ]+)?", "");
                var data = new JsonObject { ["from"] = place.Id, ["to"] = to.Id };
                if (Regex.IsMatch(around, @"\blocked\b", RegexOptions.IgnoreCase))
                {
                    data["way"] = "locked";
                    int dc = Regex.Match(around, @"(?<dc>\d+)\s+or better", RegexOptions.IgnoreCase) is { Success: true } m ? int.Parse(m.Groups["dc"].Value) : 15;
                    // picking a lock is the game's Dex check; a book that only bashes doors in is Athletics
                    string skill = Regex.IsMatch(around, @"pick(?:s|ing)?\s+(?:the\s+|its\s+)?lock|open lock", RegexOptions.IgnoreCase) ? "dex" : "athletics";
                    data["check"] = new JsonObject { ["skill"] = skill, ["difficulty"] = Math.Clamp(dc, 1, 60) };
                }
                else if (Regex.IsMatch(around, @"\b(?:across|leap|jump)\b[^.]*\b(?:chasm|crevice|gap|pit)\b|\b(?:chasm|crevice)\b", RegexOptions.IgnoreCase))
                {
                    data["way"] = "jump";
                }
                else if (Regex.IsMatch(around, @"\bdoor\b", RegexOptions.IgnoreCase))
                {
                    data["way"] = "door";
                }
                draft.Entries.Add(new OutlineEntry
                {
                    Id = Unique($"way-{place.Text("label")}-{to.Text("label")}".ToLowerInvariant(), used),
                    Kind = OutlineKind.Link,
                    Data = data,
                    From = new OutlineSource(part.Page, Quote(go.Value)),
                });
            }
        }
    }

    // ---------------------------------------------------------------- heroes

    private static void AddHeroes(SourceBook book, Outline draft, IReadOnlyCollection<string> classes, HashSet<string> used)
    {
        foreach ((string picture, string name) in BookLayout.PictureNames(book))
        {
            SourceBook.Picture shown = book.Pictures.First(p => p.File == picture);
            SourceBook.Page page = book.Pages.First(p => p.Number == shown.Page);
            int at = page.Blocks.FindIndex(b => b.Kind == "heading" && b.Text == name);
            if (at < 0 || at + 1 >= page.Blocks.Count)
            {
                continue;
            }
            // "Human Fighter": a race (or nothing) and a class word, and nothing more
            string[] words = page.Blocks[at + 1].Text.Trim().TrimEnd('.').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string classWord = words.Length is >= 1 and <= 3 ? words[^1].ToLowerInvariant() : "";
            if (!Classes.Contains(classWord) || !classes.Contains(classWord))
            {
                continue;
            }
            var data = new JsonObject { ["name"] = BookLayout.TitleCase(name), ["class"] = classWord };
            if (words.Length > 1)
            {
                data["race"] = string.Join(' ', words[..^1]).ToLowerInvariant();
            }
            if (at + 2 < page.Blocks.Count && page.Blocks[at + 2].Kind == "text")
            {
                data["description"] = page.Blocks[at + 2].Text;
            }
            draft.Entries.Add(new OutlineEntry
            {
                Id = Unique(OutlineBuilder.Slug(name), used),
                Kind = OutlineKind.Hero,
                Data = data,
                From = new OutlineSource(page.Number, name + " " + page.Blocks[at + 1].Text.Trim()),
                Picture = picture,
            });
        }
    }

    // ---------------------------------------------------------------- the book cut at its numbered places

    private static List<(OutlineEntry Place, List<Part> Parts)> PlaceParts(SourceBook book, Outline draft)
    {
        var byHeading = draft.OfKind(OutlineKind.Place).ToDictionary(p => p.From.Quote, p => p, StringComparer.Ordinal);
        var parts = new List<(OutlineEntry, List<Part>)>();
        List<Part>? current = null;
        foreach (SourceBook.Page page in book.Pages)
        {
            foreach (SourceBook.Block block in page.Blocks)
            {
                if (block.Kind == "heading" && block.Box.Length == 0 && byHeading.Remove(block.Text.Trim(), out OutlineEntry? place))
                {
                    current = new List<Part>();
                    parts.Add((place, current));
                    continue;
                }
                current?.Add(new Part(page.Number, block));
            }
        }
        return parts;
    }

    // ---------------------------------------------------------------- foes and the fight

    private static Dictionary<string, OutlineEntry> AddFight(Outline draft, OutlineEntry place, List<Part> parts, HashSet<string> used)
    {
        var rows = new List<Foe>();
        foreach (Part part in parts.Where(p => p.Block.Box == "framed"))
        {
            string text = part.Block.Text.Trim();
            if (FoeRow().Match(text) is { Success: true } row)
            {
                string name = Regex.Replace(row.Groups["name"].Value, @"\s*#\s*\d+", "").Trim();
                rows.Add(new Foe(BaseOf(name), name, Boxes(row.Groups["boxes"].Value)));
            }
            else if (MoreBoxes().IsMatch(text) && rows.Count > 0)
            {
                // a long row of boxes runs on to the next line
                rows[^1] = rows[^1] with { Hp = rows[^1].Hp + Boxes(text) };
            }
        }
        var made = new Dictionary<string, OutlineEntry>(StringComparer.Ordinal);
        if (rows.Count == 0)
        {
            return made;
        }
        string box = string.Join(" ", parts.Where(p => p.Block.Box == "framed").Select(p => p.Block.Text));
        string story = string.Join(" ", parts.Where(p => p.Block.Box != "framed").Select(p => p.Block.Text));
        int page = parts.First(p => p.Block.Box == "framed").Page;
        var foes = new JsonArray();
        foreach (IGrouping<string, Foe> kind in rows.GroupBy(r => r.Base))
        {
            string id = OutlineBuilder.Slug(kind.Key);
            OutlineEntry creature = draft.Find(id) ?? MakeFoe(draft, id, kind.Key, kind.Max(f => f.Hp), box, story, page, used);
            made[kind.Key] = creature;
            var foe = new JsonObject { ["creature"] = creature.Id };
            if (kind.Count() > 1)
            {
                foe["count"] = kind.Count();
            }
            // "Jezer the Ogre": a foe the book names
            if (Regex.Match(story, $@"\b(?<name>[A-Z][a-z]+) the {Regex.Escape(kind.Key)}\b", RegexOptions.IgnoreCase) is { Success: true } named
                && char.IsUpper(named.Groups["name"].Value[0]) && named.Groups["name"].Value != "The")
            {
                foe["name"] = named.Groups["name"].Value;
            }
            foes.Add(foe);
        }
        draft.Entries.Add(new OutlineEntry
        {
            Id = Unique(place.Id + "-fight", used),
            Kind = OutlineKind.Encounter,
            Data = new JsonObject { ["place"] = place.Id, ["creatures"] = foes },
            From = new OutlineSource(page, Quote(string.Join(", ", rows.Select(r => r.Name)))),
        });
        return made;
    }

    private static OutlineEntry MakeFoe(Outline draft, string id, string name, int hp, string box, string story, int page, HashSet<string> used)
    {
        string weaponWord = WeaponOf(name, story);
        (string Word, string Name, int Hands) weapon = Weapons.FirstOrDefault(w => w.Word == weaponWord);
        string weaponId = Unique(id + "-" + (weapon.Word ?? "weapon"), used);
        draft.Entries.Add(new OutlineEntry
        {
            Id = weaponId,
            Kind = OutlineKind.Item,
            Data = new JsonObject
            {
                ["name"] = weapon.Name ?? "Weapon",
                ["slot"] = "mainHand",
                ["damage"] = DamageOf(name, box),
                ["hands"] = Math.Max(1, weapon.Hands),
                ["weight"] = weapon.Hands == 0 ? 0 : 3,
            },
            From = weapon.Word != null ? new OutlineSource(page, weapon.Word) : OutlineSource.Invented,
        });
        var data = new JsonObject
        {
            ["name"] = BookLayout.TitleCase(name),
            ["hp"] = Math.Max(1, hp),
            ["armorClass"] = ArmorOf(name, box),
            ["level"] = Math.Clamp(hp / 6, 1, 20),
            ["speed"] = 30,
            ["items"] = new JsonArray(weaponId),
            ["token"] = new JsonObject { ["color"] = new JsonArray(130, 110, 90), ["size"] = hp >= 15 ? 0.45 : 0.36 },
            ["ai"] = "cunning",
        };
        var creature = new OutlineEntry { Id = Unique(id, used), Kind = OutlineKind.Creature, Data = data, From = new OutlineSource(page, Quote(box)) };
        draft.Entries.Add(creature);
        return creature;
    }

    // "Sleeping Orc", "Orc with knife": the last word before any "with ..."
    private static string BaseOf(string name)
    {
        string bare = Regex.Replace(name, @"\s+with\s+.*$", "", RegexOptions.IgnoreCase).Trim();
        string[] words = bare.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (words.Length > 0 ? words[^1] : bare).ToLowerInvariant();
    }

    private static int Boxes(string row) => row.Count(c => c is 'o' or 'O' or '○' or '□' or '◯');

    // what the box says it takes to hit this foe, else what it says for every foe, else 12
    private static int ArmorOf(string name, string box)
    {
        string n = Regex.Escape(name);
        foreach (string pattern in new[]
        {
            $@"attacks?\s+[^.,]*?\b{n}\b[^.]*?(?<ac>\d+)\s+or better",
            $@"attacks?\s+[^.,]*?\b{n}\b[^.]*?needs\s+(?:a\s+total\s+of\s+)?(?<ac>\d+)",
            $@"(?<ac>\d+)\s+or better[^.]*?\bhits?\s+(?:the\s+)?{n}\b",
            @"(?<ac>\d+)\s+or better",
        })
        {
            if (Regex.Match(box, pattern, RegexOptions.IgnoreCase) is { Success: true } m && int.TryParse(m.Groups["ac"].Value, out int ac) && ac is >= 5 and <= 30)
            {
                return ac;
            }
        }
        return 12;
    }

    // the dice the box rolls for this foe's damage: after the place it turns to the foe, else the first given
    private static string DamageOf(string name, string box)
    {
        int from = Regex.Match(box, $@"(?:for|each)\s+(?:the\s+|each\s+)?(?:awake\s+)?{Regex.Escape(name)}", RegexOptions.IgnoreCase) is { Success: true } turn ? turn.Index : 0;
        foreach (int start in new[] { from, 0 })
        {
            // "roll 1 die damage", "roll 1 die for damage", "roll 2 dice for the ogre's damage"
            if (Regex.Match(box[start..], @"roll\s+(?<n>\d+|one|two|three)\s+(?:die|dice)\s+(?:of\s+|for\s+)?(?:(?:the|its)\s+[\w’']+\s+)?damage", RegexOptions.IgnoreCase) is { Success: true } m)
            {
                string n = m.Groups["n"].Value.ToLowerInvariant() switch { "one" => "1", "two" => "2", "three" => "3", var digits => digits };
                return n + "d6";
            }
        }
        return "1d6";
    }

    // the weapon in the sentence that names the foe, or in the one after it if that starts "It"; else the nearest one
    private static string WeaponOf(string name, string story)
    {
        string[] sentences = Sentences().Split(story);
        // first a sentence where it holds the weapon ("has a massive axe", "its spear"), then any that names both
        foreach (string holding in new[] { @"\b(?:has|have|holds?|holding|carr(?:y|ies|ied)|clutch(?:es)?|wields?|armed with|its|their)\s+(?:\w+[\s,-]+){0,3}", "" })
        {
            for (int i = 0; i < sentences.Length; i++)
            {
                if (!Regex.IsMatch(sentences[i], $@"\b{Regex.Escape(name)}s?\b", RegexOptions.IgnoreCase))
                {
                    continue;
                }
                string near = sentences[i] + (i + 1 < sentences.Length && Regex.IsMatch(sentences[i + 1], @"^(?:It|Its)\b") ? " " + sentences[i + 1] : "");
                if (Weapons.FirstOrDefault(w => Regex.IsMatch(near, $@"{holding}\b{w.Word}s?\b", RegexOptions.IgnoreCase)) is { Word: not null } found)
                {
                    return found.Word;
                }
            }
        }
        return Weapons.FirstOrDefault(w => Regex.IsMatch(story, $@"\b{w.Word}s?\b", RegexOptions.IgnoreCase)).Word ?? "";
    }

    // ---------------------------------------------------------------- talk

    private static void AddTalk(Outline draft, OutlineEntry place, List<Part> parts, HashSet<string> used)
    {
        var pairs = new List<(string Question, string Answer, int Page)>();
        for (int i = 0; i + 1 < parts.Count; i++)
        {
            string ask = parts[i].Block.Text.Trim();
            string answer = parts[i + 1].Block.Text.Trim();
            if (ask.StartsWith('•') && ask.EndsWith('?') && answer.Length > 1 && answer[0] is '“' or '"')
            {
                pairs.Add((ask.TrimStart('•', ' '), answer.Trim('“', '”', '"', ' '), parts[i].Page));
            }
        }
        if (pairs.Count < 2)
        {
            return;
        }
        string all = string.Join(" ", parts.Select(p => p.Block.Text));
        string speaker = NameIs().Match(all) is { Success: true } named ? named.Groups["name"].Value : "Stranger";
        string talkId = Unique(OutlineBuilder.Slug(speaker) + "-talk", used);
        var nodes = new JsonArray();
        for (int n = 0; n < pairs.Count; n++)
        {
            var choices = new JsonArray();
            for (int q = 1; q < pairs.Count; q++)
            {
                if (q != n)
                {
                    choices.Add(new JsonObject { ["id"] = $"q{q}", ["text"] = pairs[q].Question, ["next"] = $"a{q}" });
                }
            }
            choices.Add(new JsonObject { ["id"] = "bye", ["text"] = "Farewell.", ["next"] = "" });
            // the first answer opens the talk: books lead with "Who are you?"
            nodes.Add(new JsonObject { ["id"] = $"a{n}", ["speaker"] = speaker, ["text"] = pairs[n].Answer, ["choices"] = choices });
        }
        draft.Entries.Add(new OutlineEntry
        {
            Id = talkId,
            Kind = OutlineKind.Dialogue,
            Data = new JsonObject { ["start"] = "a0", ["nodes"] = nodes },
            From = new OutlineSource(pairs[0].Page, Quote(pairs[0].Answer)),
        });
        draft.Entries.Add(new OutlineEntry
        {
            Id = Unique(OutlineBuilder.Slug(speaker), used),
            Kind = OutlineKind.Npc,
            Data = new JsonObject { ["name"] = speaker, ["place"] = place.Id, ["dialogue"] = talkId },
            From = new OutlineSource(pairs[0].Page, Quote(pairs[0].Answer)),
        });
    }

    // ---------------------------------------------------------------- finds

    private static void AddFinds(Outline draft, OutlineEntry place, List<Part> parts, Dictionary<string, OutlineEntry> foes, HashSet<string> used)
    {
        for (int i = 0; i < parts.Count; i++)
        {
            SourceBook.Block heading = parts[i].Block;
            if (heading.Kind != "heading" || heading.Box.Length > 0 || Search().Match(heading.Text.Trim()) is not { Success: true } search)
            {
                continue;
            }
            // the heading's own text runs to the next heading
            string text = string.Join(" ", parts.Skip(i + 1).TakeWhile(p => p.Block.Kind != "heading").Where(p => p.Block.Box.Length == 0).Select(p => p.Block.Text));
            string what = search.Groups["what"].Value;
            int coins = Gold().Matches(text).Select(m => int.Parse(m.Groups["n"].Value)).Where(g => !Regex.IsMatch(text, $@"for\s+{g}\s+gold", RegexOptions.IgnoreCase)).Sum();
            List<string> items = Items(draft, text, parts[i].Page, used);
            if (coins == 0 && items.Count == 0)
            {
                continue;
            }
            // on a foe of this place: it carries them
            if (foes.FirstOrDefault(f => Regex.IsMatch(what, $@"\b{Regex.Escape(f.Key)}s?\b|creatures", RegexOptions.IgnoreCase)) is { Value: not null } foe)
            {
                if (coins > 0)
                {
                    foe.Value.Data["loot"] = new JsonObject { ["coins"] = coins * 100 };
                }
                var carried = (JsonArray)foe.Value.Data["items"]!;
                foreach (string item in items)
                {
                    carried.Add(item);
                }
                continue;
            }
            var data = new JsonObject { ["place"] = place.Id, ["name"] = ContainerName(what, text), ["items"] = new JsonArray(items.Select(n => (JsonNode?)n).ToArray()) };
            if (coins > 0)
            {
                data["coins"] = coins * 100; // the game counts in copper
            }
            draft.Entries.Add(new OutlineEntry
            {
                Id = Unique(place.Id + "-" + OutlineBuilder.Slug(ContainerName(what, text)), used),
                Kind = OutlineKind.Container,
                Data = data,
                From = new OutlineSource(parts[i].Page, Quote(heading.Text + ": " + text)),
            });
        }
    }

    private static List<string> Items(Outline draft, string text, int page, HashSet<string> used)
    {
        var found = new List<string>();
        foreach ((string pattern, string name, string damage, int hands) in Things)
        {
            if (Regex.Match(text, pattern, RegexOptions.IgnoreCase) is not { Success: true } m)
            {
                continue;
            }
            found.Add(Item(draft, name, damage, hands, SentenceAt(text, m.Index), page, used));
        }
        // potions by their colour, each with what it does in its own sentence
        foreach (Match potion in Regex.Matches(text, @"\b(?<colour>blue|green|orange|red|yellow|purple|black|white|clear|silver|golden)\s+(?:potion|liquid|fluid|flask)", RegexOptions.IgnoreCase))
        {
            string colour = BookLayout.TitleCase(potion.Groups["colour"].Value);
            string said = string.Join(" ", Sentences().Split(text).Where(s => Regex.IsMatch(s, $@"\b{colour}\s+potion\b", RegexOptions.IgnoreCase)));
            if (said.Length == 0 || found.Any(f => draft.Find(f)?.Text("name") == colour + " potion"))
            {
                continue;
            }
            found.Add(Regex.IsMatch(said, @"\bheal", RegexOptions.IgnoreCase) ? "healing-potion" : Item(draft, colour + " potion", "", 1, said, page, used));
        }
        return found;
    }

    private static string Item(Outline draft, string name, string damage, int hands, string said, int page, HashSet<string> used)
    {
        string id = OutlineBuilder.Slug(name);
        if (draft.Find(id) != null)
        {
            return id;
        }
        var data = new JsonObject { ["name"] = name, ["description"] = said, ["weight"] = 1 };
        if (damage.Length > 0)
        {
            data["slot"] = "mainHand";
            data["damage"] = damage;
            data["hands"] = hands;
        }
        // "can be sold for 50 gold pieces"
        if (Regex.Match(said, @"for\s+(?<n>\d+)\s+gold", RegexOptions.IgnoreCase) is { Success: true } price)
        {
            data["value"] = int.Parse(price.Groups["n"].Value) * 100;
        }
        string unique = Unique(id, used);
        draft.Entries.Add(new OutlineEntry { Id = unique, Kind = OutlineKind.Item, Data = data, From = new OutlineSource(page, Quote(said)) });
        return unique;
    }

    // "a small bag with 20 gold coins" names it; else the heading's thing, "Sack" for "Look in the Sack"
    private static string ContainerName(string what, string text)
    {
        if (Regex.Match(text, @"\b(?:small|large|old|wooden|iron)?\s*(?<c>bag|sack|box|chest|pouch|barrel|crate|coffer|cart)\b", RegexOptions.IgnoreCase) is { Success: true } named
            && !Regex.IsMatch(what, @"\b(?:crates|barrel|box|sack|cart|chest)\b", RegexOptions.IgnoreCase))
        {
            return BookLayout.TitleCase(named.Groups["c"].Value);
        }
        string first = Regex.Split(what, @"\s+(?:and|in|on|under|of)\s+", RegexOptions.IgnoreCase)[0].Trim();
        return first.Length is > 0 and <= 24 ? BookLayout.TitleCase(first) : "Hidden things";
    }

    private static string SentenceAt(string text, int index) =>
        Sentences().Split(text).Aggregate((Found: "", At: 0), (s, sentence) =>
            s.Found.Length == 0 && index < s.At + sentence.Length + 1 ? (sentence, s.At) : (s.Found, s.At + sentence.Length + 1)).Found;

    private static string Unique(string id, HashSet<string> used)
    {
        string unique = id.Length > 0 ? id : "entry";
        for (int n = 2; !used.Add(unique); n++)
        {
            unique = $"{id}-{n}";
        }
        return unique;
    }

    private static string Quote(string text) => text.Length <= 120 ? text : text[..120];
}
