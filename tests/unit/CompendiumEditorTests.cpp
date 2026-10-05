// Compendium mode of the Create screen: forms from create/compendium.json, its commands and undo,
// what it writes, and the game's own definitions opening in it untouched.

#include "screens/CompendiumEditor.h"
#include "screens/CreateScreen.h"

#include <yorehold/framework/rpg/Compendium.h>
#include <yorehold/framework/save/SaveFile.h>

#include <nlohmann/json.hpp>

#include <algorithm>
#include <filesystem>
#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;
using nlohmann::json;

// Written by hand, with a field no form lists.
const char* ropeJson = R"({
  "id": "rope",
  "name": "Rope",
  "notes": "fifty feet",
  "weight": 10,
  "value": 100
})";

std::string forms()
{
    const std::optional<std::string> text = yh::readTextFile(std::string(YH_GAME_ASSETS) + "/create/compendium.json");
    return text ? *text : std::string();
}

bool hasError(const std::vector<CompendiumEditor::Problem>& problems)
{
    return std::any_of(problems.begin(), problems.end(), [](const CompendiumEditor::Problem& p) { return p.error; });
}

void loading(const Check& check)
{
    yh::History history;
    CompendiumEditor editor(history);
    std::string error;
    check(editor.setKinds(forms(), &error) && editor.kinds().size() == 9 && editor.kind("item") && editor.kind("spell")->ruleset,
        "The game's forms file lists every kind");
    check(!editor.setKinds(R"({"kinds": [{"id": "item", "folder": "items", "fields": [{"key": "name", "type": "colour"}]}]})", &error)
        && error.find("colour") != std::string::npos, "A forms file with an unknown field type is refused, saying which");
    editor.setKinds(forms());

    check(editor.addFile("item", "items/rope.json", ropeJson, &error) && editor.of("item").size() == 1, "A hand-written item opens");
    check(editor.changed().empty() && editor.problems(0).empty(), "It isn't rewritten until something changes, and nothing is wrong with it");
    check(!editor.addFile("item", "items/broken.json", "[1, 2]", &error) && error.find("broken") != std::string::npos, "A file that isn't an object is left out");
    check(!editor.addFile("dragon", "dragons/red.json", "{}", &error), "A kind with no form is left out");
}

void editing(const Check& check)
{
    yh::History history;
    CompendiumEditor editor(history);
    editor.setKinds(forms());
    editor.setOptions({{"abilities", {"str", "dex"}}, {"items", {"longsword"}}});
    editor.addFile("item", "items/rope.json", ropeJson);

    std::string error;
    check(editor.setField(0, "value", "250") && editor.entries()[0].value["value"] == 250, "A number typed in a box is set");
    check(!editor.setField(0, "weight", "-1", &error) && error.find("at least 0") != std::string::npos && editor.entries()[0].value["weight"] == 10,
        "A number out of range is refused, saying why");
    check(!editor.setField(0, "value", "lots", &error) && editor.entries()[0].value["value"] == 250, "Text in a number box is refused");
    check(editor.setField(0, "slot", "offHand") && editor.setField(0, "magic", "true") && editor.setField(0, "modifiers", R"([{"stat": "armorClass", "value": 1}])"),
        "Choices, flags and nested JSON are set");
    check(editor.setField(0, "weight", "") && !editor.entries()[0].value.contains("weight"), "A blank box leaves the field out");
    check(!editor.setField(0, "id", "cord") && !editor.setField(0, "colour", "red"), "The id follows the file name, and a field no form lists can't be set");

    const json written = json::parse(editor.toJson(0));
    check(written.value("notes", "") == "fifty feet" && written.value("slot", "") == "offHand", "Fields the form doesn't list are written back");
    check(editor.changed() == std::vector<size_t>{0} && !hasError(editor.problems(0)), "The changed item is what the next save writes");
    std::string text = editor.toJson(0);
    check(text.find("\"id\"") < text.find("\"notes\"") && text.find("\"notes\"") < text.find("\"value\""), "Fields stay in the order the file had them");

    // One box typed into is one undo step.
    editor.endTyping();
    editor.setField(0, "name", "Ro");
    editor.setField(0, "name", "Rop");
    editor.setField(0, "name", "Rope ladder");
    editor.endTyping();
    history.undo();
    check(editor.entries()[0].value["name"] == "Rope", "Undo takes back the whole typing at once");
    history.redo();
    check(editor.entries()[0].value["name"] == "Rope ladder", "Redo puts it back");

    // The game's reader has the last word.
    editor.setField(0, "damage", "zz");
    const std::vector<CompendiumEditor::Problem> bad = editor.problems(0);
    check(hasError(bad), "Dice the game can't read are an error");
    editor.setField(0, "damage", "1d4");
    editor.setField(0, "attackAbility", "luck");
    const std::vector<CompendiumEditor::Problem> odd = editor.problems(0);
    check(!odd.empty() && !hasError(odd), "A name the lists don't offer is only a warning");
}

void adding(const Check& check)
{
    yh::History history;
    CompendiumEditor editor(history);
    editor.setKinds(forms());
    editor.addFile("item", "items/rope.json", ropeJson);

    const std::optional<size_t> made = editor.add("item", "items");
    check(made && editor.entries()[*made].id == "new-item" && editor.entries()[*made].path == "items/new-item.json"
        && editor.entries()[*made].value.value("name", "") == "New item", "Add makes an item from the form's defaults");
    check(editor.add("item", "items") && editor.of("item").size() == 3 && editor.entries().back().id == "new-item-2", "A second one gets the next free id");
    check(!editor.add("item", "items", "rope") && !editor.add("item", "items", "Big Rope") && !editor.add("item", ""),
        "A taken id, a bad id or no folder adds nothing");
    const std::optional<size_t> copied = editor.copy(0);
    check(copied && editor.entries()[*copied].id == "rope-copy" && editor.entries()[*copied].value["id"] == "rope-copy"
        && editor.entries()[*copied].value.value("notes", "") == "fifty feet", "Copy makes another under a new id");
    check(editor.options()["items"].size() == 4, "New entries are offered to the other forms at once");
    history.undo();
    history.undo();
    check(editor.of("item").size() == 2, "Undo takes added entries away again");

    // Every kind's new entry is one the game would load.
    editor.setOptions({{"ai", {"cunning"}}});
    for (const CompendiumEditor::Kind& kind : editor.kinds())
    {
        const std::optional<size_t> fresh = editor.add(kind.form.id, kind.form.folder);
        const std::vector<CompendiumEditor::Problem> problems = fresh ? editor.problems(*fresh) : std::vector<CompendiumEditor::Problem>{};
        check(fresh && problems.empty(), ("A new " + kind.form.id + " has nothing wrong" + (problems.empty() ? "" : ": " + problems[0].text)).c_str());
    }
}

void inCreate(const Check& check, const std::filesystem::path& scratch)
{
    yh::Ui ui;
    yh::Input input;
    yh::Font* title = nullptr;
    CreateScreen screen(ui, input, title);
    screen.table.stateDir = (scratch / "compendium-state").generic_string() + "/";
    screen.newPackage();
    CompendiumEditor* editor = screen.compendiumEditor();
    check(editor && editor->entries().empty(), "A new package has no definitions of its own");
    check(screen.compendiumFolders().at("item") == "items" && !screen.compendiumFolders().contains("spell"),
        "Items go at the root; spells need a ruleset folder the new package doesn't have");
    check(editor->options().contains("items") && !editor->options().at("items").empty() && !editor->options().at("abilities").empty(),
        "The forms offer the game's own items and the ruleset's abilities");

    const std::optional<size_t> rope = editor->add("item", "items", "rope");
    editor->setField(*rope, "name", "Rope");
    editor->setField(*rope, "weight", "10");
    check(screen.history().dirty() && screen.save() && screen.status() == "Saved 1 file", "A new item is saved");
    const std::filesystem::path file = std::filesystem::path(screen.packagePath()) / "items/rope.json";
    const std::optional<std::string> written = yh::readTextFile(file.string());
    const std::optional<yh::Item> read = written ? yh::Compendium::itemFromJson(*written) : std::nullopt;
    check(read && read->name == "Rope" && read->weight == 10, "The file on disk is one the game reads");
    check(screen.save() && screen.status() == "Nothing to save", "Saving again writes nothing");

    // Something the game would refuse isn't written.
    editor->setField(*rope, "damage", "zz");
    check(!screen.save() && screen.status().find("not saved") != std::string::npos && *yh::readTextFile(file.string()) == *written,
        "An item the game can't read isn't saved, and the status says why");
    screen.undo();
    check(json::parse(editor->toJson(*rope)).value("damage", "") == "", "Undo is the same history as the other modes");

    // Opened again, it is read from the folder.
    screen.openPackage(screen.packagePath());
    editor = screen.compendiumEditor();
    check(editor && editor->of("item").size() == 1 && editor->changed().empty(), "Reopened, the saved item is there and unchanged");

    // The game's own content opens with nothing wrong and nothing to rewrite.
    screen.openPackage(YH_GAME_ASSETS);
    editor = screen.compendiumEditor();
    check(editor && editor->of("item").size() >= 16 && editor->of("spell").size() >= 20 && editor->of("kit").size() == 6 && editor->of("ai").size() >= 10,
        "The game's items, spells, kits and AI profiles are listed");
    check(screen.compendiumFolders().at("spell") == "rulesets/yorehold/spells", "Its new spells go in its ruleset folder");
    for (size_t i = 0; editor && i < editor->entries().size(); i++)
    {
        const std::vector<CompendiumEditor::Problem> problems = editor->problems(i);
        check(!hasError(problems), (editor->entries()[i].path + (problems.empty() ? std::string() : ": " + problems[0].text)).c_str());
    }
    check(editor && editor->changed().empty(), "Opening the game's files rewrites none of them");
}

}

void compendiumEditorTests(const Check& check, const std::filesystem::path& scratch)
{
    loading(check);
    editing(check);
    adding(check);
    inCreate(check, scratch);
}
