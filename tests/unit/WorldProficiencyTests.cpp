#include "WorldFixture.h"

#include <functional>

namespace
{

std::map<std::string, std::string> yardFiles()
{
    return {{"chapters/rank-yard/chapter.json", R"({"id":"rank-yard","title":"Yard","map":"map.json",
        "party":[{"name":"Ana","class":"fighter","at":[3,3]},{"name":"Bo","class":"cleric","at":[3,4]}],
        "encounters":[{"id":"yard","creatures":[{"creature":"goblin","name":"Gik","at":[4,3]}]}]})"},
        {"chapters/rank-yard/map.json", R"({"name":"Yard","tiles":{"floor":{"art":"grass"},
        "wall":{"art":"wall","walkable":false,"blocksSight":true}},"legend":{".":"floor","#":"wall"},
        "layers":[{"name":"ground","rows":["########","#......#","#......#","#......#",
        "#......#","#......#","#......#","########"]}]})"},
        {"rulesets/yorehold/actions/dc-test.json", R"({"id":"dc-test","cost":0,
        "target":{"kind":"creature","side":"enemy","range":1},"effects":[
        {"do":"roll","kind":"save","ability":"dex","dc":"caster","steps":[]}]})"}};
}

}

void worldProficiencyTests(const std::function<void(bool, const char*)>& check)
{
    WorldFixture world;
    check(world.loadJson("chapters/rank-yard", yardFiles(), 5), "The ranked yard loads");
    if (!world.chapter()) return;
    const auto& rules = world.rules();
    auto& ana = world.sheet(0);
    check(ana.proficiencyRank(rules, "str") == "trained" && ana.proficiencyRank(rules, "dex") == "untrained"
        && world.sheet(1).dcAbility == "wis" && ana.proficiencyModifier(rules, "armor") == 3,
        "Class files choose saving throws, armour training and the DC ability");
    ana.stats.setBase("str", 18);
    check(ana.attackModifier(rules) == 7 && ana.saveModifier(rules, "str") == 7
        && ana.checkModifier(rules, "athletics") == 7 && ana.difficultyClass(rules) == 17,
        "Level-one trained attacks, saves, skills and DCs add level plus two");
    check(world.sheet(2).armorClass(rules) == 13, "A creature's written AC is already final");
    const uint64_t before = world.checksum();
    ana.level = 7; ana.dcAbility = "wis"; ana.stats.setBase("wis", 20);
    ana.proficiencyRanks["dc"] = "expert";
    check(world.checksum() != before && ana.difficultyClass(rules) == 26, "Explicit ranks and level affect both DC and the replicated checksum");

    const auto saved = world.stateJson();
    ana.proficiencyRanks["dc"] = "legendary";
    check(world.restoreState(saved) && world.sheet(0).proficiencyRank(world.rules(), "dc") == "expert"
        && world.sheet(0).difficultyClass(world.rules()) == 26, "A saved sheet restores rank choices and DC ability");
    auto bad = nlohmann::json::parse(saved);
    bad["creatures"][0]["sheet"]["hp"] = 1;
    bad["creatures"][2]["sheet"]["proficiencyRanks"]["weapons"] = "missing";
    std::string error;
    const auto unchanged = world.stateJson();
    check(!world.restoreState(bad.dump(), &error) && error.find("missing") != std::string::npos
        && world.stateJson() == unchanged, "An unknown saved rank is rejected before applying any state");
    auto old = nlohmann::json::parse(saved);
    for (auto& creature : old["creatures"])
    {
        creature["sheet"].erase("proficiencyRanks"); creature["sheet"].erase("dcAbility");
    }
    check(world.restoreState(old.dump()) && world.sheet(0).proficiencyRanks.empty()
        && world.sheet(0).proficiencyModifier(world.rules(), "weapons") == 9
        && world.sheet(0).proficiencyModifier(world.rules(), "dc") == 0,
        "Sheets without ranks still load, using trained for their legacy proficiency list");
    check(world.restoreState(saved), "The saved expert sheet can be restored again");
    world.sheet(0).stats.setBase("dex", 2000);
    nlohmann::json positions = nlohmann::json::array();
    for (const auto& token : world.tokens().tokens) positions.push_back({token.position.x, token.position.y});
    check(world.send("fight", {{"group", 0}, {"at", positions}}) && world.currentCreature() == 0,
        "The ranked yard starts with the DC test's caster");
    check(world.send("use", {{"action", "dc-test"}, {"target", 2}}) && world.said("dex, DC 26"),
        "A caster DC in an action uses the acting sheet's ability, level and rank");
    world.sheet(0).proficiencyRanks["dc"] = "untrained";
    check(world.send("use", {{"action", "dc-test"}, {"target", 2}}) && world.said("dex, DC 15"),
        "Changing the caster to untrained removes the level bonus on its next action");

    WorldFixture invalidClass, invalidCreature;
    auto files = yardFiles();
    files["chapters/rank-yard/classes/fighter.json"] = R"({"id":"fighter","proficiencyRanks":{"armor":"missing"}})";
    check(!invalidClass.loadJson("chapters/rank-yard", files, 1, &error)
        && error.find("chapters/rank-yard/classes/fighter.json") != std::string::npos && error.find("missing") != std::string::npos,
        "An invalid class rank names the overridden file and missing rank");
    files = yardFiles();
    files["chapters/rank-yard/creatures/goblin.json"] = R"({"id":"goblin","dcAbility":"missing"})";
    check(!invalidCreature.loadJson("chapters/rank-yard", files, 1, &error)
        && error.find("creatures/goblin.json") != std::string::npos && error.find("DC ability") != std::string::npos,
        "An invalid creature DC ability names its file and reason");
}
