// Story mode of the Create screen: the graph's commands and undo, what it writes, its suggestions
// and warnings, and story.json saved and read back in Create.

#include "screens/CreateScreen.h"
#include "screens/StoryEditor.h"

#include <yorehold/framework/save/SaveFile.h>

#include <nlohmann/json.hpp>

#include <algorithm>
#include <filesystem>
#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;
using nlohmann::json;
using Kind = StoryEditor::Kind;

// Written by hand, with fields the editor has no tool for.
const char* storyJson = R"({
  "format": 1,
  "mood": "grim",
  "nodes": [
    {"id": "keep", "kind": "scene", "title": "The keep", "text": "Rain on the walls.", "at": [0, 0], "chapter": "chapters/keep", "colour": "grey"},
    {"id": "gate", "kind": "encounter", "title": "At the gate", "at": [0, 80], "chapter": "chapters/keep", "group": "gate"},
    {"id": "rescue", "kind": "quest", "title": "Find Tobb", "at": [240, 80], "chapter": "chapters/keep", "quest": "rescue"},
    {"id": "end", "kind": "ending", "title": "Home", "at": [0, 160]}
  ],
  "links": [
    {"from": "keep", "to": "gate", "text": "They knock", "note": "loud"},
    {"from": "gate", "to": "end", "when": ["gate-open"]}
  ]
})";

StoryEditor::Catalog keepCatalog()
{
    StoryEditor::Catalog catalog;
    StoryEditor::Catalog::Chapter keep;
    keep.folder = "chapters/keep";
    keep.id = "keep";
    keep.title = "The Keep";
    keep.groups = {{"gate", 50, 2}, {"hall", 75, 3}};
    keep.dialogues = {"chapters/keep/dialogue/wren.json"};
    keep.quests = {{"rescue", "Find Tobb"}};
    keep.ending = "chapters/keep/ending.json";
    StoryEditor::Catalog::Chapter warren;
    warren.folder = "chapters/warren";
    warren.id = "warren";
    catalog.chapters = {keep, warren};
    catalog.adventure = true;
    catalog.travel = {{"keep", "warren"}};
    return catalog;
}

bool offers(const StoryEditor& editor, std::string_view key)
{
    const std::vector<StoryEditor::Suggestion> all = editor.suggestions();
    return std::any_of(all.begin(), all.end(), [&](const StoryEditor::Suggestion& s) { return s.key == key; });
}

void loadAndSave(const Check& check)
{
    yh::History history;
    StoryEditor editor(history);
    std::string error;
    check(editor.load(storyJson, &error) && editor.nodes().size() == 4 && editor.links().size() == 2, "A hand-written story opens");
    check(editor.nodes()[1].kind == Kind::Encounter && editor.nodes()[1].ref == "gate" && editor.nodes()[2].ref == "rescue" && editor.links()[1].when.size() == 1,
        "Its nodes, what they point at and the link's flags are read");
    const json out = json::parse(editor.toJson());
    check(out.value("mood", "") == "grim" && out["nodes"][0].value("colour", "") == "grey" && out["links"][0].value("note", "") == "loud",
        "Fields the editor has no tool for are written back");
    check(out["nodes"][1].value("group", "") == "gate" && out["nodes"][2].value("quest", "") == "rescue" && !out["nodes"][3].contains("cutscene"),
        "What a node points at is written under its kind's own name");
    StoryEditor again(history);
    check(again.load(editor.toJson()) && again.toJson() == editor.toJson(), "What it writes reads back the same");

    StoryEditor broken(history);
    check(!broken.load(R"({"nodes": [{"id": "a", "kind": "scene"}, {"id": "a", "kind": "quest"}]})", &error) && error.find("two nodes") != std::string::npos,
        "Two nodes with one id are refused, and it says why");
    check(!broken.load(R"({"nodes": [{"id": "a", "kind": "boss"}]})", &error) && !error.empty(), "So is a kind it doesn't know");
    check(!broken.load(R"({"nodes": [{"id": "a", "kind": "scene"}], "links": [{"from": "a", "to": "b"}]})", &error) && !error.empty(), "And a link to nothing");
    check(!broken.load(R"({"format": 9})", &error) && error.find("newer") != std::string::npos, "A file from a newer game says so");
    check(!broken.load("{ not json", &error) && !broken.loaded(), "Something that isn't JSON doesn't open");

    StoryEditor fresh(history);
    fresh.create();
    check(fresh.loaded() && fresh.nodes().empty() && json::parse(fresh.toJson())["format"] == 1, "A new story is an empty graph");
}

void commands(const Check& check)
{
    yh::History history;
    StoryEditor editor(history);
    editor.load(storyJson);
    editor.setCatalog(keepCatalog());

    const std::optional<size_t> talk = editor.addNode(Kind::Dialogue, {300, 0});
    check(talk && editor.nodes()[*talk].id == "dialogue", "A new node gets an id from its kind");
    check(editor.addNode(Kind::Dialogue, {0, 0}) && editor.nodes().back().id == "dialogue-2", "And the next free one after that");
    check(!editor.addNode(Kind::Scene, {0, 0}, "keep") && !editor.addNode(Kind::Scene, {0, 0}, "Bad Id"), "Taken and bad ids are refused");

    check(editor.addLink(0, *talk) && !editor.addLink(0, *talk) && !editor.addLink(0, 0), "A link goes once each way and never to itself");
    check(editor.renameNode(0, "castle") && editor.links()[0].from == "castle" && editor.findLink("castle", "dialogue"), "Links follow a rename");
    check(!editor.renameNode(0, "gate"), "A rename can't take another node's id");

    editor.moveNode(1, {10, 90});
    editor.moveNode(1, {20, 100});
    editor.endTyping();
    check(editor.nodes()[1].at == yh::Vec2{20, 100}, "A node moves");
    history.undo();
    check(editor.nodes()[1].at == yh::Vec2{0, 80}, "One drag is one undo step");

    check(editor.setChapter(*talk, "chapters/keep") && editor.setRef(*talk, "chapters/keep/dialogue/wren.json"), "A conversation points at its file");
    check(!editor.setChapter(*talk, "chapters/nowhere"), "Only a chapter the package has can be picked");
    check(editor.setKind(*talk, Kind::Quest) && editor.nodes()[*talk].ref.empty(), "A new kind forgets what it pointed at");
    check(editor.setSteps(*talk, {"Find the cellar", "Open it"}) && !editor.setSteps(*talk, {"", "x"}), "A quest has steps, none empty");
    check(editor.setXp(1, 40) && !editor.setXp(1, -5) && !editor.setXp(0, 10), "Fights and quests take XP; nothing negative, and scenes don't");
    check(editor.setMapSize(0, 30, 20) && !editor.setMapSize(0, 3, 20) && !editor.setMapSize(1, 30, 20), "Only a scene takes a map size, 8 to 200 each way");
    check(editor.setLinkText(0, "They go in") && editor.setLinkWhen(0, {"gate-open"}) && !editor.setLinkWhen(0, {"a", "a"}), "A link has words and flags");

    const size_t before = editor.links().size();
    check(editor.removeNode(1) && editor.links().size() == before - 2 && !editor.find("gate"), "A removed node takes its links with it");
    history.undo();
    check(editor.find("gate") && editor.links().size() == before, "Undo brings both back");
    check(editor.removeLink(0) && editor.links().size() == before - 1, "A link can go by itself");
}

void suggestions(const Check& check)
{
    yh::History history;
    StoryEditor editor(history);
    editor.create();
    editor.setCatalog(keepCatalog());

    check(offers(editor, "scene|chapters/keep") && offers(editor, "scene|chapters/warren") && offers(editor, "encounter|chapters/keep|hall")
            && offers(editor, "dialogue|chapters/keep|chapters/keep/dialogue/wren.json") && offers(editor, "quest|chapters/keep|rescue")
            && offers(editor, "ending|chapters/keep"),
        "What the package has is offered as nodes");
    check(editor.accept("scene|chapters/keep") && editor.nodes().size() == 1 && editor.nodes()[0].title == "The Keep" && editor.nodes()[0].chapter == "chapters/keep",
        "Taking one adds a scene for the chapter");
    check(editor.accept("encounter|chapters/keep|gate") && editor.findLink("keep", "gate") && editor.nodes()[1].at.x > editor.nodes()[0].at.x,
        "A fight goes beside its chapter's scene with a link from it");
    check(offers(editor, "xp|gate|50") && editor.accept("xp|gate|50") && editor.nodes()[1].xp == 50, "Its XP is proposed from creature levels");
    check(!offers(editor, "xp|gate|50"), "and not again once it matches");
    check(editor.accept("scene|chapters/warren") && editor.findLink("keep", "warren") && !editor.findLink("warren", "keep"),
        "Travel in adventure.json links the scenes it joins, the way it goes");
    check(!editor.accept("scene|chapters/warren"), "A suggestion that is taken is gone");

    history.undo();
    check(!editor.find("warren") && offers(editor, "scene|chapters/warren"), "Taking one is one undo step");

    check(editor.dismiss("ending|chapters/keep") && !offers(editor, "ending|chapters/keep") && editor.dismissed().size() == 1, "One can be turned down");
    check(json::parse(editor.toJson())["dismissed"][0] == "ending|chapters/keep", "and stays turned down in the file");
    check(editor.restoreDismissed() && offers(editor, "ending|chapters/keep"), "Turned down ones can come back");

    // Quest steps from what comes after it, and a map for a scene with no chapter yet.
    const std::optional<size_t> quest = editor.addNode(Kind::Quest, {500, 0}, "hunt");
    editor.addLink(*quest, *editor.find("gate"));
    check(editor.accept("steps|hunt") && editor.nodes()[*quest].steps == std::vector<std::string>{"gate"}, "A quest's steps come from the nodes after it");
    const std::optional<size_t> cave = editor.addNode(Kind::Scene, {700, 0}, "cave");
    const std::optional<size_t> bats = editor.addNode(Kind::Encounter, {700, 80}, "bats");
    editor.addLink(*cave, *bats);
    check(offers(editor, "map|cave") && editor.accept("map|cave") && editor.nodes()[*cave].mapWidth == 32 && editor.nodes()[*cave].mapHeight == 20,
        "A scene with no chapter is offered a map with room for its fights");

    const int taken = editor.acceptAll();
    check(taken >= 4 && editor.find("warren") && editor.find("wren") && editor.find("rescue") && editor.find("hall"), "Take all takes every one at once");
    const std::vector<StoryEditor::Suggestion> left = editor.suggestions();
    check(left.empty(), ("Nothing is left to suggest" + (left.empty() ? std::string() : ": " + left[0].key)).c_str());
    history.undo();
    check(!editor.find("warren") && !editor.find("hall"), "and undoes as one");
}

void problems(const Check& check)
{
    yh::History history;
    StoryEditor editor(history);
    editor.load(storyJson);
    editor.setCatalog(keepCatalog());
    auto says = [&](std::string_view part) {
        const std::vector<StoryEditor::Problem> all = editor.problems();
        return std::any_of(all.begin(), all.end(), [&](const StoryEditor::Problem& p) { return p.text.find(part) != std::string::npos && !p.error; });
    };
    check(says("rescue isn't linked"), "A node on its own is a warning");
    editor.setRef(1, "cellar");
    check(says("group cellar isn't in chapters/keep"), "So is pointing at a group the chapter doesn't have");
    const std::optional<size_t> after = editor.addNode(Kind::Scene, {0, 240}, "after");
    editor.addLink(3, *after);
    check(says("end is an ending but the story goes on"), "And a story going on after an ending");
    const std::optional<size_t> warren = editor.addNode(Kind::Scene, {200, 0}, "warren");
    editor.setChapter(*warren, "chapters/warren");
    editor.addLink(*warren, 0);
    check(says("adventure.json has no way from warren to keep"), "A link between scenes that adventure.json can't travel");
    editor.addLink(0, *warren);
    check(!says("no way from keep to warren"), "but not the way it can");
    const std::vector<StoryEditor::Problem> all = editor.problems();
    check(std::none_of(all.begin(), all.end(), [](const StoryEditor::Problem& p) { return p.error; }), "None of them stops a save");
}

void inCreate(const Check& check, const std::filesystem::path& scratch)
{
    yh::Ui ui;
    yh::Input input;
    yh::Font* title = nullptr;
    CreateScreen screen(ui, input, title);
    screen.table.stateDir = (scratch / "story-state").generic_string() + "/";
    screen.newPackage();
    StoryEditor* story = screen.storyEditor();
    check(story && story->nodes().empty() && offers(*story, "scene|chapters/chapter-one"), "A new package has an empty story that offers its chapter");
    check(screen.save() && screen.status() == "Nothing to save" && !std::filesystem::exists(std::filesystem::path(screen.packagePath()) / "story.json"),
        "An untouched story isn't written");

    // A fight made in Encounters mode shows up here before it is saved.
    EncountersEditor* encounters = screen.encountersEditor();
    const std::optional<size_t> group = encounters ? encounters->addGroup("ambush") : std::nullopt;
    check(group && encounters->addCreature(*group, "goblin", {6, 6}), "A fight is placed");
    story->setCatalog(screen.storyCatalog());
    check(offers(*story, "encounter|chapters/chapter-one|ambush"), "Story mode offers it");
    story->acceptAll();
    const std::optional<size_t> ambush = story->find("ambush");
    check(ambush && story->nodes()[*ambush].xp == encounters->proposedXp(*group), "with the XP Encounters mode proposes");

    check(screen.save() && std::filesystem::exists(std::filesystem::path(screen.packagePath()) / "story.json"), "Save writes story.json");
    const std::string written = *yh::readTextFile((std::filesystem::path(screen.packagePath()) / "story.json").string());
    yh::History history;
    StoryEditor reread(history);
    check(reread.load(written) && reread.find("chapter-one") && reread.find("ambush") && reread.findLink("chapter-one", "ambush"), "and it reads back");

    // The same history as the other modes.
    story->setTitle(*ambush, "Ambush on the road");
    screen.undo();
    check(story->nodes()[*ambush].title == "ambush", "Undo is the shared one");

    // The game's own content: every suggestion taken leaves nothing that stops a save.
    screen.openPackage(YH_GAME_ASSETS);
    StoryEditor* keep = screen.storyEditor();
    check(keep && offers(*keep, "scene|chapters/goblin-keep") && offers(*keep, "ending|chapters/goblin-keep"), "The keep's chapter and ending are offered");
    check(keep && keep->acceptAll() > 3 && keep->find("goblin-keep") && keep->find("entry-hall"), "Taking them all draws the keep");
    const std::vector<StoryEditor::Problem> wrong = keep ? keep->problems() : std::vector<StoryEditor::Problem>{};
    check(std::none_of(wrong.begin(), wrong.end(), [](const StoryEditor::Problem& p) { return p.error; }), "Nothing in it is an error");
}

}

void storyEditorTests(const Check& check, const std::filesystem::path& scratch)
{
    loadAndSave(check);
    commands(check);
    suggestions(check);
    problems(check);
    inCreate(check, scratch);
}
