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

// Containers, what the dead leave, taking and giving.
void lootTests(const std::function<void(bool, const char*)>& check)
{
    auto files = yardFiles(50);
    nlohmann::json chapter = nlohmann::json::parse(files.at("chapters/choice-yard/chapter.json"));
    chapter["containers"] = {{{"id", "box"}, {"name", "Box"}, {"at", {2, 2}}, {"items", {"mace"}}, {"coins", 30}}};
    files["chapters/choice-yard/chapter.json"] = chapter.dump();
    WorldFixture world;
    check(world.loadJson("chapters/choice-yard", files, 5), "The yard loads with a container");
    if (!world.chapter()) return;
    const auto& rules = world.rules();
    check(world.piles().size() == 1 && world.piles()[0].name == "Box" && world.piles()[0].coins == 30 && world.piles()[0].items.size() == 1
        && world.pileNear(0) == std::optional<size_t>(0) && !world.pileNear(1), "A container starts full; only a hero beside it can reach it");
    check(!world.send("loot", {{"hero", 1}, {"pile", 0}, {"all", true}}) && world.refusal.find("too far") != std::string::npos,
        "A hero further away is told so");
    check(world.send("loot", {{"hero", 0}, {"pile", 0}, {"coins", true}}) && world.sheet(0).coins == 30 && world.piles()[0].coins == 0
        && world.said("Ana takes 3 sp (Box)."), "Coins are taken by themselves");
    check(world.send("loot", {{"hero", 0}, {"pile", 0}, {"item", 0}}) && world.sheet(0).inventory.back().id == "mace"
        && !world.sheet(0).inventory.back().equipped && world.piles()[0].empty() && !world.pileNear(0) && !world.send("loot", {{"hero", 0}, {"pile", 0}, {"all", true}}),
        "Items go into the hero's pack, and an empty container has nothing more");

    // Giving: items and coins change hands; a worn item comes off, and the rest stays as it was.
    check(world.send("give", {{"from", 0}, {"to", 1}, {"item", world.sheet(0).inventory.size() - 1}}) && world.sheet(1).inventory.back().id == "mace"
        && world.said("Ana gives Mace to Bo."), "An item can be handed to an ally");
    check(world.send("give", {{"from", 0}, {"to", 1}, {"coins", 20}}) && world.sheet(0).coins == 10 && world.sheet(1).coins == 20
        && !world.send("give", {{"from", 0}, {"to", 1}, {"coins", 11}}) && !world.send("give", {{"from", 0}, {"to", 0}, {"coins", 1}}),
        "Coins can be handed over, but not more than there are");
    const int ac = world.sheet(0).armorClass(rules);
    const std::string first = world.sheet(0).inventory[0].id;
    check(world.sheet(0).inventory[0].equipped && world.send("give", {{"from", 0}, {"to", 1}, {"item", 0}}) && world.sheet(1).inventory.back().id == first
        && !world.sheet(1).inventory.back().equipped && world.sheet(0).armorClass(rules) == ac && !world.sheet(0).weapon(),
        "Giving a held weapon away leaves armour and shield as they were");
    const auto& left = world.sheet(0).inventory;
    const size_t shield = static_cast<size_t>(std::find_if(left.begin(), left.end(), [](const yh::Item& i) { return i.id == "shield"; }) - left.begin());
    check(shield < left.size() && world.send("equip", {{"hero", 0}, {"item", shield}, {"on", false}}) && world.sheet(0).armorClass(rules) < ac,
        "What is still worn can still be put away");

    // The dead leave what they carried and what their loot table gives, where they fell.
    world.sheet(1).stats.setBase("dex", 2000); // Bo acts first
    nlohmann::json places = nlohmann::json::array();
    for (const auto& token : world.tokens().tokens) places.push_back({token.position.x, token.position.y});
    check(world.send("fight", {{"group", 0}, {"at", places}}) && world.currentCreature() == 1 && world.send("use", {{"action", "finish-test"}}),
        "The goblin is beaten");
    check(world.piles().size() == 2 && world.piles()[1].name == "Gik" && world.piles()[1].at == world.cellOf(2) && world.piles()[1].container == -1
        && world.piles()[1].coins >= 2 && world.piles()[1].coins <= 12 && world.piles()[1].items.size() == 1 && world.piles()[1].items[0].id == "scimitar"
        && !world.piles()[1].items[0].equipped, "A dead goblin leaves its scimitar and some coins");
    const int found = world.piles()[1].coins;
    const auto saved = nlohmann::json::parse(world.stateJson());
    check(world.restoreState(saved.dump()) && world.piles().size() == 2 && world.piles()[1].coins == found && world.piles()[0].empty()
        && world.piles()[1].items.size() == 1 && world.sheet(1).coins == 20, "Saves keep what lies around and what everyone carries");
    const std::string again = world.stateJson();
    check(world.restoreState(again) && world.stateJson() == again, "A save with loot round trips unchanged");
    auto older = saved;
    older.erase("piles");
    for (auto& creature : older["creatures"]) creature.erase("dropped");
    check(world.restoreState(older.dump()) && world.piles().size() == 1 && world.piles()[0].coins == 30,
        "A save from before loot starts the containers full and leaves the dead as they were");
}

}

void worldCharacterTests(const std::function<void(bool, const char*)>& check)
{
    lootTests(check);
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

    // A character brought from the library takes a seat; the other seats and the dice stay as they were.
    WorldFixture brought;
    check(brought.loadJson("chapters/choice-yard", yardFiles(50), 5), "The yard loads for a brought character");
    if (!brought.chapter()) return;
    const std::string bo = brought.sheet(1).toJson(), gik = brought.sheet(2).toJson();
    yh::Random dice(9);
    World::PartyPick ada{yh::rollChoices(brought.rules(), "Ada", "rogue", dice), {}, "ada.json"};
    yh::Item rope;
    rope.id = "rope";
    rope.name = "Rope";
    ada.inventory.push_back(rope);
    yh::Item blade = *brought.chapter()->compendium.item("shortsword");
    blade.equipped = true;
    ada.inventory.push_back(blade);
    brought.setParty({ada, std::nullopt});
    brought.newAdventure(5);
    const auto& seated = brought.creatures()[0];
    check(seated.sheet.name == "Ada" && seated.library == "ada.json" && seated.choices.levels[0].classId == "rogue"
        && brought.tokens().tokens[0].name == "Ada" && seated.sheet.inventory.size() == 2 && seated.sheet.weapon()
        && seated.sheet.weapon()->id == "shortsword", "A brought character takes the seat with what it carries, worn as it was");
    check(brought.sheet(1).toJson() == bo && brought.sheet(2).toJson() == gik, "The other seats and the dice are unchanged");
    const auto withAda = nlohmann::json::parse(brought.stateJson());
    check(withAda.at("library") == nlohmann::json::array({"ada.json", ""}), "The save names the brought character's library file");
    brought.setParty({});
    brought.newAdventure(5);
    check(brought.restoreState(withAda.dump()) && brought.creatures()[0].library == "ada.json" && brought.tokens().tokens[0].name == "Ada",
        "Loading the save puts the brought character back in its seat");
    ada.choices.levels[0].classId = "bard";
    brought.setParty({ada});
    brought.newAdventure(5);
    check(brought.sheet(0).name == "Ana" && brought.creatures()[0].library.empty(), "A character the chapter can't build leaves the seat to its own hero");

    // Gear: free to change between fights, an Interact on the hero's own turn in one.
    WorldFixture gear;
    check(gear.loadJson("chapters/choice-yard", yardFiles(50), 5), "The yard loads for changing gear");
    if (!gear.chapter()) return;
    auto find = [&](const char* id) {
        const auto& inventory = gear.sheet(0).inventory;
        return static_cast<size_t>(std::find_if(inventory.begin(), inventory.end(), [&](const yh::Item& i) { return i.id == id; }) - inventory.begin());
    };
    const size_t shield = find("shield");
    const int ac = gear.sheet(0).armorClass(gear.rules());
    check(shield < gear.sheet(0).inventory.size() && gear.send("equip", {{"hero", 0}, {"item", shield}, {"on", false}})
        && gear.sheet(0).armorClass(gear.rules()) < ac && gear.said("Ana puts away Shield."), "A shield can be put away between fights");
    check(!gear.send("equip", {{"hero", 0}, {"item", shield}, {"on", false}}) && !gear.send("equip", {{"hero", 0}, {"item", 99}, {"on", true}}),
        "Nothing happens for an item already away or not there");
    check(gear.send("equip", {{"hero", 0}, {"item", shield}, {"on", true}}) && gear.sheet(0).armorClass(gear.rules()) == ac, "And taken up again");
    gear.sheet(0).inventory.push_back(*gear.chapter()->compendium.item("greataxe"));
    check(gear.send("equip", {{"hero", 0}, {"item", find("greataxe")}, {"on", true}}) && gear.sheet(0).weapon()->id == "greataxe"
        && !gear.sheet(0).inventory[shield].equipped && gear.sheet(0).handsInUse() == 2 && gear.said("Ana takes up Greataxe, putting away Longsword, Shield."),
        "A two-handed weapon takes both hands: the sword and shield are put away");
    gear.sheet(0).stats.setBase("dex", 2000); // acts first
    nlohmann::json places = nlohmann::json::array();
    for (const auto& token : gear.tokens().tokens) places.push_back({token.position.x, token.position.y});
    check(gear.send("fight", {{"group", 0}, {"at", places}}) && gear.currentCreature() == 0, "The gear fight starts on Ana's turn");
    auto actionsLeft = [&] { return gear.encounter()->order()[gear.encounter()->currentIndex()].budget.actions; };
    const int actions = actionsLeft();
    check(!gear.send("equip", {{"hero", 1}, {"item", 0}, {"on", false}}), "Gear can't be changed on someone else's turn");
    check(gear.send("equip", {{"hero", 0}, {"item", find("longsword")}, {"on", true}}) && actionsLeft() == actions - 1
        && gear.sheet(0).weapon()->id == "longsword", "In a fight, changing gear costs an action");
}
