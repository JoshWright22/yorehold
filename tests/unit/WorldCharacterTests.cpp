#include "WorldFixture.h"

#include <functional>

namespace
{

std::map<std::string, std::string> yardFiles(int xpPerVictory)
{
    return {{"chapters/choice-yard/chapter.json", R"({"id":"choice-yard","title":"Yard","map":"map.json","xpPerVictory":)"
        + std::to_string(xpPerVictory) + R"(,
        "party":[{"name":"Ana","class":"fighter","at":[3,3]},{"name":"Bo","class":"cleric","at":[3,4]}],
        "encounters":[{"id":"yard","creatures":[{"creature":"goblin","name":"Gik","at":[4,3]}]}]})"},
        {"chapters/choice-yard/map.json", R"({"name":"Yard","tiles":{"floor":{"art":"grass"},
        "wall":{"art":"wall","walkable":false,"blocksSight":true}},"legend":{".":"floor","#":"wall"},
        "layers":[{"name":"ground","rows":["########","#......#","#......#","#......#",
        "#......#","#......#","#......#","########"]}]})"},
        {"rulesets/yorehold/actions/finish-test.json", R"({"id":"finish-test","cost":0,
        "effects":[{"do":"damage","dice":10000,"target":"enemies"}]})"}};
}

}

void worldCharacterTests(const std::function<void(bool, const char*)>& check)
{
    WorldFixture world;
    check(world.loadJson("chapters/choice-yard", yardFiles(50), 5), "The choice yard loads");
    if (!world.chapter()) return;
    const auto& rules = world.rules();
    const yh::CharacterChoices& ana = world.creatures()[0].choices;
    check(ana.name == "Ana" && ana.scoreMethod == "roll" && ana.level() == 1 && ana.levels[0].classId == "fighter"
        && ana.scores.at("con") == world.sheet(0).abilityScore("con") && world.creatures()[2].choices.levels.empty(),
        "Heroes start from rolled choices; creatures have none");

    // Saves carry the choices, and loading rebuilds the sheet from them around its live state.
    world.sheet(0).hp = 3;
    world.sheet(0).stats.setBase("maxHp", 500);
    world.sheet(0).addCondition(rules, "frightened", 2);
    const auto saved = nlohmann::json::parse(world.stateJson());
    check(saved.at("choices").size() == 2 && saved.at("choices")[1].at("levels")[0].at("class") == "cleric",
        "A save lists each hero's choices");
    check(world.restoreState(saved.dump()) && world.sheet(0).maxHp() < 500 && world.sheet(0).hp == 3
        && world.sheet(0).hasCondition("frightened"),
        "Loading rebuilds a hero's maximum HP from its class and keeps its wounds and conditions");
    const auto again = world.stateJson();
    check(world.restoreState(again) && world.stateJson() == again, "A rebuilt save round trips unchanged");

    auto changed = saved;
    changed["choices"][0]["scores"]["con"] = 20;
    changed["choices"][0]["levels"].push_back({{"class", "cleric"}});
    check(world.restoreState(changed.dump()) && world.sheet(0).level == 2 && world.sheet(0).abilityScore("con") == 20
        && world.sheet(0).characterClass == "Fighter / Cleric", "Changed choices change the sheet on the next load");

    // Saves from before choices: read off the sheet, in the class the chapter gave the hero.
    auto old = saved;
    old.erase("choices");
    old["creatures"][1]["sheet"]["level"] = 3;
    check(world.restoreState(old.dump()) && world.creatures()[1].choices.level() == 3
        && world.creatures()[1].choices.levels[2].classId == "cleric" && world.creatures()[1].choices.scoreMethod == "fixed"
        && world.sheet(1).abilityScore("wis") == old["creatures"][1]["sheet"]["stats"]["wis"].get<int>(),
        "An older save without choices still loads, taking them from its sheets");

    std::string error;
    const auto unchanged = world.stateJson();
    auto bad = saved;
    bad["choices"][1]["levels"][0]["class"] = "bard";
    check(!world.restoreState(bad.dump(), &error) && error.find("Bo") != std::string::npos && error.find("bard") != std::string::npos
        && world.stateJson() == unchanged, "A saved character with an unknown class fails, naming it, before anything is applied");
    bad = saved;
    bad["choices"].erase(1);
    check(!world.restoreState(bad.dump(), &error) && error == "saved characters don't match the party", "A save missing a hero's choices fails clearly");

    // Enough XP for a level: it goes into the hero's class, and the extra HP comes with it.
    WorldFixture levelling;
    check(levelling.loadJson("chapters/choice-yard", yardFiles(300), 5), "The levelling yard loads");
    if (!levelling.chapter()) return;
    const int maxHp = levelling.sheet(0).maxHp();
    levelling.sheet(0).hp = maxHp - 2;
    levelling.sheet(0).stats.setBase("dex", 2000); // acts first
    nlohmann::json positions = nlohmann::json::array();
    for (const auto& token : levelling.tokens().tokens) positions.push_back({token.position.x, token.position.y});
    check(levelling.send("fight", {{"group", 0}, {"at", positions}}) && levelling.currentCreature() == 0, "The levelling fight starts");
    check(levelling.send("use", {{"action", "finish-test"}}) && levelling.said("Ana reaches level 2."),
        "Winning with enough XP levels a hero up");
    const auto& grown = levelling.creatures()[0];
    check(grown.choices.level() == 2 && grown.choices.levels[1].classId == "fighter" && grown.sheet.level == 2
        && grown.sheet.maxHp() > maxHp && grown.sheet.hp >= maxHp - 2 + (grown.sheet.maxHp() - maxHp),
        "The new level is in the hero's class, with its HP added");
}
