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

}

void worldPlayTests(const Check& check)
{
    keepVictory(check);
}
