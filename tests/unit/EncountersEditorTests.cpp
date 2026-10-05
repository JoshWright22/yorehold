// Encounters mode of the Create screen: its commands, undo, what it writes to chapter.json, and
// the game giving a group's own XP and loot.

#include "WorldFixture.h"
#include "screens/CreateScreen.h"
#include "screens/EncountersEditor.h"

#include <yorehold/framework/save/SaveFile.h>

#include <nlohmann/json.hpp>

#include <filesystem>
#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;
using nlohmann::json;

// A chapter written by hand, with fields the editor has no tool for.
const char* keepJson = R"({
  "id": "keep",
  "title": "Keep",
  "map": "map.json",
  "xpPerVictory": 50,
  "party": [{"name": "Ana", "class": "fighter", "at": [1, 1]}],
  "npcs": [{"id": "wren", "name": "Wren", "at": [2, 1], "dialogue": "dialogue/wren.json"}],
  "containers": [{"id": "box", "at": [3, 1]}],
  "encounters": [
    {"id": "hall", "set": ["hall_clear"], "text": "Goblins!", "surrender": "dialogue/yield.json", "creatures": [
      {"creature": "goblin", "name": "Gob", "facing": 180, "at": [5, 1]},
      {"creature": "goblin", "at": [5, 2], "ai": "lookout", "surrender": "dialogue/gob.json"},
      {"creature": "goblin-boss", "name": "Grak", "at": [6, 3], "ai": {"fleeHp": 0.6}}
    ]},
    {"id": "cellar", "creatures": [{"creature": "goblin", "at": [1, 4]}]}
  ],
  "aiChanges": [{"when": ["hall_clear"], "encounter": "cellar", "ai": "brute"}],
  "victoryText": "Won {xp}"
})";

EncountersEditor::Catalog testCatalog()
{
    EncountersEditor::Catalog catalog;
    catalog.creatures["goblin"] = {"Goblin", 1, {120, 150, 60, 255}, 0.36f};
    catalog.creatures["goblin-boss"] = {"Goblin boss", 3, {150, 90, 60, 255}, 0.42f};
    catalog.ai = {"brute", "coward", "lookout"};
    catalog.items = {{"mace", "Mace"}, {"shield", "Shield"}};
    return catalog;
}

// An 8 x 6 room with a pillar at 4, 4.
bool room(yh::Cell cell)
{
    return cell.x >= 0 && cell.y >= 0 && cell.x < 8 && cell.y < 6 && !(cell == yh::Cell{4, 4});
}

void loadAndSave(const Check& check)
{
    yh::History history;
    EncountersEditor editor(history);
    std::string error;
    check(!editor.load("{not json", &error) && !editor.loaded() && !error.empty(), "A broken chapter is refused with a reason");
    check(editor.load(keepJson, &error, testCatalog(), room) && editor.loaded(), "A hand-written chapter loads into the editor");
    check(editor.groups().size() == 2 && editor.groups()[0].id == "hall" && editor.groups()[0].creatures.size() == 3
        && editor.groups()[0].text == "Goblins!" && editor.groups()[0].set == std::vector<std::string>{"hall_clear"}, "It has the groups of the file");
    const EncountersEditor::Placement& gob = editor.groups()[0].creatures[0];
    check(gob.creature == "goblin" && gob.name == "Gob" && gob.at == yh::Cell{5, 1} && gob.facing == 180.0f, "Creatures are read with their names, cells and facing");
    check(EncountersEditor::profileOf(editor.groups()[0].creatures[1].ai) == "lookout" && EncountersEditor::profileOf(editor.groups()[0].creatures[2].ai) == "custom"
        && EncountersEditor::profileOf(gob.ai).empty(), "An AI entry is a profile's name, changes of its own, or nothing");
    check(editor.fixed().size() == 3 && editor.fixed()[0].name == "Ana" && editor.fixed()[2].kind == EncountersEditor::Fixed::Kind::Chest,
        "Heroes, NPCs and chests are known, to keep clear of");
    check(editor.chapterXp() == 50 && editor.xpOf(0) == 50, "A group without XP of its own gives the chapter's");
    check(history.size() == 0 && !history.dirty() && editor.problems().empty(), "Loading is not an edit, and the chapter has nothing wrong");

    const nlohmann::ordered_json inOrder = nlohmann::ordered_json::parse(editor.toJson());
    const json saved = json::parse(editor.toJson());
    check(inOrder.begin().key() == "id" && saved.value("title", "") == "Keep" && saved.at("party").size() == 1 && saved.at("npcs").size() == 1
        && saved.value("victoryText", "") == "Won {xp}", "The rest of the file is written back as it was, in its order");
    const json& hall = saved.at("encounters")[0];
    check(hall.value("surrender", "") == "dialogue/yield.json" && hall.at("creatures")[1].value("surrender", "") == "dialogue/gob.json"
        && hall.at("creatures")[2].at("ai").value("fleeHp", 0.0) == 0.6, "Fields the editor has no tool for are kept");
    check(hall.at("creatures")[0].at("at") == json({5, 1}) && hall.at("creatures")[0].at("facing") == 180 && !hall.at("creatures")[1].contains("facing")
        && !hall.at("creatures")[1].contains("name") && hall.at("set") == json({"hall_clear"}), "Creatures are written as the game reads them");
    check(saved.at("aiChanges")[0].value("encounter", "") == "cellar", "Story changes to AI are kept");

    EncountersEditor twice(history);
    check(!twice.load(R"({"encounters":[{"id":"a","creatures":[]},{"id":"a","creatures":[]}]})", &error) && !error.empty(), "Two groups with one id are refused");
    EncountersEditor bare(history);
    check(bare.load(R"({"id":"empty","party":[]})") && bare.groups().empty() && bare.chapterXp() == 0, "A chapter with no encounters loads");
    check(!json::parse(bare.toJson()).contains("xpPerVictory") && json::parse(bare.toJson()).at("encounters").empty(), "... and saves with none");
}

void creatures(const Check& check)
{
    yh::History history;
    EncountersEditor editor(history);
    editor.load(keepJson, nullptr, testCatalog(), room);

    const std::optional<size_t> rat = editor.addCreature(1, "goblin", {2, 4});
    check(rat && *rat == 1 && editor.groups()[1].creatures.size() == 2 && editor.creatureAt({2, 4}) == std::pair<size_t, size_t>{1, 1}, "A creature is placed in a group");
    check(!editor.addCreature(1, "dragon", {3, 4}) && !editor.addCreature(5, "goblin", {3, 4}), "Unknown creatures and groups are refused");
    check(!editor.addCreature(1, "goblin", {1, 1}) && !editor.addCreature(1, "goblin", {2, 1}) && !editor.addCreature(1, "goblin", {3, 1})
        && !editor.addCreature(1, "goblin", {5, 1}), "Not on a hero, an NPC, a chest or another creature");
    check(!editor.addCreature(1, "goblin", {4, 4}) && !editor.addCreature(1, "goblin", {8, 0}) && !editor.free({4, 4}) && editor.free({3, 4}),
        "Not where nobody can stand");
    check(history.size() == 1 && history.undo() && editor.groups()[1].creatures.size() == 1 && !editor.creatureAt({2, 4}), "Undo takes the creature away");
    check(history.redo() && editor.groups()[1].creatures[1].at == yh::Cell{2, 4}, "Redo puts it back");

    check(editor.moveCreature(1, 1, {3, 4}) && editor.groups()[1].creatures[1].at == yh::Cell{3, 4}, "A creature can be moved");
    check(!editor.moveCreature(1, 1, {3, 4}) && !editor.moveCreature(1, 1, {1, 4}) && !editor.moveCreature(1, 1, {4, 4}) && !editor.moveCreature(1, 9, {6, 5}),
        "... but not onto its own cell, someone else or a pillar");
    check(history.undo() && editor.groups()[1].creatures[1].at == yh::Cell{2, 4}, "Undo moves it back");

    for (const char* typed : {"R", "Ra", "Rat"})
        editor.setCreatureName(1, 1, typed);
    editor.endTyping();
    check(editor.groups()[1].creatures[1].name == "Rat" && editor.setCreatureName(1, 1, "Rats"), "A creature can be named");
    editor.endTyping();
    check(history.undo() && editor.groups()[1].creatures[1].name == "Rat", "Typing again later is an undo step of its own");
    check(history.undo() && editor.groups()[1].creatures[1].name.empty(), "Typing a name is one undo step");

    check(editor.setFacing(0, 0, 90.0f) && editor.facingOf(editor.groups()[0].creatures[0]) == 90, "A creature can be turned");
    check(!editor.setFacing(0, 0, 90.0f) && !editor.setFacing(0, 0, 400.0f), "The same way again or more than a full turn is refused");
    check(editor.setFacing(0, 0, std::nullopt) && !editor.groups()[0].creatures[0].facing && editor.facingOf(editor.groups()[0].creatures[0]) == 180,
        "With no facing it looks toward where the party starts");
    check(!json::parse(editor.toJson()).at("encounters")[0].at("creatures")[0].contains("facing"), "... and none is written");
    check(history.undo() && history.undo() && editor.groups()[0].creatures[0].facing == 180.0f, "Undo turns it back");

    check(editor.setCreatureAi(0, 0, "coward") && EncountersEditor::profileOf(editor.groups()[0].creatures[0].ai) == "coward", "A creature can be given an AI profile");
    check(json::parse(editor.toJson()).at("encounters")[0].at("creatures")[0].at("ai") == "coward", "It is written as the profile's name");
    check(!editor.setCreatureAi(0, 0, "coward") && !editor.setCreatureAi(0, 0, "genius"), "The same profile again or one the package lacks is refused");
    check(editor.setCreatureAi(0, 2, "") && editor.groups()[0].creatures[2].ai.empty(), "Picking none clears changes written by hand");
    check(history.undo() && EncountersEditor::profileOf(editor.groups()[0].creatures[2].ai) == "custom", "Undo brings them back");

    const std::optional<size_t> moved = editor.moveToGroup(0, 2, 1);
    check(moved && editor.groups()[0].creatures.size() == 2 && editor.groups()[1].creatures[*moved].name == "Grak", "A creature can change group and keeps its place on the map");
    check(!editor.moveToGroup(1, 0, 1) && !editor.moveToGroup(1, 0, 7), "Its own group or one that isn't there is refused");
    check(history.undo() && editor.groups()[0].creatures.size() == 3 && editor.groups()[0].creatures[2].name == "Grak", "Undo puts it back where it was in the list");

    check(editor.removeCreature(0, 1) && editor.groups()[0].creatures.size() == 2 && editor.groups()[0].creatures[1].name == "Grak" && !editor.removeCreature(0, 5),
        "A creature can be removed");
    check(history.undo() && editor.groups()[0].creatures.size() == 3 && EncountersEditor::profileOf(editor.groups()[0].creatures[1].ai) == "lookout",
        "Undo brings it back as it was");
}

void groups(const Check& check)
{
    yh::History history;
    EncountersEditor editor(history);
    editor.load(keepJson, nullptr, testCatalog(), room);

    const std::optional<size_t> added = editor.addGroup();
    check(added && *added == 2 && editor.groups()[2].id == "encounter-3" && editor.groups()[2].creatures.empty(), "A new group gets an id of its own");
    check(!editor.addGroup("hall") && editor.addGroup("yard") == std::optional<size_t>(3), "A group can be named, but not like another");
    const std::vector<EncountersEditor::Problem> empty = editor.problems();
    check(empty.size() == 2 && !empty[0].error, "A group with nobody in it is pointed out, as a warning");
    check(json::parse(editor.toJson()).at("encounters").size() == 2, "... and left out of the file, which the game would refuse");
    history.undo();
    check(editor.addCreature(2, "goblin", {6, 5}) && json::parse(editor.toJson()).at("encounters").size() == 3 && editor.problems().empty(),
        "With a creature in it the group is saved");

    check(editor.setGroupId(1, "vault") && editor.groups()[1].id == "vault", "A group can be renamed");
    check(json::parse(editor.toJson()).at("aiChanges")[0].value("encounter", "") == "vault", "Story changes that named it follow the new id");
    check(!editor.setGroupId(1, "hall") && !editor.setGroupId(1, "") && !editor.setGroupId(1, std::string(65, 'a')), "Taken, empty and overlong ids are refused");
    editor.endTyping();
    check(history.undo() && editor.groups()[1].id == "cellar" && json::parse(editor.toJson()).at("aiChanges")[0].value("encounter", "") == "cellar",
        "Undo gives it and the story changes the old id back");

    check(editor.setGroupText(0, "Goblins ahead!") && editor.setGroupFlags(0, {"hall_clear", "alarm"}) && editor.setGroupAi(0, "brute"),
        "A group's line, flags and AI can be changed");
    const json hall = json::parse(editor.toJson()).at("encounters")[0];
    check(hall.value("text", "") == "Goblins ahead!" && hall.at("set") == json({"hall_clear", "alarm"}) && hall.at("ai") == "brute", "They are written to the file");
    check(!editor.setGroupFlags(0, {"ok", ""}) && !editor.setGroupAi(0, "genius") && !editor.setGroupText(0, "Goblins ahead!"), "Blank flags, unknown AI and no change are refused");
    check(editor.setGroupText(0, "") && !json::parse(editor.toJson()).at("encounters")[0].contains("text"), "An empty line is not written");

    const size_t steps = history.size();
    check(editor.removeGroup(1) && editor.groups().size() == 2 && editor.groups()[1].id == "encounter-3" && !editor.removeGroup(9), "A group can be removed");
    check(!json::parse(editor.toJson()).contains("aiChanges"), "Story changes that named it go with it");
    check(history.size() == steps + 1 && history.undo() && editor.groups().size() == 3 && editor.groups()[1].id == "cellar"
        && json::parse(editor.toJson()).at("aiChanges").size() == 1, "Undo brings back the group, its creatures and the story changes");
}

void xpAndLoot(const Check& check)
{
    yh::History history;
    EncountersEditor editor(history);
    editor.load(keepJson, nullptr, testCatalog(), room);

    check(editor.proposedXp(0) == 125 && editor.proposedXp(1) == 25, "XP is proposed from the levels of the creatures in a group");
    check(editor.setGroupXp(0, editor.proposedXp(0)) && editor.xpOf(0) == 125 && editor.xpOf(1) == 50, "A group can give XP of its own");
    check(editor.setChapterXp(80) && editor.xpOf(1) == 80 && editor.xpOf(0) == 125, "The chapter's XP is for the groups without");
    check(!editor.setGroupXp(0, -1) && !editor.setGroupXp(0, 125) && !editor.setChapterXp(-5) && !editor.setChapterXp(80), "Negative or unchanged XP is refused");
    json saved = json::parse(editor.toJson());
    check(saved.at("encounters")[0].at("xp") == 125 && !saved.at("encounters")[1].contains("xp") && saved.at("xpPerVictory") == 80, "Both are written to the file");
    check(editor.setGroupXp(0, std::nullopt) && !json::parse(editor.toJson()).at("encounters")[0].contains("xp") && editor.xpOf(0) == 80,
        "Clearing a group's XP goes back to the chapter's");
    editor.endTyping();
    check(history.undo() && editor.xpOf(0) == 125, "Undo brings its own XP back");

    yh::LootTable loot;
    loot.coins = "2d6";
    loot.items.push_back({"mace", 0.5f, 2});
    check(editor.setGroupLoot(1, loot) && editor.groups()[1].loot.items.size() == 1, "A group can leave loot");
    saved = json::parse(editor.toJson()).at("encounters")[1].at("loot");
    check(saved.at("coins") == "2d6" && saved.at("items")[0].at("item") == "mace" && saved.at("items")[0].at("chance") == 0.5 && saved.at("items")[0].at("quantity") == 2,
        "It is written as a loot table");
    check(!editor.setGroupLoot(1, loot), "The same loot again is not an edit");
    yh::LootTable wrong = loot;
    wrong.coins = "lots";
    check(!editor.setGroupLoot(1, wrong), "Coins that aren't dice are refused");
    wrong = loot;
    wrong.items.push_back({"crown", 1, 1});
    check(!editor.setGroupLoot(1, wrong), "... and so is an item the package lacks");
    wrong = loot;
    wrong.items[0].chance = 2;
    check(!editor.setGroupLoot(1, wrong) && editor.groups()[1].loot.items[0].chance == 0.5f, "... and a chance over 1");
    editor.endTyping();
    check(history.undo() && editor.groups()[1].loot.empty() && !json::parse(editor.toJson()).at("encounters")[1].contains("loot"), "Undo takes the loot away");
}

void problems(const Check& check)
{
    yh::History history;
    EncountersEditor editor(history);
    // The map changes under the creatures when a wall is painted in map mode.
    bool walled = false;
    editor.load(keepJson, nullptr, testCatalog(), [&](yh::Cell cell) { return room(cell) && !(walled && cell == yh::Cell{5, 1}); });
    check(editor.problems().empty(), "The chapter as written has no problems");
    walled = true;
    const std::vector<EncountersEditor::Problem> stuck = editor.problems();
    check(stuck.size() == 1 && stuck[0].error && stuck[0].text.find("Gob (hall)") != std::string::npos, "A creature walled in is pointed out by name and group");

    EncountersEditor strange(history);
    check(strange.load(R"({"id":"odd","party":[{"name":"Ana","class":"fighter","at":[1,1]}],"encounters":[{"id":"den","ai":"genius","loot":{"items":["crown"]},"creatures":[
        {"creature":"dragon","at":[1,1]},{"creature":"goblin","at":[2,2]},{"creature":"goblin","at":[2,2],"ai":"sly"}]}]})", nullptr, testCatalog(), room),
        "A chapter with things wrong still opens");
    const std::vector<EncountersEditor::Problem> found = strange.problems();
    auto says = [&](std::string_view words) {
        return std::any_of(found.begin(), found.end(), [&](const EncountersEditor::Problem& p) { return p.error && p.text.find(words) != std::string::npos; });
    };
    check(found.size() == 6 && says("unknown creature dragon") && says("profile genius") && says("profile sly") && says("unknown item crown"),
        "Unknown creatures, AI profiles and items are listed");
    check(says("dragon (den) shares a cell") && says("goblin (den) shares a cell"), "... and so is everyone on a taken cell");
}

// What the editor saves plays: the game loads it, and a group's XP and loot are what a win gives.
void inTheGame(const Check& check)
{
    std::map<std::string, std::string> files{
        {"chapters/yard/chapter.json", R"({"id":"yard","title":"Yard","map":"map.json","xpPerVictory":10,"victoryText":"Won {xp}",
            "party":[{"name":"Ana","class":"fighter","at":[3,3]},{"name":"Bo","class":"cleric","at":[3,4]}],
            "encounters":[{"id":"gate","creatures":[{"creature":"goblin","name":"Gik","at":[5,3]}]}]})"},
        {"chapters/yard/map.json", R"({"name":"Yard","tiles":{"floor":{"art":"grass"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
            "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":["########","#......#","#......#","#......#","#......#","#......#","#......#","########"]}]})"},
        {"rulesets/yorehold/actions/finish-test.json", R"({"id":"finish-test","cost":0,"effects":[{"do":"damage","dice":10000,"target":"enemies"}]})"},
    };
    std::string error;
    WorldFixture before;
    check(before.loadJson("chapters/yard", files, 5, &error), "The yard loads as written");
    if (!before.chapter())
        return;

    yh::History history;
    EncountersEditor editor(history);
    const Chapter* chapter = before.chapter();
    check(editor.load(files.at("chapters/yard/chapter.json"), &error, EncountersEditor::Catalog::from(chapter->compendium),
        [chapter](yh::Cell cell) { return chapter->map.walkable(cell); }), "The editor opens it with the game's creatures");
    check(editor.catalog().creatures.contains("goblin") && editor.catalog().ai.contains("lookout") && editor.catalog().items.contains("mace"),
        "The catalog is the chapter's compendium");
    check(!editor.addCreature(0, "goblin", {0, 0}) && editor.addCreature(0, "goblin", {5, 4}).has_value(), "Walls are the map's own");
    editor.setCreatureName(0, 1, "Nok");
    editor.setFacing(0, 1, 270.0f);
    editor.setCreatureAi(0, 1, "lookout");
    editor.setGroupXp(0, 120);
    yh::LootTable loot;
    loot.coins = "40";
    loot.items.push_back({"mace", 1, 1});
    check(editor.setGroupLoot(0, loot) && editor.problems().empty(), "A second goblin, XP and loot are added");

    files["chapters/yard/chapter.json"] = editor.toJson();
    WorldFixture world;
    check(world.loadJson("chapters/yard", files, 5, &error), "The game loads what the editor saves");
    if (!world.chapter())
        return;
    const Chapter::Encounter& gate = world.chapter()->encounters[0];
    check(gate.creatures.size() == 2 && gate.creatures[1].name == "Nok" && gate.creatures[1].facing == 270.0f && gate.creatures[1].ai == "\"lookout\""
        && gate.xp == 120 && gate.loot.coins == "40" && gate.loot.items.size() == 1, "The chapter has the placement, its facing and AI, the XP and the loot");

    // Bo acts first and drops every enemy at once.
    auto win = [](WorldFixture& fight) {
        fight.sheet(1).stats.setBase("dex", 2000);
        json places = json::array();
        for (const auto& token : fight.tokens().tokens)
            places.push_back({token.position.x, token.position.y});
        return fight.send("fight", {{"group", 0}, {"at", places}}) && fight.currentCreature() == 1 && fight.send("use", {{"action", "finish-test"}});
    };
    const int xpBefore = world.sheet(0).xp;
    check(win(world), "Both goblins are beaten");
    check(world.said("Won 120") && world.sheet(0).xp == xpBefore + 120 && world.sheet(1).xp == xpBefore + 120, "The win gives the group's XP, not the chapter's");
    const auto& piles = world.piles();
    auto hasMace = [](const World::Pile& pile) {
        return std::any_of(pile.items.begin(), pile.items.end(), [](const yh::Item& item) { return item.id == "mace"; });
    };
    check(piles.size() == 2 && !hasMace(piles[0]) && piles[0].coins <= 12 && hasMace(piles[1]) && piles[1].coins >= 42 && piles[1].name == "Nok",
        "The group's loot lies with the last of them to fall, besides what each carried");

    // A chapter that says nothing about XP or loot plays as it always did.
    check(win(before) && before.said("Won 10") && before.sheet(0).xp == xpBefore + 10 && before.piles().size() == 1 && before.piles()[0].coins <= 12,
        "Without them a win gives the chapter's XP and only what the goblin carried");
}

void inCreate(const Check& check, const std::filesystem::path& scratch)
{
    yh::Ui ui;
    yh::Input input;
    yh::Font* title = nullptr;
    CreateScreen screen(ui, input, title);
    screen.table.stateDir = (scratch / "encounters-state").generic_string() + "/";
    screen.newPackage();
    EncountersEditor* editor = screen.encountersEditor();
    MapEditor* map = screen.mapEditor();
    check(screen.isOpen() && editor && map && editor->groups().empty() && editor->fixed().size() == 1, "Encounters mode opens a new package's chapter");
    if (!editor || !map)
        return;
    check(editor->catalog().creatures.contains("goblin") && editor->catalog().ai.contains("lookout"), "The game's own creatures and AI profiles are there to place");

    const std::filesystem::path file = std::filesystem::path(screen.packagePath()) / "chapters/chapter-one/chapter.json";
    const std::string untouched = *yh::readTextFile(file.string());
    check(screen.save() && *yh::readTextFile(file.string()) == untouched, "Saving with nothing changed leaves chapter.json alone");

    // One history for both modes, and the map as it is drawn right now.
    map->paint(map->wallLayer(0), {8, 8}, map->wallTile());
    map->endStroke();
    const std::optional<size_t> group = editor->addGroup("gate");
    check(group && !editor->addCreature(*group, "goblin", {8, 8}) && !editor->addCreature(*group, "goblin", {2, 2}), "Nobody is placed on a wall just painted, or on the hero");
    check(editor->addCreature(*group, "goblin", {10, 8}).has_value() && editor->setFacing(*group, 0, 180.0f) && editor->setGroupXp(*group, 75), "A goblin is placed and turned");
    check(screen.history().dirty() && screen.save() && !screen.history().dirty() && screen.status() == "Saved 2 files", "Save writes the map and the chapter");
    const json written = json::parse(*yh::readTextFile(file.string()));
    check(written.at("encounters").size() == 1 && written.at("encounters")[0].value("id", "") == "gate" && written.at("encounters")[0].at("xp") == 75
        && written.at("encounters")[0].at("creatures")[0].at("at") == json({10, 8}) && written.value("title", "") == "Chapter one" && written.at("party").size() == 1,
        "chapter.json on disk has the group and the rest of the chapter");

    screen.undo();
    screen.undo();
    screen.undo();
    check(editor->groups()[0].creatures.empty() && !map->map().walkable({8, 8}), "Undo steps back through the encounter edits first");
    screen.undo();
    screen.undo();
    check(editor->groups().empty() && map->map().walkable({8, 8}), "... then through the group and the wall, on the same history");
    for (int i = 0; i < 5; i++)
        screen.redo();
    check(editor->groups().size() == 1 && editor->xpOf(0) == 75 && !screen.history().dirty(), "Redo comes back to the saved state");

    // A wall painted over a creature would leave a chapter the game refuses: neither file is written.
    const std::filesystem::path mapFile = file.parent_path() / "map.json";
    const std::string mapWritten = *yh::readTextFile(mapFile.string());
    map->paint(map->wallLayer(0), {10, 8}, map->wallTile());
    map->endStroke();
    check(!screen.save() && screen.status().find("not saved") != std::string::npos && *yh::readTextFile(mapFile.string()) == mapWritten
        && json::parse(*yh::readTextFile(file.string())) == written, "A wall over a creature is not saved, and the status says why");
    check(editor->moveCreature(0, 0, {11, 8}) && screen.save() && *yh::readTextFile(mapFile.string()) != mapWritten
        && json::parse(*yh::readTextFile(file.string())).at("encounters")[0].at("creatures")[0].at("at") == json({11, 8}),
        "Once the creature has moved off it both are saved");

    const std::string folder = screen.packagePath();
    screen.openPackage(folder);
    check(screen.encountersEditor() && screen.encountersEditor()->groups().size() == 1 && screen.encountersEditor()->groups()[0].creatures[0].facing == 180.0f
        && !screen.history().canUndo(), "The saved package opens again with its group");

    // The game's own content opens too.
    screen.openPackage(YH_GAME_ASSETS);
    editor = screen.encountersEditor();
    check(editor && editor->groups().size() == 3 && editor->groups()[2].creatures.size() == 4 && editor->fixed().size() == 7, "The keep's encounters open in the editor");
    if (!editor)
        return;
    const std::vector<EncountersEditor::Problem> wrong = editor->problems();
    check(wrong.empty(), wrong.empty() ? "The keep has nothing wrong" : wrong[0].text.c_str());
}

}

void encountersEditorTests(const Check& check, const std::filesystem::path& scratch)
{
    loadAndSave(check);
    creatures(check);
    groups(check);
    xpAndLoot(check);
    problems(check);
    inTheGame(check);
    inCreate(check, scratch);
}
