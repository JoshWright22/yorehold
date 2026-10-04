#include "WorldFixture.h"

#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;

class DeathFixture : public WorldFixture
{
public:
    void checkpoint() { requestSave(); }
};

std::map<std::string, std::string> yardFiles(const nlohmann::json& wipe = nullptr)
{
    auto chapter = nlohmann::json::parse(R"({"id":"death-yard","title":"Yard","map":"map.json",
        "party":[{"name":"Ana","class":"fighter","at":[3,3]},{"name":"Bo","class":"cleric","at":[3,4]}],
        "encounters":[{"id":"yard","creatures":[{"creature":"goblin","name":"Gik","at":[4,3]}]}]})");
    if (!wipe.is_null()) chapter["onWipe"] = wipe;
    return {{"chapters/death-yard/chapter.json", chapter.dump()},
        {"chapters/death-yard/map.json", R"({"name":"Yard","tiles":{"floor":{"art":"grass"},
        "wall":{"art":"wall","walkable":false,"blocksSight":true}},"legend":{".":"floor","#":"wall"},
        "layers":[{"name":"ground","rows":["########","#......#","#......#","#......#",
        "#......#","#......#","#......#","########"]}]})"},
        {"chapters/death-yard/wipe.json", R"({"steps":[{"title":"Return","seconds":1}]})"},
        {"rulesets/yorehold/actions/finish-test.json", R"({"id":"finish-test","cost":0,
        "effects":[{"do":"damage","dice":10000,"target":"enemies"}]})"},
        {"rulesets/yorehold/actions/wipe-test.json", R"({"id":"wipe-test","cost":0,
        "effects":[{"do":"damage","dice":10000,"target":"allies"}]})"}};
}

bool load(DeathFixture& world, const nlohmann::json& wipe = nullptr, std::string* error = nullptr)
{
    if (!world.loadJson("chapters/death-yard", yardFiles(wipe), 5, error)) return false;
    for (size_t i = 0; i < world.creatures().size(); i++)
    {
        world.sheet(i).stats.setBase("maxHp", 1000); world.sheet(i).hp = 1000;
        world.sheet(i).stats.setBase("dex", i == 0 ? 2000.0f : i == 1 ? 1800.0f : 1000.0f);
    }
    world.checkpoint();
    return true;
}

bool fight(DeathFixture& world)
{
    nlohmann::json positions = nlohmann::json::array();
    for (const auto& token : world.tokens().tokens) positions.push_back({token.position.x, token.position.y});
    return world.send("fight", {{"group", 0}, {"at", positions}}) && world.currentCreature() == 0;
}

void recovering(const Check& check)
{
    DeathFixture world;
    check(load(world) && fight(world), "The death yard starts with Ana's shared turn");
    if (!world.fighting()) return;
    world.sheet(1).takeDamage(1000, world.rules());
    check(world.sheet(1).hasCondition("dying") && world.send("use", {{"action", "help"}, {"target", 1}})
        && world.sheet(1).hp == 1 && !world.sheet(1).hasCondition("dying") && !world.sheet(1).hasCondition("downed"),
        "Help gets a dying ally up and removes the fallen conditions");
    world.sheet(1).takeDamage(1, world.rules());
    world.sheet(1).death.successes = 3; world.sheet(1).death.stable = true; world.sheet(1).syncDeath(world.rules());
    // Sheets from before inventory consumables keep their remaining resource-backed uses.
    world.sheet(0).resources["potions"] = {1, 1};
    check(world.send("use", {{"action", "potion"}, {"target", 1}}) && world.sheet(1).hp >= 4 && world.sheet(1).hp <= 10
        && !world.sheet(1).death.stable && world.sheet(1).death.successes == 0
        && world.sheet(0).resources.at("potions").current == 0, "A potion heals a stable ally and spends one carried use");
    std::string reason;
    check(!world.findAction("potion")->meets(world.sheet(0), world.rules(), &reason) && reason.find("potions") != std::string::npos,
        "An empty potion resource prevents another use");
    world.sheet(1).takeDamage(1000, world.rules());
    const size_t logAt = world.log.size();
    for (int i = 0; i < 4 && world.log.size() < logAt + 4; i++) world.send("use", {{"action", "end-turn"}});
    check(world.said("Bo death save") && (world.sheet(1).hp > 0 || world.sheet(1).death.successes + world.sheet(1).death.failures > 0),
        "A downed hero still rolls a death save at its initiative block");
    world.sheet(1).hp = 0; world.sheet(1).death = {true};
    world.sheet(1).takeDamage(1, world.rules(), true); world.sheet(1).takeDamage(1, world.rules());
    check(world.sheet(1).death.dead && !world.validTarget(0, *world.findAction("help"), 1)
        && !world.validTarget(0, *world.findAction("potion"), 1), "Help and potions refuse a dead ally");
    world.sheet(1).heal(1000);
    check(world.sheet(1).hp == 0, "Ordinary healing cannot revive the dead");
    check(world.send("use", {{"action", "finish-test"}}) && world.said("Victory!")
        && world.sheet(1).death.dead && world.sheet(1).hp == 0, "Victory recovery leaves a dead ally dead");
}

void savedStates(const Check& check)
{
    DeathFixture world;
    check(load(world), "The save-state death yard loads");
    if (!world.chapter()) return;
    world.sheet(1).takeDamage(1000, world.rules()); world.sheet(1).death.successes = 1; world.sheet(1).death.failures = 2;
    const auto saved = world.stateJson();
    check(world.restoreState(saved) && world.sheet(1).death.successes == 1 && world.sheet(1).death.failures == 2,
        "Death counters survive saving and restoring");
    auto old = nlohmann::json::parse(saved);
    for (auto& creature : old["creatures"]) creature["sheet"].erase("death");
    check(world.restoreState(old.dump()) && world.sheet(1).death.saves && world.sheet(1).death.failures == 0
        && !world.sheet(2).death.saves, "Old sheets inherit death-save eligibility from their class or creature");
    auto bad = nlohmann::json::parse(saved); bad["creatures"][1]["sheet"]["death"]["failures"] = 3;
    const auto unchanged = world.stateJson();
    std::string error;
    check(!world.restoreState(bad.dump(), &error) && error.find("death counters") != std::string::npos
        && world.stateJson() == unchanged, "Invalid death counters fail without applying other state");
}

void wipes(const Check& check)
{
    DeathFixture left, right;
    check(load(left) && load(right), "Two copies load the wipe yard");
    if (!left.chapter() || !right.chapter()) return;
    left.sheet(0).hp = 9; right.sheet(0).hp = 9;
    left.sheet(0).resources["potions"].current = 0; right.sheet(0).resources["potions"].current = 0;
    left.checkpoint(); right.checkpoint();
    check(fight(left) && fight(right) && left.send("use", {{"action", "wipe-test"}})
        && right.send("use", {{"action", "wipe-test"}}) && left.wiping() && right.wiping(), "A party wipe waits for a return command");
    std::string reason;
    check(!left.validate(1, "wipe-return", "{}", reason), "A joining player cannot choose the wipe checkpoint");
    const auto accepted = left.validate(0, "wipe-return", R"({"checkpoint":"ignored"})", reason);
    check(accepted.has_value(), "The host returns the stored checkpoint instead of client-supplied data");
    if (!accepted) return;
    left.apply({0, 0, "wipe-return", *accepted}); right.apply({0, 0, "wipe-return", *accepted});
    check(!left.wiping() && !left.partyDown() && left.sheet(0).hp == 9 && left.sheet(0).resources.at("potions").current == 0
        && left.cellOf(0) == yh::Cell{3,3} && left.checksum() == right.checksum(),
        "Both copies restore the checkpoint's health, supplies and positions after a wipe");
    DeathFixture joined;
    check(load(joined) && joined.restoreState(left.snapshot()) && joined.checksum() == left.checksum(),
        "A joining snapshot carries the current checkpoint and mortality state");

    DeathFixture scripted;
    const nlohmann::json wipe{{"cutscene", "wipe.json"}, {"destination", {{1,1},{1,2}}}};
    check(load(scripted, wipe) && fight(scripted) && scripted.send("use", {{"action", "wipe-test"}}), "An onWipe cutscene starts");
    if (!scripted.chapter()) return;
    check(scripted.wiping() && !scripted.ended && !scripted.send("wipe-return"), "The wipe cutscene finishes before returning and is not chapter completion");
    scripted.endCutscene(); scripted.step(1.0 / 60);
    check(!scripted.wiping() && scripted.cellOf(0) == yh::Cell{1,1} && scripted.cellOf(1) == yh::Cell{1,2},
        "Finishing onWipe returns the saved party to its authored destination");
    DeathFixture invalid;
    std::string error;
    check(!load(invalid, {{"destination", {{0,0},{1,2}}}}, &error) && error.find("onWipe.destination") != std::string::npos,
        "A blocked wipe destination fails while loading with its field name");
}

}

void worldDeathTests(const Check& check)
{
    recovering(check);
    savedStates(check);
    wipes(check);
}
