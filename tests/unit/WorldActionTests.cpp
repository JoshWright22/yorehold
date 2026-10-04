#include "WorldFixture.h"

#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;

bool loadFight(WorldFixture& world, const std::map<std::string, std::string>& extra = {})
{
    const char* chapter = R"({"id":"actions-yard","title":"Yard","map":"map.json",
        "party":[{"name":"Ana","class":"fighter","at":[3,3]},{"name":"Bo","class":"cleric","at":[3,4]}],
        "encounters":[{"id":"yard","creatures":[{"creature":"goblin","name":"Gik","at":[4,3]},
            {"creature":"goblin","name":"Gok","at":[6,3]}]}]})";
    const char* map = R"({"name":"Yard","tiles":{"floor":{"art":"grass"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
        "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":[
            "########","#......#","#......#","#......#","#......#","#......#","#......#","########"]}]})";
    std::map<std::string, std::string> files{{"chapters/actions-yard/chapter.json", chapter}, {"chapters/actions-yard/map.json", map}};
    files.insert(extra.begin(), extra.end());
    if (!world.loadJson("chapters/actions-yard", files, 5))
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

bool hasReaction(const WorldFixture& world, size_t creature)
{
    const auto index = world.orderIndex(creature);
    return index && world.encounter()->order()[*index].budget.reaction;
}

void finishWalk(WorldFixture& world, size_t creature)
{
    yh::Token& token = world.tokens().tokens[creature];
    if (!token.path.empty()) token.position = token.path.back();
    token.path.clear();
}

bool turnTo(WorldFixture& world, size_t creature)
{
    for (int i = 0; i < 8 && world.currentCreature() != creature; i++)
        if (!world.send("use", {{"action", "end-turn"}})) return false;
    return world.currentCreature() == creature;
}

void movementReactions(const Check& check)
{
    WorldFixture world;
    check(loadFight(world), "The reaction yard loads");
    if (!world.fighting()) return;
    check(world.chapter()->reactions.size() == 2, "Opportunity and readied triggers come from reaction files");
    check(world.send("step", {{"at", {2, 3}}}) && !hasReaction(world, 2) && hasReaction(world, 3)
        && world.encounter()->order()[*world.orderIndex(0)].budget.actions == 2, "Leaving reach with free movement spends the foe's reaction, not a turn action");
    finishWalk(world, 0);
    check(world.send("step", {{"at", {3, 3}}}), "Returning toward the foe is allowed");
    finishWalk(world, 0);
    check(world.send("step", {{"at", {2, 3}}}), "The hero can leave again after the foe spent its reaction");
    int strikes = 0;
    for (const std::string& line : world.log)
        strikes += line == "Gik takes Opportunity Strike.";
    check(strikes == 1, "The foe cannot take a second opportunity before its next turn");
    finishWalk(world, 0);
    check(turnTo(world, 2) && hasReaction(world, 2), "The reaction refreshes at the start of the creature's turn");

    WorldFixture forced;
    check(loadFight(forced) && forced.send("use", {{"action", "shove"}, {"target", 2}})
        && hasReaction(forced, 0) && hasReaction(forced, 2), "Forced movement from Shove does not provoke reactions");

    WorldFixture ready;
    check(loadFight(ready), "The Ready yard loads");
    if (!ready.fighting()) return;
    ready.sheet(0).stats.setBase("str", 20);
    ready.tokens().tokens[2].position = ready.grid().center({5, 3});
    check(ready.send("use", {{"action", "ready"}}) && turnTo(ready, 2), "Ready waits through the other turns");
    check(ready.send("step", {{"at", {4, 3}}}) && !hasReaction(ready, 0) && ready.creatures()[0].readiedAction.empty()
        && ready.said("Ana takes Readied Strike."), "An enemy entering reach triggers the readied action once");

    WorldFixture lethal;
    const std::map<std::string, std::string> damage{{"rulesets/yorehold/actions/strike.json", R"({"id":"strike","order":10,"target":{"kind":"creature","side":"enemy","range":1},"effects":[{"do":"damage","dice":2000}]})"}};
    check(loadFight(lethal, damage), "The lethal reaction yard loads");
    if (!lethal.fighting()) return;
    check(lethal.send("step", {{"at", {2, 3}}}) && lethal.sheet(0).down() && lethal.tokens().tokens[0].path.empty()
        && lethal.cellOf(0) == yh::Cell{3, 3} && lethal.currentCreature() != 0, "A lethal opportunity stops the move before leaving and advances the turn");
}

void reactionPrompts(const Check& check)
{
    WorldFixture world;
    check(loadFight(world), "The prompt yard loads");
    if (!world.fighting()) return;
    world.setOptions({0, 0, true, true});
    world.sheet(0).stats.setBase("str", 20);
    world.tokens().tokens[1].position = world.grid().center({1, 6});
    check(turnTo(world, 2) && world.send("step", {{"at", {5, 3}}, {"prompts", false}}) && world.reactionPrompt()
        && world.reactionPrompt()->creature == 0, "The host's setting creates a prompt when an enemy leaves the hero's reach");
    if (!world.reactionPrompt()) return;
    const uint64_t offer = world.reactionPrompt()->id;
    check(!world.send("use", {{"action", "end-turn"}}), "Turns and other actions wait for the reaction decision");
    std::string reason;
    check(!world.validate(99, "reaction", nlohmann::json{{"take", true}, {"creature", 0}, {"offer", offer}}.dump(), reason),
        "Another player cannot decide the hero's reaction");
    check(world.send("reaction", {{"take", false}, {"creature", 0}, {"offer", offer}}) && !world.reactionPrompt()
        && hasReaction(world, 0) && world.cellOf(2) == yh::Cell{5, 3}, "Skip resumes movement and keeps the reaction budget");
    finishWalk(world, 2);
    world.tokens().tokens[2].position = world.grid().center({4, 3});
    check(world.send("step", {{"at", {5, 3}}}) && world.reactionPrompt(), "A new trigger can offer the preserved reaction");
    check(!world.send("reaction", {{"take", true}, {"creature", 0}, {"offer", offer}}), "An old decision cannot accept a later offer");
    world.step(2.1);
    check(!world.reactionPrompt() && !hasReaction(world, 0) && world.said("Ana takes Opportunity Strike."),
        "Without input the short prompt defaults to taking the reaction");

    WorldFixture both;
    check(loadFight(both), "The two-ally prompt yard loads");
    if (!both.fighting()) return;
    both.setOptions({0, 0, true, true});
    check(turnTo(both, 2) && both.send("step", {{"at", {5, 3}}}) && both.reactionPrompt(), "A move crossing two allies' reach offers the first reaction");
    if (!both.reactionPrompt()) return;
    const uint64_t first = both.reactionPrompt()->id;
    both.react(false);
    check(both.reactionPrompt() && both.reactionPrompt()->creature == 1 && both.reactionPrompt()->id != first,
        "Skipping one ally's reaction offers the next ally's reaction on the same edge");
    both.react(false);
    check(!both.reactionPrompt() && hasReaction(both, 0) && hasReaction(both, 1) && both.cellOf(2) == yh::Cell{5, 3},
        "Skipping both offers resumes the move with both budgets intact");
}

}

void worldActionTests(const Check& check)
{
    defendingAndHelping(check);
    hidingAndSeeking(check);
    positioningAndReadying(check);
    movementReactions(check);
    reactionPrompts(check);
}
