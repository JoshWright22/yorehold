#include "WorldFixture.h"

#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;

bool loadFight(WorldFixture& world)
{
    const char* chapter = R"({"id":"actions-yard","title":"Yard","map":"map.json",
        "party":[{"name":"Ana","class":"fighter","at":[3,3]},{"name":"Bo","class":"cleric","at":[3,4]}],
        "encounters":[{"id":"yard","creatures":[{"creature":"goblin","name":"Gik","at":[4,3]},
            {"creature":"goblin","name":"Gok","at":[6,3]}]}]})";
    const char* map = R"({"name":"Yard","tiles":{"floor":{"art":"grass"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
        "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":[
            "########","#......#","#......#","#......#","#......#","#......#","#......#","########"]}]})";
    if (!world.loadJson("chapters/actions-yard", {{"chapters/actions-yard/chapter.json", chapter}, {"chapters/actions-yard/map.json", map}}, 5))
        return false;
    world.sheet(0).stats.setBase("dex", 2000);
    world.sheet(0).stats.setBase("str", 2000);
    world.sheet(0).stats.setBase("wis", 2000);
    for (size_t i = 0; i < world.creatures().size(); i++)
    {
        world.sheet(i).stats.setBase("maxHp", 1000);
        world.sheet(i).hp = 1000;
    }
    nlohmann::json positions = nlohmann::json::array();
    for (const yh::Token& token : world.tokens().tokens)
        positions.push_back({token.position.x, token.position.y});
    return world.send("fight", {{"group", 0}, {"at", positions}}) && world.currentCreature() == 0;
}

bool nextAna(WorldFixture& world)
{
    for (int i = 0; i < 8; i++)
    {
        if (!world.send("use", {{"action", "end-turn"}}))
            return false;
        if (world.currentCreature() == 0)
            return true;
    }
    return false;
}

void defendingAndHelping(const Check& check)
{
    WorldFixture world;
    check(loadFight(world), "The action yard starts with Ana's turn");
    if (!world.fighting()) return;
    check(world.actionsOf(0).size() == 11, "All eleven actions come from the ruleset folder");
    const int ac = world.sheet(0).armorClass(world.rules());
    check(world.send("use", {{"action", "defend"}}) && world.sheet(0).armorClass(world.rules()) == ac + 2
        && world.encounter()->order()[world.encounter()->currentIndex()].budget.actions == 1, "Defend costs one action and adds two AC");
    check(world.send("use", {{"action", "defend"}}) && world.sheet(0).armorClass(world.rules()) == ac + 2,
        "Repeated Defend refreshes rather than stacking");
    check(nextAna(world) && world.sheet(0).armorClass(world.rules()) == ac, "Defend ends when the defender's next turn starts");
    world.sheet(1).hp = 50;
    check(!world.send("use", {{"action", "help"}, {"target", 2}}), "Help refuses an enemy");
    check(world.send("use", {{"action", "help"}, {"target", 1}}) && world.sheet(1).hp == 50
        && world.sheet(1).attackAdvantage(world.rules()) == yh::Advantage::Advantage, "Help aids a standing ally without healing it");
    world.sheet(1).conditionEvent(world.rules(), "attack");
    check(world.sheet(1).attackAdvantage(world.rules()) == yh::Advantage::None, "The ally's next attack consumes Help");
    world.sheet(1).hp = 0;
    world.sheet(1).addCondition(world.rules(), "downed");
    world.tokens().tokens[1].floor = World::dead;
    check(world.send("use", {{"action", "help"}, {"target", 1}}) && world.sheet(1).hp == 1
        && !world.sheet(1).hasCondition("downed") && world.tokens().tokens[1].floor == 0, "Help gets a downed ally up with one HP in the same fight");
    check(nextAna(world), "The party can keep taking turns after Help");
    world.sheet(1).addCondition(world.rules(), "dead");
    check(!world.send("use", {{"action", "help"}, {"target", 1}}), "Help cannot revive the dead");
}

void hidingAndSeeking(const Check& check)
{
    WorldFixture world;
    check(loadFight(world), "The hiding yard loads");
    if (!world.fighting()) return;
    check(world.send("use", {{"action", "hide"}}) && world.sheet(0).hasCondition("hidden")
        && world.sheet(0).attackAdvantage(world.rules()) == yh::Advantage::Advantage, "Passing every enemy's perception hides the creature");
    check(world.send("step", {{"at", {2, 3}}}) && !world.sheet(0).hasCondition("hidden"), "Moving in combat ends Hide");
    world.tokens().tokens[0].position = world.grid().center({2, 3});
    world.tokens().tokens[0].path.clear();
    world.sheet(2).addCondition(world.rules(), "hidden");
    check(world.send("use", {{"action", "seek"}, {"target", 2}}) && !world.sheet(2).hasCondition("hidden"), "Seek reveals an enemy when Perception beats passive Stealth");
    check(nextAna(world), "The hiding yard advances a round");
    world.sheet(3).stats.setBase("wis", 4000);
    check(world.send("use", {{"action", "hide"}}) && !world.sheet(0).hasCondition("hidden"), "One watcher spotting the creature defeats Hide");
    world.sheet(2).stats.setBase("dex", 4000);
    world.sheet(2).addCondition(world.rules(), "hidden");
    check(world.send("use", {{"action", "seek"}, {"target", 2}}) && world.sheet(2).hasCondition("hidden"), "A failed Seek leaves the enemy hidden");
}

void positioningAndReadying(const Check& check)
{
    WorldFixture world;
    check(loadFight(world), "The positioning yard loads");
    if (!world.fighting()) return;
    check(world.send("use", {{"action", "shove"}, {"target", 2}}) && world.cellOf(2) == yh::Cell{5, 3}, "Shove pushes an adjacent enemy one square on success");
    check(!world.send("use", {{"action", "grapple"}, {"target", 2}}), "Grapple refuses a target out of reach");
    world.tokens().tokens[2].position = world.grid().center({4, 3});
    check(world.send("use", {{"action", "grapple"}, {"target", 2}}) && world.sheet(2).hasFlag(world.rules(), "cantMove"), "Grapple holds an adjacent enemy in place");
    check(nextAna(world), "The positioning yard advances a round");
    world.tokens().tokens[3].position = world.grid().center({5, 3});
    check(world.send("use", {{"action", "shove"}, {"target", 2}}) && world.cellOf(2) == yh::Cell{4, 3}, "Shove stops before an occupied square");
    world.sheet(0).addCondition(world.rules(), "prone");
    check(world.send("use", {{"action", "interact"}}) && !world.sheet(0).hasCondition("prone"), "Interact stands up from prone");
    check(nextAna(world), "The positioning yard refreshes the turn");
    check(!world.send("use", {{"action", "interact"}}), "Stand up is unavailable when already standing");
    world.sheet(0).addCondition(world.rules(), "grabbed");
    check(!world.send("step", {{"at", {2, 3}}}), "Being grabbed immediately prevents movement, including free movement");
    world.sheet(0).removeCondition("grabbed");
    check(world.send("use", {{"action", "ready"}}) && world.currentCreature() != 0 && world.creatures()[0].readiedAction == "strike",
        "Ready records a Strike, spends both actions and ends the turn");
    for (int i = 0; i < 8 && world.currentCreature() != 0; i++)
        world.send("use", {{"action", "end-turn"}});
    check(world.currentCreature() == 0 && world.creatures()[0].readiedAction.empty(), "An unused Ready expires at the creature's next turn");
}

}

void worldActionTests(const Check& check)
{
    defendingAndHelping(check);
    hidingAndSeeking(check);
    positioningAndReadying(check);
}
