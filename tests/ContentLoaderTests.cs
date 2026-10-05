namespace Yorehold.Rules.Tests;

/// <summary>The readers one kind at a time: a good file, what a sparse one defaults to, and what a broken one says.</summary>
public class ContentLoaderTests
{
    private const string SmallRuleset = """
        {"id": "small", "name": "Small",
         "abilities": [{"id": "str", "name": "Strength"}, {"id": "dex", "name": "Dexterity"}, {"id": "con", "name": "Constitution"}],
         "skills": [{"id": "stealth", "name": "Stealth", "ability": "dex"}]}
        """;

    private static Ruleset Small()
    {
        return Ruleset.Read(TestContent.Json(SmallRuleset, "ruleset.json"));
    }

    [Fact]
    public void BadJsonNamesTheFileAndLine()
    {
        ContentException error = TestContent.Refused(() => TestContent.Json("{\n  \"id\": \"x\",\n}", "items/x.json"));
        Assert.Equal("items/x.json", error.File);
        Assert.Contains("items/x.json: line 3", error.Message);
    }

    [Fact]
    public void AMissingFileIsNamed()
    {
        ContentException error = TestContent.Refused(() => ContentNode.Read(TestContent.Shipped(), "items/no-such-thing.json"));
        Assert.Equal("items/no-such-thing.json: missing file", error.Message);
    }

    [Theory]
    [InlineData("items/mace.json", true)]
    [InlineData("chapters/goblin-keep", true)]
    [InlineData("../outside", false)]
    [InlineData("items/../../outside.json", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("C:/windows/win.ini", false)]
    [InlineData("items\\mace.json", false)]
    [InlineData("", false)]
    public void PathsStayInsideTheContent(string path, bool allowed)
    {
        Assert.Equal(allowed, ContentFiles.IsContentPath(path));
    }

    [Theory]
    [InlineData("2d6+1", true)]
    [InlineData("4d6kh3", true)]
    [InlineData("d%", true)]
    [InlineData("3", true)]
    [InlineData("1d8 + 2", true)]
    [InlineData("", false)]
    [InlineData("2d", false)]
    [InlineData("2d6+", false)]
    [InlineData("sword", false)]
    [InlineData("4d6kh5", false)]
    [InlineData("2000d6", false)]
    public void DiceTextIsChecked(string text, bool valid)
    {
        Assert.Equal(valid, DiceText.IsValid(text));
    }

    [Fact]
    public void ARulesetWithOnlyTheNeededFieldsGetsTheDefaults()
    {
        Ruleset rules = Small();
        Assert.Equal(ModifierTable.D20, rules.ModifierTable);
        Assert.Equal(1, rules.ActionsPerTurn);
        Assert.True(rules.BonusActions);
        Assert.False(rules.SharedTurns);
        Assert.Equal(5, rules.FeetPerSquare);
        Assert.Equal(10, rules.BaseDc);
        Assert.Equal(10, rules.PassiveBase);
        Assert.False(rules.Death.Enabled);
        Assert.Empty(rules.ProficiencyRanks);
        Assert.Equal(RecoveryKind.None, rules.AfterVictory.Kind);
        Assert.Equal("4d6kh3", rules.ScoreMethods.Roll);
        Assert.Equal(27, rules.ScoreMethods.PointBudget);
        Assert.Equal(-100, rules.Companions.ApprovalMin);
        Assert.Equal(8, rules.DefaultHitDie);
    }

    [Theory]
    [InlineData("\"actionsPerTurn\": 11", "actionsPerTurn")]
    [InlineData("\"initiativeAbility\": \"luck\"", "initiativeAbility")]
    [InlineData("\"modifierTable\": \"d12\"", "modifierTable")]
    [InlineData("\"xpForLevel\": [300, 100]", "xpForLevel[1]")]
    [InlineData("\"rests\": [{\"id\": \"long\", \"resets\": [\"nap\"]}]", "rests[0].resets")]
    [InlineData("\"rests\": [{\"id\": \"long\", \"recovery\": {\"kind\": \"magic\"}}]", "rests[0].recovery.kind")]
    [InlineData("\"proficiencyRanks\": [{\"id\": \"trained\"}], \"proficientRank\": \"trained\", \"untrainedRank\": \"none\"", "untrainedRank")]
    [InlineData("\"death\": {\"successes\": 0}", "death.successes")]
    [InlineData("\"scoreMethods\": {\"roll\": \"dice\"}", "scoreMethods.roll")]
    [InlineData("\"skills\": [{\"id\": \"lore\", \"name\": \"Lore\", \"ability\": \"int\"}]", "skills[0].ability")]
    public void ABrokenRulesetNamesTheField(string extra, string field)
    {
        string text = SmallRuleset.Trim().TrimEnd('}').Replace("\"skills\"", "\"oldSkills\"") + ", " + extra + "}";
        ContentException error = TestContent.Refused(() => Ruleset.Read(TestContent.Json(text, "rulesets/small/ruleset.json")));
        Assert.Equal("rulesets/small/ruleset.json", error.File);
        Assert.Equal(field, error.Field);
        Assert.StartsWith($"rulesets/small/ruleset.json: {field}: ", error.Message);
    }

    [Fact]
    public void ConditionsReadTheirFieldsAndDefaults()
    {
        ConditionDefinition frightened = ConditionDefinition.Read(TestContent.Json("""
            {"id": "frightened", "name": "Frightened",
             "modifiers": [{"stat": "attack", "op": "add", "value": -1}, {"stat": "speed", "op": "multiply", "value": 0.5}],
             "flags": ["frightened"], "stacking": "value", "maxValue": 4, "perValue": true, "decay": 1, "ends": ["rest"],
             "save": {"ability": "wis", "dc": 12}}
            """));
        Assert.Equal(ConditionStacking.Value, frightened.Stacking);
        Assert.Equal(4, frightened.MaxValue);
        Assert.True(frightened.PerValue);
        Assert.Equal(new Modifier("speed", ModifierOp.Multiply, 0.5), frightened.Modifiers[1]);
        Assert.True(frightened.HasFlag("frightened") && frightened.EndsOn("rest"));
        Assert.Equal(("wis", 12), (frightened.SaveAbility, frightened.SaveDc));

        ConditionDefinition bare = ConditionDefinition.Read(TestContent.Json("{\"id\": \"marked\"}"));
        Assert.Equal("marked", bare.Name);
        Assert.Equal(-1, bare.Duration);
        Assert.Equal(ConditionStacking.Refresh, bare.Stacking);
        Assert.Equal(1, bare.MaxValue);
        Assert.Empty(bare.Modifiers);
        Assert.Equal("", bare.SaveAbility);
    }

    [Theory]
    [InlineData("{\"name\": \"No id\"}", "id")]
    [InlineData("{\"id\": \"x\", \"stacking\": \"pile\"}", "stacking")]
    [InlineData("{\"id\": \"x\", \"ends\": [\"lunch\"]}", "ends")]
    [InlineData("{\"id\": \"x\", \"duration\": 0}", "duration")]
    [InlineData("{\"id\": \"x\", \"flags\": [\"a\", \"a\"]}", "flags")]
    [InlineData("{\"id\": \"x\", \"modifiers\": [{\"stat\": \"ac\", \"op\": \"halve\", \"value\": 1}]}", "modifiers[0].op")]
    [InlineData("{\"id\": \"x\", \"modifiers\": [{\"stat\": \"ac\"}]}", "modifiers[0].value")]
    [InlineData("{\"id\": \"x\", \"maxValue\": \"four\"}", "maxValue")]
    public void ABrokenConditionNamesTheField(string text, string field)
    {
        Assert.Equal(field, TestContent.Refused(() => ConditionDefinition.Read(TestContent.Json(text))).Field);
    }

    [Fact]
    public void AConditionFolderChecksTheSetTogether()
    {
        using var scratch = new Scratch();
        scratch.Write("conditions/prone.json", "{\"id\": \"prone\", \"removes\": [\"flying\"]}");
        Ruleset rules = Small();
        ContentException error = TestContent.Refused(() => rules.LoadConditions(new ContentFiles(scratch.Folder), "conditions"));
        Assert.Equal("conditions/prone.json: removes: unknown condition \"flying\"", error.Message);

        scratch.Write("conditions/prone.json", "{\"id\": \"fallen\"}");
        error = TestContent.Refused(() => Small().LoadConditions(new ContentFiles(scratch.Folder), "conditions"));
        Assert.Equal("conditions/prone.json", error.File);
        Assert.Contains("doesn't match the file name", error.Message);

        scratch.Write("conditions/prone.json", "{\"id\": \"prone\", \"save\": {\"ability\": \"luck\"}}");
        error = TestContent.Refused(() => Small().LoadConditions(new ContentFiles(scratch.Folder), "conditions"));
        Assert.Equal("save.ability", error.Field);
    }

    [Fact]
    public void EffectsReadTheirSteps()
    {
        Effect effect = Effect.Read(TestContent.Json("""
            {"save": {"ability": "dex", "dc": "caster"},
             "effects": [
               {"do": "damage", "dice": "3d6", "type": "fire", "target": "area", "onSave": "half", "scale": {"by": "slot", "dice": "1d6"}},
               {"do": "condition", "id": "prone", "when": "saveFailed", "duration": 2},
               {"do": "roll", "kind": "check", "ability": "stealth", "against": "stealth", "steps": [
                 {"do": "move", "how": "push", "distance": 2, "when": "success"}]},
               {"do": "tempHp", "dice": 5},
               {"do": "choose", "options": [{"name": "Mend", "steps": [{"do": "heal", "dice": "1d8"}]}]}
             ]}
            """));
        Assert.True(effect.Save.CasterDc);
        Assert.Equal("dex", effect.Save.Ability);
        Assert.Equal(5, effect.Steps.Count);

        EffectStep damage = effect.Steps[0];
        Assert.Equal((EffectKind.Damage, "3d6", "fire", "area", OnSave.Half), (damage.Kind, damage.Amount, damage.Type, damage.Target, damage.OnSave));
        Assert.Equal(new EffectScale(ScaleBy.Slot, 1, 1, "1d6", 0), damage.Scale);
        Assert.True(damage.CritDoubles);
        Assert.Equal(2, effect.Steps[1].Duration);
        // A check against someone's passive score is aimed at them; its steps follow the roll.
        Assert.Equal(("check", "target", "stealth"), (effect.Steps[2].How, effect.Steps[2].Target, effect.Steps[2].Against));
        Assert.Equal(("push", "2", "success"), (effect.Steps[2].Steps[0].How, effect.Steps[2].Steps[0].Amount, effect.Steps[2].Steps[0].When));
        Assert.Equal("5", effect.Steps[3].Amount);
        Assert.Equal("Mend", effect.Steps[4].Options[0].Name);

        // The list on its own is an effect too, and a condition step without a duration uses the condition's.
        Effect list = Effect.Read(TestContent.Json("[{\"do\": \"condition\", \"id\": \"prone\"}]"));
        Assert.Equal(EffectStep.DefinedDuration, list.Steps[0].Duration);
        Assert.Equal("", list.Save.Ability);
        // A check with no "against" is the doer's own.
        Effect own = Effect.Read(TestContent.Json("[{\"do\": \"roll\", \"kind\": \"check\", \"ability\": \"stealth\", \"steps\": []}]"));
        Assert.Equal("self", own.Steps[0].Target);
    }

    [Theory]
    [InlineData("[{\"do\": \"explode\"}]", "[0].do")]
    [InlineData("[{\"do\": \"damage\"}]", "[0].dice")]
    [InlineData("[{\"do\": \"damage\", \"dice\": \"lots\"}]", "[0].dice")]
    [InlineData("[{\"do\": \"damage\", \"dice\": \"1d6\", \"dise\": 2}]", "[0].dise")]
    [InlineData("[{\"do\": \"heal\", \"dice\": \"1d6\", \"onSave\": \"half\"}]", "[0].onSave")]
    [InlineData("[{\"do\": \"heal\", \"dice\": \"1d6\", \"when\": \"later\"}]", "[0].when")]
    [InlineData("[{\"do\": \"condition\", \"id\": \"prone\", \"duration\": 0}]", "[0].duration")]
    [InlineData("[{\"do\": \"roll\", \"kind\": \"attack\"}]", "[0].steps")]
    [InlineData("[{\"do\": \"roll\", \"kind\": \"attack\", \"dc\": 12, \"steps\": []}]", "[0]")]
    [InlineData("[{\"do\": \"roll\", \"kind\": \"save\", \"ability\": \"dex\", \"steps\": [{\"do\": \"move\", \"how\": \"shove\"}]}]", "[0].steps[0].how")]
    [InlineData("[{\"do\": \"flag\", \"id\": \"seen\", \"scale\": {\"by\": \"level\", \"value\": 1}}]", "[0].scale")]
    [InlineData("{\"effects\": [], \"extra\": 1}", "extra")]
    [InlineData("{\"effects\": [], \"save\": {\"ability\": \"dex\", \"dc\": \"hard\"}}", "save.dc")]
    public void ABrokenEffectNamesTheStep(string text, string field)
    {
        Assert.Equal(field, TestContent.Refused(() => Effect.Read(TestContent.Json(text))).Field);
    }

    [Fact]
    public void StepsNestSixDeepAndNoFurther()
    {
        string Nested(int depth)
        {
            string step = "{\"do\": \"heal\", \"dice\": 1}";
            for (int i = 1; i < depth; i++)
            {
                step = "{\"do\": \"repeat\", \"times\": 2, \"steps\": [" + step + "]}";
            }
            return "[" + step + "]";
        }
        Assert.Single(Effect.Read(TestContent.Json(Nested(6))).Steps);
        Assert.Contains("nested too deep", TestContent.Refused(() => Effect.Read(TestContent.Json(Nested(7)))).Message);
    }

    [Theory]
    [InlineData("[{\"do\": \"condition\", \"id\": \"cursed\"}]", "effects[0].id")]
    [InlineData("[{\"do\": \"roll\", \"kind\": \"save\", \"ability\": \"luck\", \"steps\": []}]", "effects[0].ability")]
    [InlineData("[{\"do\": \"roll\", \"kind\": \"check\", \"ability\": \"stealth\", \"against\": \"luck\", \"steps\": []}]", "effects[0].against")]
    [InlineData("[{\"do\": \"damage\", \"dice\": \"1d6\", \"onSave\": \"half\"}]", "effects[0].onSave")]
    [InlineData("{\"save\": {\"ability\": \"luck\"}, \"effects\": []}", "save.ability")]
    public void EffectsAreCheckedAgainstTheRuleset(string text, string field)
    {
        Effect effect = Effect.Read(TestContent.Json(text));
        ContentException error = TestContent.Refused(() => effect.Check(Small(), "spells/x.json"));
        Assert.Equal(("spells/x.json", field), (error.File, error.Field));
    }

    [Fact]
    public void AStepUnderASaveRollMayHalve()
    {
        Effect effect = Effect.Read(TestContent.Json(
            "[{\"do\": \"roll\", \"kind\": \"save\", \"ability\": \"dex\", \"dc\": 12, \"steps\": [{\"do\": \"damage\", \"dice\": \"2d6\", \"onSave\": \"half\"}]}]"));
        effect.Check(Small(), "x.json");
    }

    [Fact]
    public void ActionsReadTheirFieldsAndDefaults()
    {
        ActionDefinition strike = ActionDefinition.Read(TestContent.Json("""
            {"id": "strike", "name": "Strike", "order": 10, "cost": "hands",
             "target": {"kind": "creature", "side": "enemy", "range": 1},
             "requires": {"flags": ["armed"], "without": ["prone"], "resources": {"arrows": 1}},
             "effects": [{"do": "roll", "kind": "attack", "steps": [{"do": "damage", "dice": "weapon", "when": "hit", "minimum": 1}]}]}
            """));
        Assert.True(strike.CostsHands);
        Assert.Equal((ActionTarget.Creature, ActionSide.Enemy, 1), (strike.Target, strike.Side, strike.Range));
        Assert.Equal(new[] { "armed" }, strike.NeedsFlags);
        Assert.Equal(new[] { "prone" }, strike.BarredBy);
        Assert.Equal(1, strike.NeedsResources["arrows"]);
        Assert.Equal("weapon", strike.Effect.Steps[0].Steps[0].Amount);

        ActionDefinition bare = ActionDefinition.Read(TestContent.Json("{\"id\": \"wait\"}"));
        Assert.Equal(("wait", 1, false, true, ActionTarget.Self), (bare.Name, bare.Cost, bare.EndsTurn, bare.General, bare.Target));
        Assert.True(bare.Effect.IsEmpty);
        Assert.Null(bare.Area);

        ActionDefinition burst = ActionDefinition.Read(TestContent.Json(
            "{\"id\": \"hymn\", \"target\": {\"side\": \"ally\"}, \"area\": {\"shape\": \"burst\", \"size\": 3}}"));
        Assert.Equal(new ActionArea(AreaShape.Burst, 3), burst.Area);
        Assert.Equal(ActionSide.Ally, burst.Side);
    }

    [Theory]
    [InlineData("{\"name\": \"No id\"}", "id")]
    [InlineData("{\"id\": \"x\", \"cost\": 11}", "cost")]
    [InlineData("{\"id\": \"x\", \"cooldown\": 2}", "cooldown")]
    [InlineData("{\"id\": \"x\", \"target\": {\"kind\": \"everyone\"}}", "target.kind")]
    [InlineData("{\"id\": \"x\", \"target\": {\"kind\": \"creature\", \"range\": 0}}", "target.range")]
    [InlineData("{\"id\": \"x\", \"target\": {\"kind\": \"self\", \"side\": \"ally\"}}", "target")]
    [InlineData("{\"id\": \"x\", \"target\": {\"kind\": \"point\"}}", "area")]
    [InlineData("{\"id\": \"x\", \"area\": {\"shape\": \"cone\", \"size\": 3}}", "area.shape")]
    [InlineData("{\"id\": \"x\", \"target\": {\"kind\": \"point\"}, \"area\": {\"shape\": \"burst\"}}", "area.size")]
    [InlineData("{\"id\": \"x\", \"target\": {\"kind\": \"point\"}, \"area\": {\"shape\": \"burst\", \"size\": 2, \"width\": 1}}", "area.width")]
    [InlineData("{\"id\": \"x\", \"requires\": {\"resources\": {\"potions\": 0}}}", "requires.resources.potions")]
    [InlineData("{\"id\": \"x\", \"effects\": [{\"do\": \"heal\"}]}", "effects[0].dice")]
    public void ABrokenActionNamesTheField(string text, string field)
    {
        Assert.Equal(field, TestContent.Refused(() => ActionDefinition.Read(TestContent.Json(text))).Field);
    }

    [Fact]
    public void ARulesetWithoutActionFilesGetsTheBasicThree()
    {
        List<ActionDefinition> actions = ActionDefinition.Basic(Small());
        Assert.Equal(new[] { "strike", "stride", "end-turn" }, actions.Select(a => a.Id));
        Assert.False(actions[0].CostsHands);
        Assert.True(actions[2].EndsTurn);
    }

    [Fact]
    public void AnActionFolderReplacesSortsAndChecks()
    {
        using var scratch = new Scratch();
        scratch.Write("actions/strike.json", "{\"id\": \"strike\", \"name\": \"Smack\", \"order\": 30}");
        scratch.Write("actions/ready.json", "{\"id\": \"ready\", \"order\": 5, \"readies\": \"strike\", \"endsTurn\": true}");
        var files = new ContentFiles(scratch.Folder);
        Ruleset rules = Small();
        List<ActionDefinition> actions = ActionDefinition.Basic(rules);
        ActionDefinition.LoadFolder(files, "actions", rules, actions);
        Assert.Equal(new[] { "ready", "stride", "strike", "end-turn" }, actions.Select(a => a.Id));
        Assert.Equal("Smack", actions[2].Name);

        scratch.Write("actions/ready.json", "{\"id\": \"ready\", \"readies\": \"end-turn\"}");
        ContentException error = TestContent.Refused(() => ActionDefinition.LoadFolder(files, "actions", rules, ActionDefinition.Basic(rules)));
        Assert.Equal(("actions/ready.json", "readies"), (error.File, error.Field));

        scratch.Write("actions/ready.json", "{\"id\": \"ready\", \"effects\": [{\"do\": \"condition\", \"id\": \"braced\"}]}");
        error = TestContent.Refused(() => ActionDefinition.LoadFolder(files, "actions", rules, ActionDefinition.Basic(rules)));
        Assert.Equal("actions/ready.json: effects[0].id: unknown condition \"braced\"", error.Message);
    }

    [Fact]
    public void ReactionsNameAnActionOrAreReadied()
    {
        ReactionDefinition opportunity = ReactionDefinition.Read(TestContent.Json(
            "{\"id\": \"opportunity\", \"trigger\": \"leavesReach\", \"action\": \"strike\"}"));
        Assert.Equal(("opportunity", ReactionTrigger.LeavesReach, "strike", false, 0, 2.0),
            (opportunity.Name, opportunity.Trigger, opportunity.Action, opportunity.Readied, opportunity.Order, opportunity.PromptSeconds));

        Assert.Equal("action", TestContent.Refused(() => ReactionDefinition.Read(TestContent.Json(
            "{\"id\": \"x\", \"trigger\": \"entersReach\", \"action\": \"strike\", \"readied\": true}"))).Field);
        Assert.Equal("trigger", TestContent.Refused(() => ReactionDefinition.Read(TestContent.Json("{\"id\": \"x\", \"action\": \"strike\"}"))).Field);
        Assert.Equal("promptSeconds", TestContent.Refused(() => ReactionDefinition.Read(TestContent.Json(
            "{\"id\": \"x\", \"trigger\": \"entersReach\", \"readied\": true, \"promptSeconds\": 60}"))).Field);
        Assert.Equal("when", TestContent.Refused(() => ReactionDefinition.Read(TestContent.Json(
            "{\"id\": \"x\", \"trigger\": \"entersReach\", \"readied\": true, \"when\": \"always\"}"))).Field);

        using var scratch = new Scratch();
        scratch.Write("reactions/parting.json", "{\"id\": \"parting\", \"trigger\": \"leavesReach\", \"action\": \"end-turn\"}");
        ContentException error = TestContent.Refused(() => ReactionDefinition.LoadFolder(new ContentFiles(scratch.Folder), "reactions", ActionDefinition.Basic(Small())));
        Assert.Equal(("reactions/parting.json", "action"), (error.File, error.Field));
    }

    [Fact]
    public void SpellsAreActionsWithALevelAndHands()
    {
        SpellDefinition fan = SpellDefinition.Read(TestContent.Json("""
            {"id": "flame-fan", "name": "Flame fan", "level": 1, "hands": 2,
             "target": {"kind": "point", "side": "any"}, "area": {"shape": "cone", "size": 3},
             "save": {"ability": "dex", "dc": "caster"},
             "effects": [{"do": "damage", "dice": "2d6", "type": "fire", "onSave": "half", "scale": {"by": "slot", "dice": "1d6"}}]}
            """));
        // One action per hand unless it says, listed after the general actions, and only for those who know it.
        Assert.Equal((1, 2, 2, 501, false), (fan.Level, fan.Hands, fan.Action.Cost, fan.Action.Order, fan.Action.General));
        Assert.Equal(53.13, fan.Action.Area?.Angle);

        SpellDefinition word = SpellDefinition.Read(TestContent.Json(
            "{\"id\": \"kind-word\", \"hands\": 0, \"cost\": 1, \"spends\": [\"focus\", \"focus\"], \"effects\": [{\"do\": \"heal\", \"dice\": 1}]}"));
        Assert.Equal((0, 1, 2), (word.Level, word.Action.Cost, word.Spends["focus"]));
        Assert.False(word.Concentration);
    }

    [Theory]
    [InlineData("{\"id\": \"x\", \"effects\": []}", "effects")]
    [InlineData("{\"id\": \"x\", \"level\": 21, \"effects\": [{\"do\": \"heal\", \"dice\": 1}]}", "level")]
    [InlineData("{\"id\": \"x\", \"endsTurn\": true, \"effects\": [{\"do\": \"heal\", \"dice\": 1}]}", "endsTurn")]
    [InlineData("{\"id\": \"x\", \"cost\": \"hands\", \"effects\": [{\"do\": \"heal\", \"dice\": 1}]}", "cost")]
    [InlineData("{\"id\": \"x\", \"spends\": {\"focus\": 0}, \"effects\": [{\"do\": \"heal\", \"dice\": 1}]}", "spends")]
    [InlineData("{\"id\": \"x\", \"school\": \"fire\", \"effects\": [{\"do\": \"heal\", \"dice\": 1}]}", "school")]
    public void ABrokenSpellNamesTheField(string text, string field)
    {
        Assert.Equal(field, TestContent.Refused(() => SpellDefinition.Read(TestContent.Json(text))).Field);
    }

    [Fact]
    public void TheSmallRuleFilesDefaultEveryField()
    {
        StealthRules stealth = StealthRules.Read(TestContent.Json("{}"));
        Assert.Equal((5.0, 0.5, 5, 2, 0, true), (stealth.CheckEvery, stealth.SneakSpeed, stealth.DarkBonus, stealth.DimBonus, stealth.BrightBonus, stealth.Critical));
        Assert.Equal("sneakSpeed", TestContent.Refused(() => StealthRules.Read(TestContent.Json("{\"sneakSpeed\": 0}"))).Field);
        Assert.Equal("darkBonus", TestContent.Refused(() => StealthRules.Read(TestContent.Json("{\"darkBonus\": 25}"))).Field);

        // A positioning file that is there turns positioning on; no file leaves it off.
        Assert.True(PositioningRules.Read(TestContent.Json("{}")).Enabled);
        Assert.False(new PositioningRules().Enabled);
        Assert.Equal("flanking", TestContent.Refused(() => PositioningRules.Read(TestContent.Json("{\"flanking\": true}"))).Field);
        Assert.Equal("threeQuartersCoverArmorClass", TestContent.Refused(() => PositioningRules.Read(TestContent.Json(
            "{\"halfCoverArmorClass\": 4, \"threeQuartersCoverArmorClass\": 2}"))).Field);
        ContentException flanking = TestContent.Refused(() => PositioningRules.Read(TestContent.Json("{\"flankingCondition\": \"pinned\"}")).Check(Small(), "positioning.json"));
        Assert.Equal("positioning.json: flankingCondition: unknown condition \"pinned\"", flanking.Message);

        SpellRules spells = SpellRules.Read(TestContent.Json("{}"));
        Assert.Equal((SpellHands.Free, "slots-", true, ConcentrationDamage.Save, "con", 10, 0.5, true),
            (spells.Hands, spells.SlotPrefix, spells.Upcast, spells.OnDamage, spells.SaveAbility, spells.MinimumDc, spells.DamageShare, spells.EndsWhenDown));
        Assert.Empty(spells.PrepareAfter);
        Assert.Equal("concentration.onDamage", TestContent.Refused(() => SpellRules.Read(TestContent.Json("{\"concentration\": {\"onDamage\": \"shrug\"}}"))).Field);
        Assert.Equal("concentration.ability", TestContent.Refused(() => SpellRules.Read(TestContent.Json("{\"concentration\": {\"ability\": \"luck\"}}")).Check(Small(), "s.json")).Field);
    }

    [Fact]
    public void LootTablesTakeIdsOrEntries()
    {
        LootTable table = LootTable.Read(TestContent.Json("{\"coins\": \"2d6\", \"items\": [\"torch\", {\"item\": \"dagger\", \"chance\": 0.25, \"quantity\": 2}]}"));
        Assert.Equal("2d6", table.Coins);
        Assert.Equal(new[] { new LootEntry("torch"), new LootEntry("dagger", 0.25, 2) }, table.Items);
        Assert.Equal("15", LootTable.Read(TestContent.Json("{\"coins\": 15}")).Coins);
        Assert.True(LootTable.Read(TestContent.Json("{\"coins\": 0}")).IsEmpty);

        Assert.Equal("coins", TestContent.Refused(() => LootTable.Read(TestContent.Json("{\"coins\": \"a few\"}"))).Field);
        Assert.Equal("items[0].chance", TestContent.Refused(() => LootTable.Read(TestContent.Json("{\"items\": [{\"item\": \"x\", \"chance\": 2}]}"))).Field);
        Assert.Equal("items[0].count", TestContent.Refused(() => LootTable.Read(TestContent.Json("{\"items\": [{\"item\": \"x\", \"count\": 2}]}"))).Field);
        Assert.Equal("gems", TestContent.Refused(() => LootTable.Read(TestContent.Json("{\"gems\": 2}"))).Field);
    }
}
