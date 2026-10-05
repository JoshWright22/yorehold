// Dialogue mode of the Create screen: its commands, undo, what it writes, the companion actions
// and the game playing what was written.

#include "screens/CreateScreen.h"
#include "screens/DialogueEditor.h"

#include <yorehold/framework/save/SaveFile.h>

#include <nlohmann/json.hpp>

#include <algorithm>
#include <filesystem>
#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;
using nlohmann::json;

// Written by hand, with fields the editor has no tool for.
const char* tamJson = R"({
  "id": "tam",
  "start": "hello",
  "mood": "wary",
  "nodes": [
    {
      "id": "hello",
      "speaker": "Tam",
      "text": "You again.",
      "voice": "tam-hello.ogg",
      "choices": [
        {"id": "join", "text": "Come with us.", "next": "yes", "forbid": ["tam_joined"], "emote": "nod"},
        {"id": "talk", "text": "Tell me about the road.", "check": {"skill": "persuasion", "difficulty": 12, "success": "road", "failure": ""}},
        {"id": "bye", "text": "Later.", "next": ""}
      ]
    },
    {"id": "yes", "speaker": "Tam", "text": "Fine.", "set": ["tam_joined"], "do": ["recruit"]},
    {"id": "road", "speaker": "Tam", "text": "It's long."}
  ]
})";

DialogueEditor::Catalog tamCatalog()
{
    DialogueEditor::Catalog catalog;
    catalog.skills = {"persuasion", "insight", "cha"};
    catalog.companions = {"tam", "wren"};
    catalog.companion = true;
    return catalog;
}

void loadAndSave(const Check& check)
{
    yh::History history;
    DialogueEditor editor(history);
    std::string error;
    check(editor.load(tamJson, &error, tamCatalog()) && editor.nodes().size() == 3 && editor.start() == "hello", "A hand-written conversation opens");
    const json out = json::parse(editor.toJson());
    check(out.value("mood", "") == "wary" && out["nodes"][0].value("voice", "") == "tam-hello.ogg" && out["nodes"][0]["choices"][0].value("emote", "") == "nod",
        "Fields the editor has no tool for are written back");
    check(json::parse(DialogueEditor(history).toJson()) == json::object(), "Nothing open writes an empty object");
    const std::optional<yh::Dialogue> read = editor.dialogue();
    check(read && read->nodes[1].flags.actions == std::vector<std::string>{"recruit"} && read->nodes[0].choices[1].check->difficulty == 12,
        "What it writes reads back in the game's own format");
    check(editor.problems().empty(), "The example has nothing wrong");

    DialogueEditor broken(history);
    json bad = json::parse(tamJson);
    bad["nodes"][0]["choices"][0]["next"] = "nowhere";
    check(!broken.load(bad.dump(), &error) && error.find("hello/join") != std::string::npos && !broken.loaded(),
        "A file the game refuses doesn't open, and says why");
    check(!broken.load("{ not json", &error) && !error.empty(), "Neither does one that isn't JSON");

    DialogueEditor fresh(history);
    fresh.create("stranger");
    check(fresh.nodes().size() == 1 && fresh.start() == "start" && fresh.dialogue() && fresh.problems().size() == 1,
        "A new conversation is one empty node, valid, with a note that it has no line");
}

void nodes(const Check& check)
{
    yh::History history;
    DialogueEditor editor(history);
    editor.load(tamJson, nullptr, tamCatalog());

    const std::optional<size_t> added = editor.addNode();
    check(added == 3 && editor.nodes()[3].id == "node-4" && editor.nodes()[3].speaker == "Tam", "A new node gets a free id and the last speaker");
    check(!editor.addNode("yes") && !editor.addNode("two words"), "Node ids are unique and have no spaces");

    check(editor.renameNode(1, "agreed") && editor.nodes()[0].choices[0].next == "agreed", "Renaming a node takes the replies that led to it along");
    check(editor.renameNode(2, "the-road") && editor.nodes()[0].choices[1].check->success == "the-road", "... and checks");
    check(editor.renameNode(0, "hi") && editor.start() == "hi", "... and the start");
    check(!editor.renameNode(0, "agreed") && !editor.renameNode(0, ""), "Not onto another node's id or nothing");

    check(editor.setStart(1) && editor.start() == "agreed" && !editor.setStart(1), "The start can move");
    check(editor.removeNode(1) && editor.start() == "hi" && editor.nodes()[0].choices[0].next.empty() && editor.dialogue(),
        "Removing a node ends the replies that led there, and the start goes to the first node");
    check(editor.linksTo("the-road") == 1 && editor.linksTo("node-4") == 0, "Links to a node are counted");

    // Typing a line is one undo step until the box is left.
    check(editor.setText(0, "You") && editor.setText(0, "You there") && !editor.setText(0, "You there"), "A line is typed");
    editor.endTyping();
    check(editor.setSpeaker(0, "Tamsin"), "And a speaker");
    history.undo();
    history.undo();
    check(editor.nodes()[0].text == "You again." && editor.nodes()[0].speaker == "Tam", "Undo takes back the speaker, then the whole line");
    history.undo();
    check(editor.nodes().size() == 4 && editor.nodes()[1].id == "agreed" && editor.start() == "agreed", "Undo brings a removed node back, and the start with it");
    while (history.undo())
    {
    }
    yh::History other;
    DialogueEditor original(other);
    original.load(tamJson, nullptr, tamCatalog());
    check(editor.toJson() == original.toJson(), "Undoing everything gives the file as it was");
    while (history.redo())
    {
    }
    check(editor.nodes().size() == 3 && editor.nodes()[0].id == "hi" && editor.nodes()[0].speaker == "Tamsin", "Redo comes all the way back");

    yh::DialogueFlags flags;
    flags.set = {"met_tam"};
    flags.actions = {"approve 2"};
    check(editor.setNodeFlags(0, flags) && editor.nodes()[0].flags.set == flags.set, "A node can set flags and do things on arrival");
    flags.clear = {"met_tam"};
    check(!editor.setNodeFlags(0, flags), "But not set and clear the same flag");
    DialogueEditor single(history);
    single.create("one");
    check(!single.removeNode(0), "The last node can't go");
}

void choices(const Check& check)
{
    yh::History history;
    DialogueEditor editor(history);
    editor.load(tamJson, nullptr, tamCatalog());

    const std::optional<size_t> added = editor.addChoice(2);
    check(added == 0 && editor.nodes()[2].choices[0].text == "..." && editor.nodes()[2].choices[0].id == "reply-1"
        && editor.nodes()[2].choices[0].next.empty(), "A new reply has words, a free id and ends the conversation");
    check(editor.setNext(2, 0, "hello") && !editor.setNext(2, 0, "nowhere") && editor.nodes()[2].choices[0].next == "hello", "It can lead to any node there is");
    check(!editor.setChoiceText(2, 0, "") && editor.setChoiceText(2, 0, "Back to it."), "A reply needs words");
    check(!editor.setChoiceId(0, 2, "join") && editor.setChoiceId(0, 2, "leave"), "Reply ids are unique in their node");

    check(editor.moveChoice(0, 2, -1) && editor.nodes()[0].choices[1].id == "leave" && !editor.moveChoice(0, 0, -1) && !editor.moveChoice(0, 2, 1),
        "Replies move up and down, not past the ends");

    check(!editor.setConditions(0, 0, {"a"}, {"a"}) && editor.setConditions(0, 0, {"met_tam"}, {"tam_joined"})
        && editor.nodes()[0].choices[0].require == std::vector<std::string>{"met_tam"}, "A reply can need flags and be hidden by others, not the same one");

    // A check takes over where the reply went, and gives it back.
    check(editor.setCheck(0, 0, yh::DialogueCheck{"insight", 10, {}, "road"}) && editor.nodes()[0].choices[0].check->success == "yes"
        && editor.nodes()[0].choices[0].next.empty() && editor.dialogue(), "Adding a check moves the reply's next to its success");
    check(!editor.setNext(0, 0, "road"), "A checked reply has no next of its own");
    check(!editor.setCheck(0, 0, yh::DialogueCheck{"insight", -1, "yes", ""}) && !editor.setCheck(0, 0, yh::DialogueCheck{"", 10, "yes", ""})
        && !editor.setCheck(0, 0, yh::DialogueCheck{"insight", 10, "nowhere", ""}), "A check needs a skill, a fair difficulty and real nodes");
    check(editor.setCheck(0, 0, std::nullopt) && editor.nodes()[0].choices[0].next == "yes" && !editor.nodes()[0].choices[0].check, "Taking it off gives the success back");

    // A new node straight from a reply, as one step.
    const size_t before = history.size();
    const std::optional<size_t> branch = editor.branch(2, 0);
    check(branch == 3 && editor.nodes()[2].choices[0].next == editor.nodes()[3].id && history.size() == before + 1, "A reply can make the node it leads to");
    check(editor.branch(0, 2) == 4 && editor.nodes()[0].choices[2].check->failure == editor.nodes()[4].id, "A check's empty way gets it");
    history.undo();
    check(editor.nodes().size() == 4 && editor.nodes()[0].choices[2].check->failure.empty(), "Undo takes the node and the link back together");

    check(editor.removeChoice(2, 0) && editor.nodes()[2].choices.empty() && !editor.removeChoice(2, 0), "A reply comes off");
    check(editor.dialogue().has_value(), "Every step left a file the game reads");
}

void actions(const Check& check)
{
    DialogueEditor::Catalog catalog = tamCatalog();
    bool error = true;
    auto fine = [&](std::string_view action) { return DialogueEditor::actionProblem(action, catalog, &error).empty() && !error; };
    auto warning = [&](std::string_view action) { return !DialogueEditor::actionProblem(action, catalog, &error).empty() && !error; };
    auto wrong = [&](std::string_view action) { return !DialogueEditor::actionProblem(action, catalog, &error).empty() && error; };

    check(fine("recruit") && fine("dismiss") && fine("approve 5") && fine("approve -3") && fine("approve +2") && fine("approve wren -3"),
        "The companion actions as the game reads them");
    check(fine("release") && fine("kill") && fine("fight"), "And the others");
    check(wrong("approve") && wrong("approve lots") && wrong("approve wren") && wrong("recruit tam") && wrong("fight now"),
        "Ones the game would do nothing with are errors");
    check(warning("approve stranger 2") && warning("dance"), "A companion from elsewhere and an unknown action are only warnings");
    catalog.companion = false;
    check(warning("recruit") && warning("approve 2") && fine("approve tam 2"), "Recruit and a bare approve only work for someone who can join");
    const std::vector<std::string_view> all = DialogueEditor::actions();
    check(std::find(all.begin(), all.end(), "recruit") != all.end() && std::find(all.begin(), all.end(), "approve") != all.end(), "The list offers both");

    yh::History history;
    DialogueEditor editor(history);
    editor.load(tamJson, nullptr, catalog);
    yh::DialogueFlags flags;
    flags.actions = {"approve"};
    editor.setChoiceFlags(0, 2, flags);
    const std::vector<DialogueEditor::Problem> problems = editor.problems();
    check(std::any_of(problems.begin(), problems.end(), [](const DialogueEditor::Problem& p) { return p.error && p.text.find("hello/bye") == 0; }),
        "A broken action shows as an error, with where it is");
    check(std::any_of(problems.begin(), problems.end(), [](const DialogueEditor::Problem& p) { return !p.error && p.text.find("yes: recruit") == 0; }),
        "Recruit in a file nobody who can join uses is a warning");

    // The game plays what was written.
    editor.load(tamJson, nullptr, tamCatalog());
    flags.actions = {"approve 3"};
    flags.set = {"asked"};
    editor.setChoiceFlags(0, 0, flags);
    yh::DialogueSession session(*editor.dialogue());
    session.takeActions();
    session.choose("join");
    check(session.takeActions() == std::vector<std::string>{"approve 3", "recruit"} && session.flags().contains("asked") && session.flags().contains("tam_joined"),
        "A session over the edited file does the reply's actions, then the node's");
}

void problems(const Check& check)
{
    yh::History history;
    DialogueEditor editor(history);
    editor.load(tamJson, nullptr, tamCatalog());
    editor.addNode("lost");
    editor.setCheck(0, 1, yh::DialogueCheck{"juggling", 10, "road", ""});
    const std::vector<DialogueEditor::Problem> found = editor.problems();
    auto has = [&](std::string_view text) {
        return std::any_of(found.begin(), found.end(), [&](const DialogueEditor::Problem& p) { return !p.error && p.text.find(text) != std::string::npos; });
    };
    check(has("lost can't be reached") && has("lost has no line") && has("checks juggling"), "Unreachable nodes, empty lines and unknown skills are warnings");
    check(std::none_of(found.begin(), found.end(), [](const DialogueEditor::Problem& p) { return p.error; }), "None of them stops a save");
}

void inCreate(const Check& check, const std::filesystem::path& scratch)
{
    yh::Ui ui;
    yh::Input input;
    yh::Font* title = nullptr;
    CreateScreen screen(ui, input, title);
    screen.table.stateDir = (scratch / "dialogue-state").generic_string() + "/";
    screen.newPackage();
    check(screen.isOpen() && screen.dialogueFiles().empty() && !screen.dialogueEditor(), "A new package's chapter has no conversations");

    // A companion who talks through a file in the chapter's dialogue folder.
    const std::filesystem::path folder = std::filesystem::path(screen.packagePath()) / "chapters/chapter-one";
    json chapter = json::parse(*yh::readTextFile((folder / "chapter.json").string()));
    chapter["npcs"] = json::array({{{"id", "tam"}, {"name", "Tam"}, {"at", {4, 4}}, {"dialogue", "dialogue/tam.json"}, {"companion", json::object()}}});
    std::filesystem::create_directories(folder / "dialogue");
    yh::writeFileAtomically((folder / "chapter.json").string(), chapter.dump(2), false);
    yh::writeFileAtomically((folder / "dialogue/tam.json").string(), tamJson, false);
    screen.openPackage(screen.packagePath());
    const std::vector<std::string> files = screen.dialogueFiles();
    check(files == std::vector<std::string>{"chapters/chapter-one/dialogue/tam.json"}, "The chapter's conversations are listed");
    check(screen.openDialogue(files[0]) && screen.dialogueEditor()->catalog().companion && screen.dialogueEditor()->catalog().companions.contains("tam")
        && !screen.dialogueEditor()->catalog().skills.empty(), "Its file knows it belongs to someone who can join, and the ruleset's skills");

    const std::string untouched = *yh::readTextFile((folder / "dialogue/tam.json").string());
    check(screen.save() && *yh::readTextFile((folder / "dialogue/tam.json").string()) == untouched, "Saving with nothing changed leaves the file alone");

    DialogueEditor* tam = screen.dialogueEditor();
    tam->setText(1, "Fine, I'll come.");
    check(screen.history().dirty() && screen.save() && screen.status() == "Saved 1 file", "A changed line is saved");
    const std::optional<std::string> written = yh::readTextFile((folder / "dialogue/tam.json").string());
    const std::optional<yh::Dialogue> read = written ? yh::Dialogue::fromJson(*written) : std::nullopt;
    check(read && read->nodes[1].text == "Fine, I'll come." && json::parse(*written).value("mood", "") == "wary", "The file on disk has it, and keeps the rest");

    // A broken action keeps the file from being written.
    yh::DialogueFlags flags;
    flags.actions = {"approve"};
    tam->setNodeFlags(2, flags);
    check(!screen.save() && screen.status().find("not saved") != std::string::npos && *yh::readTextFile((folder / "dialogue/tam.json").string()) == *written,
        "A file with an action the game can't do isn't saved, and the status says why");
    screen.undo();

    // A new one goes in the chapter's dialogue folder at the next save.
    const std::string made = screen.newDialogue();
    check(made == "chapters/chapter-one/dialogue/conversation.json" && screen.dialogue() == made && screen.dialogueFiles().size() == 2,
        "New starts another conversation and opens it");
    DialogueEditor* fresh = screen.dialogueEditor();
    fresh->setText(0, "Hello.");
    const std::optional<size_t> reply = fresh->addChoice(0, "Bye.");
    check(reply && screen.save() && std::filesystem::exists(folder / "dialogue/conversation.json")
        && yh::Dialogue::fromJson(*yh::readTextFile((folder / "dialogue/conversation.json").string())), "Save writes it as a file the game reads");

    // The same history as the other modes.
    screen.undo();
    check(fresh->nodes()[0].choices.empty(), "Undo steps back through the new file's edits");
    screen.redo();

    // The game's own content opens too.
    screen.openPackage(YH_GAME_ASSETS);
    const std::vector<std::string> keep = screen.dialogueFiles();
    check(std::find(keep.begin(), keep.end(), "chapters/goblin-keep/dialogue/wren.json") != keep.end()
        && std::find(keep.begin(), keep.end(), "chapters/goblin-keep/dialogue/tobb.json") != keep.end(), "The keep's conversations are listed");
    for (const std::string& path : keep)
    {
        screen.openDialogue(path);
        DialogueEditor* editor = screen.dialogueEditor();
        const std::vector<DialogueEditor::Problem> wrong = editor ? editor->problems() : std::vector<DialogueEditor::Problem>{};
        const bool broken = std::any_of(wrong.begin(), wrong.end(), [](const DialogueEditor::Problem& p) { return p.error; });
        check(editor && !broken, (path + (editor ? " has nothing wrong" : " opens")).c_str());
    }
}

}

void dialogueEditorTests(const Check& check, const std::filesystem::path& scratch)
{
    loadAndSave(check);
    nodes(check);
    choices(check);
    actions(check);
    problems(check);
    inCreate(check, scratch);
}
