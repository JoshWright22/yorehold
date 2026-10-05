// Cutscene mode of the Create screen: its commands, undo, timing and the preview's frames, what it
// writes, the chapter's triggers and endings, and the game loading what was written.

#include "content/Chapter.h"
#include "screens/CreateScreen.h"
#include "screens/CutsceneEditor.h"

#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/save/SaveFile.h>

#include <nlohmann/json.hpp>

#include <algorithm>
#include <cmath>
#include <filesystem>
#include <functional>
#include <set>

namespace
{

using Check = std::function<void(bool, const char*)>;
using nlohmann::json;
using Kind = CutsceneEditor::Kind;

// Written by hand, with fields the editor has no tool for.
const char* endingJson = R"({
  "music": "quiet.ogg",
  "steps": [
    {"bars": true},
    {"pause": 0.8},
    {"camera": [2816, 928], "zoom": 1.3, "seconds": 3.5, "wait": false},
    {"caption": "The brazier gutters out.", "seconds": 3.5, "voice": "grak.ogg"},
    {"fade": [0, 0, 0, 255], "seconds": 1.5},
    {"title": "The End"},
    {"event": "finished"}
  ]
})";

bool near(double a, double b)
{
    return std::abs(a - b) < 1e-4;
}

void loadAndSave(const Check& check)
{
    yh::History history;
    CutsceneEditor editor(history);
    std::string error;
    check(editor.load(endingJson, &error) && editor.steps().size() == 7, "A hand-written cutscene opens");
    check(editor.steps()[2].kind == Kind::Camera && editor.steps()[2].camera.x == 2816 && !editor.steps()[2].wait
        && editor.steps()[5].kind == Kind::Title && editor.steps()[5].seconds == 3, "Its steps read as the game reads them, a title's time too");
    const json out = json::parse(editor.toJson());
    check(out.value("music", "") == "quiet.ogg" && out["steps"][3].value("voice", "") == "grak.ogg", "Fields the editor has no tool for are written back");
    check(out["steps"][2]["zoom"].dump() == "1.3" && out["steps"][2]["camera"].dump() == "[2816,928]", "Numbers are written as they were typed, not as floats");
    check(out["steps"][5].value("seconds", 0.0) == 3, "A title's time is written, so it doesn't change on the way back");
    const std::optional<yh::Cutscene> read = editor.cutscene();
    check(read && read->steps.size() == 7 && read->steps[3].text == "The brazier gutters out." && read->steps[1].seconds == 0.8,
        "What it writes reads back in the game's own format");
    check(editor.problems().empty(), "The example has nothing wrong");
    check(json::parse(CutsceneEditor(history).toJson()) == json::object(), "Nothing open writes an empty object");

    CutsceneEditor broken(history);
    check(!broken.load(R"({"steps": [{"event": ""}]})", &error) && !error.empty() && !broken.loaded(), "A file the game refuses doesn't open, and says why");
    check(!broken.load("{ not json", &error) && !error.empty(), "Neither does one that isn't JSON");

    CutsceneEditor fresh(history);
    fresh.create();
    check(fresh.steps().size() == 3 && fresh.steps()[0].kind == Kind::Bars && fresh.steps()[0].on && !fresh.steps()[2].on && fresh.cutscene(),
        "A new cutscene is bars in, a caption and bars out, and the game reads it");
    check(fresh.problems().size() == 1 && !fresh.problems()[0].error, "Its empty caption is only a warning");
}

void steps(const Check& check)
{
    yh::History history;
    CutsceneEditor editor(history);
    editor.load(endingJson);
    editor.setBounds(yh::Rect{0, 0, 3200, 1600});

    const std::optional<size_t> camera = editor.addStep(Kind::Camera, 3);
    check(camera == 4u && editor.steps()[4].kind == Kind::Camera && editor.steps()[4].camera.x == 2816, "A new camera step starts where the last one looked");
    const std::optional<size_t> bars = editor.addStep(Kind::Bars);
    check(bars == 8u && !editor.steps()[8].on, "New bars go the other way from how they stand");
    check(editor.addStep(Kind::Event) && editor.steps().back().text == "finished", "A new event is one the game knows");
    history.undo();
    history.undo();
    check(editor.steps().size() == 8, "Undo takes added steps away");

    CutsceneEditor::Step moved = editor.steps()[4];
    moved.camera = {100, 200};
    moved.zoom = 2;
    check(editor.setStep(4, moved, "camera") && editor.steps()[4].camera.y == 200 && editor.steps()[4].zoom == 2, "A camera is aimed and zoomed");
    moved.zoom = 80;
    check(!editor.setStep(4, moved), "A zoom the game refuses is refused");
    moved = editor.steps()[4];
    moved.seconds = 700;
    check(!editor.setStep(4, moved), "So are seconds past 600");
    moved.seconds = 2;
    moved.ease = "wobbly";
    check(!editor.setStep(4, moved), "And an ease the game doesn't know");
    moved.ease = "outBack";
    check(editor.setStep(4, moved) && json::parse(editor.toJson())["steps"][4].value("ease", "") == "outBack", "A known ease is written");
    check(!editor.setStep(4, editor.steps()[4]), "Setting what is there already is no change");

    CutsceneEditor::Step line = editor.steps()[3];
    line.text = "The brazier";
    editor.setStep(3, line, "line");
    line.text = "The brazier dies.";
    editor.setStep(3, line, "line");
    editor.endTyping();
    history.undo();
    check(editor.steps()[3].text == "The brazier gutters out.", "Typing a line is one undo step");
    CutsceneEditor::Step event = editor.steps()[7];
    event.text = "";
    check(!editor.setStep(7, event), "An event needs a name");

    check(editor.moveStep(0, 1) && editor.steps()[1].kind == Kind::Bars && !editor.moveStep(0, -1), "Steps move up and down, not off the end");
    history.undo();
    const std::optional<size_t> copy = editor.copyStep(3);
    check(copy == 4u && editor.steps()[4].text == editor.steps()[3].text && editor.steps().size() == 9, "A copy goes right after the step");
    check(editor.removeStep(4) && editor.steps().size() == 8 && !editor.removeStep(99), "Remove takes it away");
}

void timing(const Check& check)
{
    yh::History history;
    CutsceneEditor editor(history);
    editor.load(R"({"steps": [
        {"bars": true},
        {"camera": [100, 0], "zoom": 2, "seconds": 2, "ease": "linear", "wait": false},
        {"caption": "Hello", "seconds": 3},
        {"fade": [0, 0, 0, 255], "seconds": 1},
        {"event": "finished"}
    ]})");
    const std::vector<CutsceneEditor::Span> spans = editor.spans();
    check(spans[1].start == 0 && spans[2].start == 0, "A step that doesn't wait starts with the next one");
    check(spans[3].start == 3 && spans[4].start == 4 && near(editor.length(), 4), "The next waits for everything running to finish");

    const CutsceneEditor::Frame half = editor.frameAt(1, {0, 0}, 1);
    check(near(half.camera.x, 50) && near(half.zoom, 1.5), "Halfway through a camera move the view is halfway there");
    check(half.lines.size() == 1 && half.lines[0].text == "Hello" && half.lines[0].alpha == 1, "The caption shows at full strength in its middle");
    check(near(editor.frameAt(0.3, {0, 0}, 1).lines[0].alpha, 0.5), "And fades in over its first 0.6 s");
    check(half.bars == 1 && near(editor.frameAt(0.2, {0, 0}, 1).bars, 0.5), "The bars slide in over 0.4 s");
    const CutsceneEditor::Frame fading = editor.frameAt(3.5, {0, 0}, 1);
    check(fading.lines.empty() && fading.fade.a == 128 && near(fading.camera.x, 100), "Then the caption is gone, the fade halfway and the camera arrived");
    check(editor.frameAt(10, {0, 0}, 1).fade.a == 255, "At the end the screen is black");
}

void problems(const Check& check)
{
    yh::History history;
    CutsceneEditor editor(history);
    editor.load(R"({"steps": [
        {"camera": [9000, 50], "ease": "wobbly"},
        {"caption": "", "seconds": 0},
        {"event": "dance"}
    ]})");
    editor.setBounds(yh::Rect{0, 0, 1000, 1000});
    const std::vector<CutsceneEditor::Problem> found = editor.problems();
    auto has = [&](const std::string& text) {
        return std::any_of(found.begin(), found.end(), [&](const CutsceneEditor::Problem& p) { return p.text.find(text) != std::string::npos; });
    };
    check(has("outside the map") && has("wobbly") && has("no line") && has("no time") && has("event dance"),
        "Off-map cameras, unknown eases and events, and empty or instant captions are listed");
    check(std::none_of(found.begin(), found.end(), [](const CutsceneEditor::Problem& p) { return p.error; }), "None of them stops a save");
}

void hooks(const Check& check)
{
    yh::History history;
    CutsceneHooks hooks(history);
    const std::string chapter = R"({
      "id": "keep",
      "endings": {"cleared": "ending.json"},
      "triggers": [{"id": "talk", "dialogue": "dialogue/a.json", "cutscene": "cutscenes/intro.json", "note": "x"}]
    })";
    const std::set<std::string> files{"chapters/keep/ending.json", "chapters/keep/cutscenes/intro.json", "cutscenes/shared.json"};
    check(hooks.load(chapter, "chapters/keep", [&](const std::string& p) { return files.contains(p); }), "A chapter's triggers and endings are read");
    check(hooks.plays(hooks.cleared(), "chapters/keep/ending.json") && hooks.resolve("cutscenes/shared.json") == "cutscenes/shared.json",
        "Names are found in the chapter folder first, then from the root, as the game does");
    check(hooks.named().size() == 2 && !hooks.changed(), "It lists the cutscenes it plays");

    const std::optional<size_t> added = hooks.addTrigger("chapters/keep/cutscenes/intro.json", {"gate-open"});
    check(added == 1u && hooks.triggers()[1].id == "cutscene-1" && hooks.triggers()[1].cutscene == "cutscenes/intro.json" && hooks.changed(),
        "A new trigger names the file from the chapter folder");
    check(!hooks.setTriggerId(1, "talk") && !hooks.setTriggerId(1, "Bad Id") && hooks.setTriggerId(1, "gate"), "Trigger ids are unique and plain");
    check(hooks.setTriggerWhen(1, {"gate-open", "night"}) && !hooks.setTriggerWhen(1, {"a", "a"}), "Its flags change, each once");
    check(hooks.setWipe("cutscenes/shared.json") && hooks.wipe() == "cutscenes/shared.json" && !hooks.setWin("cutscenes/shared.json"),
        "A wipe can play one; a chapter with no winCondition can't");
    check(hooks.removeTrigger(0) && hooks.triggers().size() == 2 && hooks.triggers()[0].cutscene.empty() && hooks.triggers()[0].dialogue == "dialogue/a.json",
        "A trigger that also opens a conversation keeps it");
    check(hooks.setCleared("") && hooks.cleared().empty(), "The cleared ending can go");

    const json written = json::parse(hooks.applyTo(chapter));
    check(written.value("id", "") == "keep" && !written.contains("endings") && written["onWipe"].value("cutscene", "") == "cutscenes/shared.json",
        "Applied to a chapter, only these fields change");
    check(written["triggers"][0].value("note", "") == "x" && written["triggers"][1]["when"].size() == 2, "Trigger fields it has no tool for stay");
    check(hooks.applyTo(written.dump(2)) == written.dump(2), "A chapter that already has them is left as it was");
    check(hooks.problems().empty(), "Nothing is wrong with them");
    check(hooks.setCleared("chapters/keep/gone.json") && !hooks.problems().empty() && hooks.problems()[0].error, "A file that isn't there stops a save");

    for (int i = 0; i < 7; i++)
        history.undo();
    check(hooks.triggers().size() == 1 && hooks.cleared() == "ending.json" && hooks.wipe().empty(), "Undo puts it all back");
}

void inCreate(const Check& check, const std::filesystem::path& scratch)
{
    yh::Ui ui;
    yh::Input input;
    yh::Font* title = nullptr;
    CreateScreen screen(ui, input, title);
    screen.table.stateDir = (scratch / "cutscene-state").generic_string() + "/";
    screen.newPackage();
    check(screen.isOpen() && screen.cutsceneFiles().empty() && !screen.cutsceneEditor(), "A new package's chapter has no cutscenes");

    const std::string made = screen.newCutscene();
    check(made == "chapters/chapter-one/cutscenes/cutscene.json" && screen.cutscene() == made && screen.cutsceneFiles().size() == 1,
        "New starts a cutscene in the chapter's cutscenes folder and opens it");
    CutsceneEditor* cut = screen.cutsceneEditor();
    CutsceneEditor::Step line = cut->steps()[1];
    line.text = "The road begins.";
    cut->setStep(1, line);
    CutsceneHooks* hooks = screen.cutsceneHooks();
    check(hooks && hooks->addTrigger(made) && hooks->setCleared(made), "It is set to play at the start and when the chapter is cleared");

    const std::filesystem::path folder = std::filesystem::path(screen.packagePath()) / "chapters/chapter-one";
    check(screen.save() && screen.status() == "Saved 2 files" && std::filesystem::exists(folder / "cutscenes/cutscene.json"), "Save writes the cutscene and the chapter");
    yh::FileSystem files;
    files.mountFolder(YH_FRAMEWORK_ASSETS, "framework");
    files.mountFolder(YH_GAME_ASSETS, "game");
    files.mountFolder(screen.packagePath(), "package");
    std::string error;
    const std::optional<Chapter> loaded = Chapter::load(files, "chapters/chapter-one", &error);
    check(loaded && loaded->triggers.size() == 1 && loaded->triggers[0].cutscene == made && loaded->clearedCutscene == made,
        ("The game loads the chapter and plays it at both " + error).c_str());

    // Groups from Encounters mode and triggers from here end up in the same file.
    EncountersEditor* encounters = screen.encountersEditor();
    const std::optional<size_t> group = encounters ? encounters->addGroup("ambush") : std::nullopt;
    const bool placed = group && encounters->addCreature(*group, "goblin", {6, 6});
    hooks->setTriggerWhen(0, {"ambushed"});
    check(placed && screen.save(), "An encounter and a trigger change are saved together");
    const json chapter = json::parse(*yh::readTextFile((folder / "chapter.json").string()));
    check(chapter["encounters"].size() == 1 && chapter["triggers"][0]["when"][0] == "ambushed", "The chapter has both");

    // A broken step keeps it from being written.
    CutsceneEditor::Step bad = cut->steps()[0];
    bad.kind = Kind::Event;
    bad.text = "";
    check(!cut->setStep(0, bad), "The editor refuses a step the game would");

    // The same history as the other modes.
    screen.undo();
    check(screen.cutsceneHooks()->triggers()[0].when.empty(), "Undo steps back through the trigger change too");

    // The game's own content opens too.
    screen.openPackage(YH_GAME_ASSETS);
    const std::vector<std::string> keep = screen.cutsceneFiles();
    check(std::find(keep.begin(), keep.end(), "chapters/goblin-keep/ending.json") != keep.end(), "The keep's ending is listed");
    for (const std::string& path : keep)
    {
        screen.openCutscene(path);
        CutsceneEditor* editor = screen.cutsceneEditor();
        const std::vector<CutsceneEditor::Problem> wrong = editor ? editor->problems() : std::vector<CutsceneEditor::Problem>{};
        check(editor && wrong.empty(), (path + (editor ? " has nothing wrong" : " opens")).c_str());
        check(editor && screen.cutsceneHooks() && screen.cutsceneHooks()->plays(screen.cutsceneHooks()->cleared(), path), "It plays when the keep is cleared");
    }
}

}

void cutsceneEditorTests(const Check& check, const std::filesystem::path& scratch)
{
    loadAndSave(check);
    steps(check);
    timing(check);
    problems(check);
    hooks(check);
    inCreate(check, scratch);
}
