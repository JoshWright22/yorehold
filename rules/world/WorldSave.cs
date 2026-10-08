using System.Numerics;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

// What a World writes down between fights, and how it is read back. The seed and the dice
// counters are in it, so a loaded game rolls the same as one that never stopped.
public sealed partial class World
{
    /// <summary>
    /// The adventure save. Version 4 is this port's; the C++ client's 1 to 3 kept its sheets in
    /// another shape and are refused with a message.
    /// </summary>
    public static readonly SaveFormat SaveFile = new("yorehold.adventure", 4) { Oldest = 4 };

    /// <summary>The state to go back to when the party is wiped: the last save, or the chapter's start.</summary>
    public string Checkpoint { get; private set; } = "";

    /// <summary>Saves are made between fights, with nothing else going on, before the chapter is done.</summary>
    public bool CanSave => Quiet && !ChapterCleared();

    /// <summary>Everything a save needs, as the data part of the file.</summary>
    public JsonObject StateJson()
    {
        var data = new JsonObject
        {
            ["chapterId"] = Chapter.Id,
            ["chapterFolder"] = Chapter.Folder,
            ["package"] = Package,
            ["seed"] = _seed,
            ["rolls"] = _rolls,
            ["fights"] = _fights,
            ["stealthDice"] = new JsonArray(_stealthRandom.State, _stealthRandom.Increment),
            ["roundClock"] = _roundClock,
            ["won"] = _won,
            ["flags"] = Texts(Flags),
            ["firedTriggers"] = Texts(FiredTriggers),
            ["fog"] = new JsonObject { ["width"] = Map.Width, ["height"] = Map.Height, ["explored"] = Fog.ExploredText(0, 0) },
            ["companions"] = Companions.ToJson(),
            ["stash"] = Stash.ToJson(),
        };
        var rests = new JsonObject();
        foreach (KeyValuePair<string, int> used in RestsUsed)
        {
            rests[used.Key] = used.Value;
        }
        data["restsUsed"] = rests;
        if (CampReturn.Length > 0)
        {
            data["campReturn"] = CampReturn;
        }

        var objects = new JsonArray();
        foreach (WorldObject o in Map.Objects)
        {
            var contents = new JsonObject();
            foreach (KeyValuePair<string, int> content in o.Contents)
            {
                contents[content.Key] = content.Value;
            }
            objects.Add(new JsonObject
            {
                ["open"] = o.Open, ["locked"] = o.Locked, ["destroyed"] = o.Destroyed,
                ["trapArmed"] = o.TrapArmed, ["trapFound"] = o.TrapFound, ["contents"] = contents,
            });
        }
        data["objects"] = objects;

        var piles = new JsonArray();
        foreach (Pile pile in Piles)
        {
            piles.Add(new JsonObject
            {
                ["name"] = pile.Name, ["at"] = new JsonArray(pile.At.X, pile.At.Y), ["coins"] = pile.Coins,
                ["container"] = pile.Container, ["object"] = pile.Object,
                ["items"] = new JsonArray(pile.Items.Select(i => (JsonNode)i.ToJson()).ToArray()),
            });
        }
        data["piles"] = piles;

        var merchants = new JsonArray();
        foreach (Merchant? merchant in Merchants)
        {
            merchants.Add(merchant == null ? null : new JsonObject
            {
                ["coins"] = merchant.Coins, ["buyMultiplier"] = merchant.BuyMultiplier, ["sellMultiplier"] = merchant.SellMultiplier,
                ["inventory"] = new JsonArray(merchant.Inventory.Select(i => (JsonNode)i.ToJson()).ToArray()),
            });
        }
        data["merchants"] = merchants;

        var surfaces = new JsonArray();
        foreach (SurfacePatch s in Surfaces)
        {
            surfaces.Add(new JsonObject { ["id"] = s.Id, ["at"] = new JsonArray(s.At.X, s.At.Y), ["size"] = s.Size, ["rounds"] = s.RoundsLeft });
        }
        data["surfaces"] = surfaces;

        var heroes = new JsonArray();
        for (int i = 0; i < HeroCount; i++)
        {
            WorldCreature hero = Creatures[i];
            var hj = new JsonObject { ["library"] = hero.Library, ["color"] = ColorJson(Tokens.Tokens[i].Color) };
            if (hero.Choices is CharacterChoices choices)
            {
                CharacterChoices kept = choices.Copy();
                kept.Xp = Math.Max(kept.Xp, hero.Sheet.Xp);
                hj["choices"] = kept.ToJson();
            }
            heroes.Add(hj);
        }
        data["heroes"] = heroes;

        // Companions from other chapters come after the chapter's own creatures.
        var along = new JsonArray();
        for (int i = AlongStart; i < Creatures.Count; i++)
        {
            WorldCreature c = Creatures[i];
            Token t = Tokens.Tokens[i];
            along.Add(new JsonObject
            {
                ["id"] = c.CompanionId, ["creature"] = c.CreatureId, ["talk"] = c.CompanionTalk,
                ["color"] = ColorJson(t.Color), ["radius"] = t.Radius, ["image"] = t.Image,
            });
        }
        data["companionsAlong"] = along;

        var creatures = new JsonArray();
        for (int i = 0; i < Creatures.Count; i++)
        {
            WorldCreature c = Creatures[i];
            Token token = Tokens.Tokens[i];
            Vector2 at = token.Path.Count == 0 ? token.Position : token.Path[^1];
            var cj = new JsonObject
            {
                ["sheet"] = c.Sheet.ToJson(),
                ["x"] = at.X,
                ["y"] = at.Y,
                ["team"] = c.Team,
                ["awake"] = c.Awake,
                ["facing"] = c.Facing,
                ["fled"] = c.Fled,
                ["surrendered"] = c.Surrendered,
                ["dropped"] = c.Dropped,
                ["mayPrepare"] = c.MayPrepare,
            };
            if (c.HeldReactions.Count > 0)
            {
                cj["heldReactions"] = new JsonArray(c.HeldReactions.OrderBy(id => id, StringComparer.Ordinal).Select(id => (JsonNode?)id).ToArray());
            }
            if (c.Concentration.Active)
            {
                cj["concentration"] = new JsonObject
                {
                    ["spell"] = c.Concentration.Spell,
                    ["holds"] = new JsonArray(c.Concentration.Holds.Select(h => (JsonNode)new JsonArray(h.Who, h.Id)).ToArray()),
                };
            }
            creatures.Add(cj);
        }
        data["creatures"] = creatures;
        return data;
    }

    /// <summary>
    /// Puts the World back as a save left it, in whichever chapter of the adventure (or camp) that
    /// was. Everything is checked before anything changes; false with Refusal saying why.
    /// </summary>
    public bool Restore(string text)
    {
        Refusal = "";
        try
        {
            RestoreFrom(ContentNode.Parse("save", text));
            return true;
        }
        catch (ContentException error)
        {
            Refusal = error.Message;
        }
        catch (ArgumentException error)
        {
            Refusal = error.Message;
        }
        return false;
    }

    /// <summary>Reads a save file (its .bak if the file is broken) and restores it.</summary>
    public bool Load(string path)
    {
        Refusal = "";
        string data;
        try
        {
            data = SaveFile.ReadFile(path);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Refusal = "Couldn't read the save: " + error.Message;
            return false;
        }
        if (!Restore(data))
        {
            Refusal = "Couldn't load the save: " + Refusal;
            return false;
        }
        return true;
    }

    /// <summary>
    /// The wiped party goes back to the checkpoint (or the chapter's start), on the chapter's wipe
    /// destination when it names one and nobody standing is in the way.
    /// </summary>
    public void ReturnFromWipe()
    {
        if (!PartyWiped)
        {
            return;
        }
        string checkpoint = Checkpoint;
        if (checkpoint.Length == 0 || !Restore(checkpoint))
        {
            string why = Refusal;
            NewAdventure(_seed);
            if (why.Length > 0)
            {
                Say("Couldn't return to the autosave: " + why);
            }
        }
        bool occupied = Chapter.WipeDestination.Any(cell => Enumerable.Range(HeroCount, Creatures.Count - HeroCount)
            .Any(i => !Creatures[i].Sheet.Down && CellOf(i) == cell));
        if (!occupied)
        {
            for (int i = 0; i < Chapter.WipeDestination.Count && i < HeroCount; i++)
            {
                Place(i, Chapter.WipeDestination[i]);
            }
        }
        else
        {
            Say("The wipe destination is taken; the party stands where the autosave left it.");
        }
        Say("The party returns to the autosave.");
        _events.Add(new WorldEvent(WorldEventKind.Banner, Chapter.ResumeText) { Seconds = 2 });
        RequestSave();
    }

    // A good moment to save: the checkpoint moves up and the screen may write the file.
    private void RequestSave()
    {
        if (!CanSave)
        {
            return;
        }
        string state = StateJson().ToJsonString();
        Checkpoint = state;
        _events.Add(new WorldEvent(WorldEventKind.Save, state));
    }

    // Where companions from other chapters start in Creatures: after the chapter's own NPCs.
    private int AlongStart => NpcStart + Chapter.Npcs.Count;

    private sealed record SavedCreature(
        CharacterSheet Sheet, Vector2 At, int Team, bool Awake, float Facing, bool Fled, bool Surrendered, bool Dropped,
        bool MayPrepare, Concentration Concentration, List<string> HeldReactions);

    private void RestoreFrom(ContentNode data)
    {
        data.RequireObject("a save is an object");
        string folder = data.At("chapterFolder").AsText();
        Chapter target = Chapter;
        ContentNode heroesNode = data.At("heroes");
        if (folder != Chapter.Folder)
        {
            if (Files == null || !KnownFolder(folder))
            {
                throw data.Fail("chapterFolder", "this save belongs to another adventure");
            }
            target = LoadChapterFor(folder, heroesNode.Count);
        }
        if (data.At("chapterId").AsText() != target.Id)
        {
            throw data.Fail("chapterId", "this save belongs to another chapter");
        }
        ContentNode fog = data.At("fog");
        if (fog.Int("width", 0) != target.Map.Width || fog.Int("height", 0) != target.Map.Height)
        {
            throw fog.Fail("the map has changed since this save");
        }
        string explored = fog.At("explored").AsText(int.MaxValue);
        if (explored.Length != target.Map.Width * target.Map.Height)
        {
            throw fog.Fail("explored", "the map has changed since this save");
        }

        // Counters, story and roster.
        ulong seed = data.At("seed").Element.GetUInt64();
        ulong rolls = data.At("rolls").Element.GetUInt64();
        int fights = data.Int("fights", 0, 0);
        ulong[] stealth = data.At("stealthDice").Items().Select(n => n.Element.GetUInt64()).ToArray();
        if (stealth.Length != 2 || (stealth[1] & 1) == 0)
        {
            throw data.Fail("stealthDice", "is [state, increment] with an odd increment");
        }
        var rests = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ContentNode> used in data.Get("restsUsed")?.Members() ?? Array.Empty<KeyValuePair<string, ContentNode>>())
        {
            rests[used.Key] = used.Value.AsInt(0);
        }
        Companions roster = Companions.Read(data.At("companions"));
        Stash stash = Stash.Read(data.At("stash"));
        string campReturn = data.Text("campReturn", "", int.MaxValue);
        if (campReturn.Length > 0 && folder != CampFolder)
        {
            throw data.Fail("campReturn", "a way back from camp saved outside camp");
        }

        // The heroes' choices and the companions from elsewhere.
        if (heroesNode.Count != target.Party.Count)
        {
            throw heroesNode.Fail("saved characters don't match the party");
        }
        var heroes = new List<(CharacterChoices? Choices, string Library, ContentColor Color)>();
        foreach (ContentNode h in heroesNode.Items())
        {
            CharacterChoices? choices = h.Get("choices") is ContentNode c ? CharacterChoices.Read(c) : null;
            heroes.Add((choices, h.Text("library", ""), ContentParts.ColorFrom(h.At("color"))));
        }
        var alongs = new List<(string Id, string Creature, string Talk, ContentColor Color, float Radius, string Image)>();
        foreach (ContentNode a in data.Get("companionsAlong")?.Items() ?? Array.Empty<ContentNode>())
        {
            string id = a.At("id").AsText();
            if (!roster.Member(id) || target.Npcs.Any(n => n.Id == id))
            {
                throw a.Fail($"saved companion {id} doesn't fit the chapter");
            }
            alongs.Add((id, a.Text("creature", ""), a.Text("talk", ""), ContentParts.ColorFrom(a.At("color")),
                (float)a.Number("radius", GameMap.CellSize * 0.4), a.Text("image", "")));
        }

        // Everyone's sheet and where they stand: heroes, the encounters' creatures, NPCs, then the companions along.
        int expected = target.Party.Count + target.Encounters.Sum(e => e.Creatures.Count) + target.Npcs.Count + alongs.Count;
        ContentNode list = data.At("creatures");
        if (list.Count != expected)
        {
            throw list.Fail("the chapter's creatures have changed since this save");
        }
        int width = target.Map.Width * GameMap.CellSize, height = target.Map.Height * GameMap.CellSize;
        var saved = new List<SavedCreature>();
        foreach (ContentNode c in list.Items())
        {
            var at = new Vector2((float)c.At("x").AsNumber(0, width - 0.001), (float)c.At("y").AsNumber(0, height - 0.001));
            Concentration held = new();
            if (c.Get("concentration") is ContentNode concentration)
            {
                var holds = new List<Concentration.Hold>();
                foreach (ContentNode hold in concentration.At("holds").Items())
                {
                    ContentNode[] pair = hold.Items().ToArray();
                    if (pair.Length != 2)
                    {
                        throw hold.Fail("is [who, id]");
                    }
                    holds.Add(new Concentration.Hold(pair[0].AsInt(0, expected - 1), pair[1].AsText()));
                }
                held = Concentration.Restore(concentration.At("spell").AsText(), holds);
            }
            CharacterSheet sheet = CharacterSheet.Read(c.At("sheet"));
            // A hero's sheet is built again from their choices, so content changes since the save
            // show up; what they lived through stays.
            if (saved.Count < heroes.Count && heroes[saved.Count].Choices is CharacterChoices choices)
            {
                CharacterSheet built = CharacterBuild.Build(target.Rules.Rules, target.Compendium, choices, out string problem)
                    ?? throw c.Fail("sheet", $"saved character {sheet.Name}: {problem}");
                sheet.AdoptBuild(built);
            }
            saved.Add(new SavedCreature(sheet, at, c.Int("team", 1, 0, 2), c.Bool("awake", false),
                (float)c.Number("facing", 0), c.Bool("fled", false), c.Bool("surrendered", false), c.Bool("dropped", false),
                c.Bool("mayPrepare", true), held, c.Get("heldReactions") is ContentNode kept ? kept.Items().Select(k => k.AsText(64)).ToList() : new List<string>()));
        }

        // Doors, chests and traps as they were left.
        ContentNode objects = data.At("objects");
        if (objects.Count != target.Map.Objects.Count)
        {
            throw objects.Fail("the map's objects have changed since this save");
        }
        var piles = new List<Pile>();
        foreach (ContentNode p in data.At("piles").Items())
        {
            var pile = new Pile
            {
                Name = p.Text("name", ""), At = CellFrom(p.At("at")), Coins = p.Int("coins", 0, 0),
                Container = p.Int("container", -1, -1, target.Containers.Count - 1), Object = p.Int("object", 0, 0, target.Map.Objects.Count),
            };
            foreach (ContentNode item in p.At("items").Items())
            {
                pile.Items.Add(Item.Read(item));
            }
            piles.Add(pile);
        }
        ContentNode merchantList = data.At("merchants");
        if (merchantList.Count != target.Npcs.Count)
        {
            throw merchantList.Fail("the chapter's NPCs have changed since this save");
        }
        var merchants = new List<Merchant?>();
        foreach (ContentNode m in merchantList.Items())
        {
            if (m.IsNull)
            {
                merchants.Add(null);
                continue;
            }
            var merchant = new Merchant { Coins = m.Int("coins", 0, 0), BuyMultiplier = m.Number("buyMultiplier", 1), SellMultiplier = m.Number("sellMultiplier", 0.5) };
            foreach (ContentNode item in m.At("inventory").Items())
            {
                merchant.Inventory.Add(Item.Read(item));
            }
            merchants.Add(merchant);
        }
        var surfaces = new List<SurfacePatch>();
        foreach (ContentNode s in data.At("surfaces").Items())
        {
            surfaces.Add(new SurfacePatch { Id = s.At("id").AsText(), At = CellFrom(s.At("at")), Size = (float)s.At("size").AsNumber(0, 1000), RoundsLeft = s.Int("rounds", 0, 0) });
        }

        // All read: now the World changes.
        if (!ReferenceEquals(target, Chapter))
        {
            SwitchChapter(target);
        }
        Begin(seed, true);
        _rolls = rolls;
        _fights = fights;
        _stealthRandom = Rng.Restore(stealth[0], stealth[1]);
        _roundClock = data.Number("roundClock", 0, 0, SecondsPerRound);
        _won = data.Bool("won", false);
        Flags.Clear();
        Flags.UnionWith(data.Texts("flags"));
        FiredTriggers.Clear();
        FiredTriggers.UnionWith(data.Texts("firedTriggers"));
        RestsUsed.Clear();
        foreach (KeyValuePair<string, int> used in rests)
        {
            RestsUsed[used.Key] = used.Value;
        }
        Stash.Clear();
        Stash.Items.AddRange(stash.Items);
        CampReturn = campReturn;
        Fog.SetExplored(0, 0, explored);

        int objectIndex = 0;
        foreach (ContentNode o in objects.Items())
        {
            WorldObject live = Map.Objects[objectIndex++];
            live.Open = o.Bool("open", live.Open);
            live.Locked = o.Bool("locked", live.Locked);
            live.Destroyed = o.Bool("destroyed", live.Destroyed);
            live.TrapArmed = o.Bool("trapArmed", live.TrapArmed);
            live.TrapFound = o.Bool("trapFound", live.TrapFound);
            live.Contents.Clear();
            foreach (KeyValuePair<string, ContentNode> content in o.Get("contents")?.Members() ?? Array.Empty<KeyValuePair<string, ContentNode>>())
            {
                live.Contents[content.Key] = content.Value.AsInt();
            }
        }
        ObjectsChanged();
        Piles.Clear();
        Piles.AddRange(piles);
        Merchants.Clear();
        Merchants.AddRange(merchants);
        Surfaces.Clear();
        Surfaces.AddRange(surfaces);

        // The roster as saved, then whoever came along from elsewhere.
        Companions.CopyFrom(roster);
        MeetCompanions();
        foreach (var a in alongs)
        {
            Creatures.Add(new WorldCreature(new CharacterSheet(), 0) { CreatureId = a.Creature, CompanionId = a.Id, CompanionTalk = a.Talk });
            Tokens.Tokens.Add(new Token { Name = a.Id, Color = a.Color, Radius = a.Radius, Image = a.Image });
        }

        for (int i = 0; i < Creatures.Count; i++)
        {
            WorldCreature c = Creatures[i];
            SavedCreature s = saved[i];
            c.Sheet = s.Sheet;
            if (i < HeroCount)
            {
                c.Choices = heroes[i].Choices;
                c.Library = heroes[i].Library;
                Tokens.Tokens[i].Color = heroes[i].Color;
            }
            c.Concentration = s.Concentration;
            c.Awake = s.Awake;
            c.Facing = s.Facing;
            c.MayPrepare = s.MayPrepare;
            c.HeldReactions.Clear();
            c.HeldReactions.UnionWith(s.HeldReactions);
            c.Fled = s.Fled && s.Sheet.Down;
            c.Surrendered = s.Surrendered;
            c.Dropped = s.Dropped;
            c.Team = s.Team;
            Token token = Tokens.Tokens[i];
            token.Name = c.Sheet.Name;
            token.Position = s.At;
            token.Path.Clear();
            if (c.Npc >= 0)
            {
                token.Owner = c.Team == 1 ? EnemyOwner : c.Team == 0 ? 0 : NpcOwner;
            }
            else if (i >= HeroCount && c.Team == 2)
            {
                token.Owner = NpcOwner;
            }
        }
        for (int i = 0; i < HeroCount; i++)
        {
            _sneak[i].Reset();
            _lastAt[i] = Tokens.Tokens[i].Position;
        }
        FollowParty();
        FallenConditions();
        Arrived();
        Checkpoint = StateJson().ToJsonString();
        _events.Add(new WorldEvent(WorldEventKind.Resumed, Chapter.ResumeText));
    }

    private static Cell CellFrom(ContentNode node)
    {
        int[] xy = node.Items().Select(n => n.AsInt(0, 100000)).ToArray();
        if (xy.Length != 2)
        {
            throw node.Fail("is [x, y]");
        }
        return new Cell(xy[0], xy[1]);
    }

    private static JsonArray ColorJson(ContentColor color) => new(color.R, color.G, color.B, color.A);

    private static JsonArray Texts(IEnumerable<string> list) => new(list.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
}
