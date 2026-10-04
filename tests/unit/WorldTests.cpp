// The game played through World alone: scripted intents in, state out.

#include "WorldFixture.h"

#include <algorithm>
#include <cstdio>
#include <functional>
#include <limits>

namespace
{

using Check = std::function<void(bool, const char*)>;

// The nearest enemy still standing to `from`, if any; in a fight, only those in it.
std::optional<size_t> nearestEnemy(const WorldFixture& world, size_t from)
{
    std::optional<size_t> best;
    float bestDistance = 0;
    for (size_t i = world.heroCount(); i < world.creatures().size(); i++)
    {
        const World::Creature& c = world.creatures()[i];
        if (c.team != 1 || c.sheet.down() || (world.fighting() && !world.orderIndex(i)))
            continue;
        const float d = world.grid().distance(world.cellOf(from), world.cellOf(i));
        if (!best || d < bestDistance)
        {
            best = i;
            bestDistance = d;
        }
    }
    return best;
}

bool walking(const WorldFixture& world)
{
    const std::vector<yh::Token>& tokens = world.tokens().tokens;
    return std::any_of(tokens.begin(), tokens.end(), [](const yh::Token& t) { return !t.path.empty(); });
}

// One decision for the party, the way a plain player would make it: walk the leader at the nearest
// enemy, rest when hurt, and in a fight walk up to the nearest enemy and hit it until it drops.
void playParty(WorldFixture& world)
{
    if (world.talk())
    {
        world.send("leave");
        return;
    }
    if (!world.fighting())
    {
        const std::vector<World::Creature>& party = world.creatures();
        const bool hurt = std::any_of(party.begin(), party.begin() + world.heroCount(),
            [](const World::Creature& c) { return !c.sheet.down() && c.sheet.hp * 2 < c.sheet.maxHp(); });
        const auto& rests = world.rules().rests;
        for (size_t i = 0; i < rests.size(); i++)
            if (hurt && world.restsLeft(rests[i]) != 0 && world.send("rest", {{"rest", i}}))
                return;
        const size_t leader = world.leaderIndex();
        if (!world.tokens().tokens[leader].path.empty())
            return;
        if (const std::optional<size_t> enemy = nearestEnemy(world, leader))
        {
            const yh::Cell at = world.cellOf(*enemy);
            world.send("go", {{"hero", leader}, {"at", {at.x, at.y}}});
        }
        return;
    }

    const std::optional<size_t> me = world.currentCreature();
    if (!me || world.creatures()[*me].team != 0 || walking(world))
        return;
    const yh::Encounter& fight = *world.encounter();
    const std::optional<size_t> enemy = nearestEnemy(world, *me);
    if (!enemy)
        return;
    if (fight.canStrike() && world.adjacent(*me, *enemy) && world.send("attack", {{"target", *enemy}}))
        return;
    // The reachable square nearest the enemy, cheapest first among equals.
    const yh::Cell goal = world.cellOf(*enemy);
    std::optional<yh::Cell> best;
    float bestDistance = world.grid().distance(world.standing(), goal), bestCost = std::numeric_limits<float>::max();
    for (const auto& [cell, cost] : world.reach())
    {
        const float d = world.grid().distance(cell, goal);
        if (d < bestDistance - 0.01f || (best && d < bestDistance + 0.01f && cost < bestCost))
        {
            best = cell;
            bestDistance = d;
            bestCost = cost;
        }
    }
    if (best && *best != world.standing() && world.send("step", {{"at", {best->x, best->y}}}))
        return;
    world.send("end");
}

// With this seed the plain script above wins (with others the dice can go against it, which is
// fine: the point is that a whole adventure runs through World, intents in and state out).
void keepVictory(const Check& check)
{
    WorldFixture world;
    std::string error;
    check(world.load("chapters/goblin-keep", 2, &error), "The fixture loads the keep from its folder");
    if (!world.chapter())
    {
        std::fprintf(stderr, "%s\n", error.c_str());
        return;
    }
    check(world.said("Goblins have taken it."), "The chapter's intro is in the log");
    check(world.rules().id == "yorehold", "The keep plays by the game's own ruleset");
    check(!world.send("attack", {{"target", world.heroCount()}}), "An attack while exploring is refused");
    check(!world.send("end"), "Ending a turn outside a fight is refused");

    int fights = 0;
    bool wasFighting = false;
    const bool over = world.stepUntil([&] {
        if (world.ended || world.partyDown())
            return true;
        playParty(world);
        if (world.fighting() && !wasFighting)
            fights++;
        wasFighting = world.fighting();
        return world.ended || world.partyDown();
    }, 1800);
    if (!over || world.partyDown())
    {
        // What happened last, to see where the script got stuck.
        std::fprintf(stderr, "keep run: %d fights, fighting %d, leader at %d,%d\n", fights, world.fighting() ? 1 : 0,
            world.cellOf(world.leaderIndex()).x, world.cellOf(world.leaderIndex()).y);
        for (size_t i = 0; i < world.creatures().size(); i++)
            if (!world.tokens().tokens[i].path.empty())
                std::fprintf(stderr, "  %s is still walking (%zu steps left)\n", world.creatures()[i].sheet.name.c_str(), world.tokens().tokens[i].path.size());
        for (size_t i = world.log.size() > 20 ? world.log.size() - 20 : 0; i < world.log.size(); i++)
            std::fprintf(stderr, "  %s\n", world.log[i].c_str());
    }
    check(over && !world.partyDown(), "The scripted party clears the keep without falling");
    check(world.chapterCleared() && world.ended, "Clearing the keep asks for its ending");
    check(fights >= 1 && world.said("Victory"), "The keep was won in a fight");
    check(std::all_of(world.creatures().begin() + world.heroCount(), world.creatures().end(),
              [](const World::Creature& c) { return c.npc >= 0 || c.sheet.down() || c.surrendered; }),
        "No enemy is left standing");
}

// A chapter written in the test: a fighter and a cleric, one goblin round a wall, no ending file.
void chapterFromStrings(const Check& check)
{
    const nlohmann::json chapter{
        {"id", "fixture-yard"},
        {"title", "The Yard"},
        {"ruleset", "modern"},
        {"map", "map.json"},
        {"intro", {"A goblin waits in the yard."}},
        {"xpPerVictory", 10},
        {"party", {{{"name", "Ana"}, {"class", "fighter"}, {"color", {220, 90, 80}}, {"at", {1, 1}}},
                      {{"name", "Bo"}, {"class", "cleric"}, {"color", {90, 160, 230}}, {"at", {1, 2}}}}},
        {"encounters", {{{"id", "yard"}, {"set", {"yard_clear"}}, {"text", "A goblin!"},
                           {"creatures", {{{"creature", "goblin"}, {"name", "Gik"}, {"at", {8, 4}}}}}}}},
        {"victoryText", "Won ({xp} XP)."},
        {"defeatText", "Lost."},
        {"resumeText", "Back."},
        {"clearedText", "The yard is clear."},
    };
    const nlohmann::json map{
        {"name", "Yard"},
        {"tiles", {{"grass", {{"art", "grass"}}}, {"wall", {{"art", "wall"}, {"walkable", false}, {"blocksSight", true}}}}},
        {"legend", {{".", "grass"}, {"#", "wall"}}},
        {"layers", {{{"name", "ground"}, {"rows", {
            "##########",
            "#........#",
            "#........#",
            "#####....#",
            "#........#",
            "##########",
        }}}}},
    };
    WorldFixture world;
    std::string error;
    const bool loaded = world.loadJson("chapters/fixture-yard",
        {{"chapters/fixture-yard/chapter.json", chapter.dump()}, {"chapters/fixture-yard/map.json", map.dump()}}, 5, &error);
    check(loaded, "The fixture loads a chapter written as JSON strings");
    if (!loaded)
    {
        std::fprintf(stderr, "%s\n", error.c_str());
        return;
    }
    check(world.heroCount() == 2 && world.creatures().size() == 3 && world.said("A goblin waits"), "The JSON chapter has its party, goblin and intro");
    check(world.rules().id == "modern" && world.chapter()->stealth.checkEvery == 5, "A chapter can still name a built-in ruleset");
    check(!world.send("go", {{"hero", 0}, {"at", {0, 0}}}), "Walking into a wall is refused");
    check(world.send("go", {{"hero", 0}, {"at", {7, 4}}}), "A hero is sent across the yard");
    check(world.stepUntil([&] { return world.fighting(); }, 30) && world.said("A goblin!"), "Seeing the goblin starts its fight");
    const bool done = world.stepUntil([&] {
        playParty(world);
        return !world.fighting();
    }, 300);
    check(done && !world.partyDown() && world.chapterCleared() && world.flags().contains("yard_clear"), "The fight is won and sets its flag");
    check(world.said("Won (10 XP).") && world.said("The yard is clear.") && !world.ended,
        "Without an ending file, clearing the chapter says its text instead");
}

// A yard with one goblin round a wall, played by the game's own ruleset.
std::map<std::string, std::string> yardFiles()
{
    const nlohmann::json chapter{
        {"id", "condition-yard"},
        {"title", "The Yard"},
        {"map", "map.json"},
        {"party", {{{"name", "Ana"}, {"class", "fighter"}, {"at", {1, 1}}}, {{"name", "Bo"}, {"class", "cleric"}, {"at", {1, 2}}}}},
        {"encounters", {{{"id", "yard"}, {"creatures", {{{"creature", "goblin"}, {"name", "Gik"}, {"at", {8, 4}}}}}}}},
    };
    const nlohmann::json map{
        {"name", "Yard"},
        {"tiles", {{"grass", {{"art", "grass"}}}, {"wall", {{"art", "wall"}, {"walkable", false}, {"blocksSight", true}}}}},
        {"legend", {{".", "grass"}, {"#", "wall"}}},
        {"layers", {{{"name", "ground"}, {"rows", {
            "##########",
            "#........#",
            "#........#",
            "#####....#",
            "#........#",
            "##########",
        }}}}},
    };
    return {{"chapters/condition-yard/chapter.json", chapter.dump()}, {"chapters/condition-yard/map.json", map.dump()}};
}

// The ruleset's condition files, and the conditions the game itself puts on creatures.
void conditionsInPlay(const Check& check)
{
    WorldFixture world;
    std::string error;
    const bool loaded = world.loadJson("chapters/condition-yard", yardFiles(), 5, &error);
    check(loaded, "A chapter with no ruleset of its own loads with the game's conditions");
    if (!loaded)
    {
        std::fprintf(stderr, "%s\n", error.c_str());
        return;
    }
    const yh::Ruleset& rules = world.rules();
    bool all = rules.conditions.size() == 10;
    for (const char* id : {"off-guard", "frightened", "prone", "slowed", "grabbed", "hidden", "downed", "dying", "dead", "shielded"})
        all = all && rules.condition(id) && !rules.condition(id)->name.empty() && !rules.condition(id)->description.empty();
    check(all, "The ruleset folder supplies the ten conditions, one file each");
    using Stacking = yh::ConditionDefinition::Stacking;
    check(all && rules.condition("frightened")->stacking == Stacking::Value && rules.condition("frightened")->maxValue == 4
            && rules.condition("frightened")->decay == 1 && rules.condition("off-guard")->endsOn("turnStart")
            && rules.condition("hidden")->endsOn("attack") && rules.condition("downed")->hasFlag("cantAct")
            && rules.condition("grabbed")->hasFlag("cantMove") && rules.condition("downed")->endsOn("healed"),
        "Their flags, stacking and endings come from the files");
    if (!all)
        return;

    // Applying, stacking and ending, with the shipped numbers.
    yh::Character& ana = world.sheet(0);
    const int ac = ana.armorClass(rules), attack = ana.attackModifier(rules), speed = ana.speedFeet();
    ana.addCondition(rules, "off-guard");
    ana.addCondition(rules, "shielded");
    check(ana.armorClass(rules) == ac && ana.hasFlag(rules, "offGuard"), "Off-guard takes 2 from AC and a raised shield adds 2");
    ana.conditionEvent(rules, "turnStart");
    check(ana.conditions.empty() && ana.armorClass(rules) == ac, "Both last until the creature's next turn starts");
    ana.addCondition(rules, "frightened", yh::Character::definedDuration, 2);
    ana.addCondition(rules, "frightened");
    check(ana.conditionValue("frightened") == 3 && ana.attackModifier(rules) == attack - 3 && ana.armorClass(rules) == ac - 3,
        "Frightened stacks by value and weakens attack and AC by it");
    ana.addCondition(rules, "frightened", yh::Character::definedDuration, 5);
    check(ana.conditionValue("frightened") == 4, "Frightened stops at 4");
    ana.endRound(rules);
    check(ana.conditionValue("frightened") == 3 && ana.attackModifier(rules) == attack - 3, "It drops by one each round");
    for (int round = 0; round < 3; round++)
        ana.endRound(rules);
    check(!ana.hasCondition("frightened") && ana.attackModifier(rules) == attack && ana.armorClass(rules) == ac, "and ends when it reaches nothing");
    ana.addCondition(rules, "slowed");
    ana.addCondition(rules, "prone");
    ana.addCondition(rules, "grabbed");
    check(ana.speedFeet() == speed / 2 && ana.attackModifier(rules) == attack - 2 && ana.armorClass(rules) == ac - 4 && ana.hasFlag(rules, "cantMove"),
        "Slowed halves speed, prone hampers attacks, and prone and grabbed are both easier to hit");
    ana.conditionEvent(rules, "fightEnd");
    check(ana.hasCondition("slowed") && ana.conditions.size() == 1, "Prone and grabbed end with the fight");
    ana.addCondition(rules, "downed");
    ana.addCondition(rules, "dying");
    ana.addCondition(rules, "dead");
    check(ana.hasCondition("dead") && !ana.hasCondition("downed") && !ana.hasCondition("dying") && ana.hasFlag(rules, "cantAct"), "Dead replaces downed and dying");
    ana.removeCondition("dead");
    ana.addCondition(rules, "frightened");

    // Sneaking is the Hidden condition on the sheet, and travels with the save.
    check(world.send("sneak", {{"on", true}}), "The party starts sneaking");
    check(world.creatures()[0].sneaking() && world.sheet(1).hasCondition("hidden") && world.sheet(1).hasFlag(rules, "hidden") && world.sneakingMine(),
        "Sneaking heroes are Hidden");
    const std::string saved = world.stateJson();
    check(world.send("sneak", {{"on", false}}) && !world.sheet(0).hasCondition("hidden") && !world.sneakingMine(), "Stopping takes Hidden off");
    check(world.restoreState(saved, &error) && world.creatures()[0].sneaking() && world.creatures()[1].sneaking() && world.sheet(0).hasCondition("slowed"),
        "A save brings conditions back");
    nlohmann::json old = nlohmann::json::parse(saved);
    for (nlohmann::json& creature : old["creatures"])
        creature["sheet"]["conditions"] = nlohmann::json::array();
    check(world.restoreState(old.dump(), &error) && world.creatures()[0].sneaking() && world.sheet(0).hasCondition("hidden"),
        "A save from before sneaking was a condition still restores it");

    // Downed is a hero at 0 HP: a short rest leaves them down, a long one gets them up; resting ends Frightened and Slowed.
    world.sheet(0).addCondition(rules, "frightened");
    world.sheet(1).hp = 0;
    check(world.send("rest", {{"rest", 0}}), "The party takes a short rest");
    check(world.sheet(1).hasCondition("downed") && world.sheet(1).hasFlag(rules, "cantAct") && !world.sheet(1).hasCondition("hidden")
            && !world.sheet(0).hasCondition("downed") && !world.sheet(0).hasCondition("frightened"),
        "A hero at 0 HP is Downed and no longer Hidden; a rest ends fear");
    check(world.send("rest", {{"rest", 1}}) && !world.sheet(1).down() && !world.sheet(1).hasCondition("downed") && !world.sheet(1).hasFlag(rules, "cantAct"),
        "Being healed ends Downed");

    // A fight starting gives sneakers away; whoever falls in it is Dead (enemies) or Downed (heroes).
    world.send("sneak", {{"on", true}});
    check(world.send("go", {{"hero", 0}, {"at", {7, 4}}}), "A sneaking hero heads for the goblin");
    const size_t goblin = world.heroCount();
    world.stepUntil([&] { return world.fighting() || world.fog().state(0, 0, world.cellOf(goblin)) == yh::FogState::Visible; }, 60);
    if (!world.fighting())
        check(world.send("ambush", {{"creature", goblin}}), "The hidden party ambushes the goblin");
    check(world.fighting() && !world.creatures()[0].sneaking() && !world.sheet(1).hasCondition("hidden"), "The fight ends Hidden for everyone");
    const bool done = world.stepUntil([&] {
        playParty(world);
        return !world.fighting();
    }, 300);
    bool heroesRight = true;
    for (size_t i = 0; i < world.heroCount(); i++)
        heroesRight = heroesRight && world.sheet(i).hasCondition("downed") == world.sheet(i).down() && !world.sheet(i).hasCondition("dead");
    // One that ran off is out of the adventure instead, with nothing left on its sheet.
    check(done && world.sheet(goblin).down() && world.sheet(goblin).hasCondition("dead") != world.creatures()[goblin].fled
            && world.sheet(goblin).hasFlag(rules, "dead") != world.creatures()[goblin].fled,
        "A goblin that falls in the fight is Dead");
    check(heroesRight, "Heroes are Downed exactly while they are at 0 HP");

    // A condition file that fails its checks stops the chapter loading and is named.
    std::map<std::string, std::string> broken = yardFiles();
    broken["rulesets/yorehold/conditions/cursed.json"] = R"({"id": "cursed", "ends": ["never"]})";
    WorldFixture refused;
    check(!refused.loadJson("chapters/condition-yard", broken, 5, &error) && error.find("conditions/cursed.json") != std::string::npos
            && error.find("never") != std::string::npos,
        "A bad condition file stops the chapter and is named");
    std::map<std::string, std::string> extra = yardFiles();
    extra["rulesets/yorehold/conditions/cursed.json"] = R"({"id": "cursed", "name": "Cursed", "modifiers": [{"stat": "attack", "value": -1}]})";
    WorldFixture added;
    check(added.loadJson("chapters/condition-yard", extra, 5, &error) && added.rules().conditions.size() == 11 && added.rules().condition("cursed"),
        "A new condition is one more file");
}

}

void worldPlayTests(const Check& check)
{
    keepVictory(check);
    chapterFromStrings(check);
    conditionsInPlay(check);
}
