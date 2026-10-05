#pragma once

#include "content/ContentPackage.h"
#include "screens/CompendiumEditor.h"
#include "screens/CutsceneEditor.h"
#include "screens/DialogueEditor.h"
#include "screens/EncountersEditor.h"
#include "screens/MapEditor.h"

#include <yorehold/framework/editor/History.h>
#include <yorehold/framework/ui/Ui.h>
#include <yorehold/framework/input/ControlScheme.h>

#include <functional>
#include <map>
#include <memory>
#include <optional>
#include <string>
#include <vector>

union SDL_Event;

class World;

// Editing a content package: open/create, edit modes (Map, Encounters, Dialogue, Compendium, Cutscene, Story),
// shared undo/redo, validation list, playtest and export.
class CreateScreen
{
public:
    CreateScreen(yh::Ui& ui, yh::Input& input, yh::Font* const& title);

    // What the game passes each frame.
    struct Table
    {
        std::string stateDir; // where to save/load package paths
        std::function<void()> playtest; // open the package in a playtest session
        std::function<void(const std::string&)> export_package; // export to .yore file
    };
    Table table;

    void openPackage(const std::string& path);
    // Writes a new adventure folder (a manifest, one chapter, an empty map) under the state
    // directory's create/ and opens it.
    void newPackage();
    bool isOpen() const { return package_.has_value(); }
    // The folder or .yore that is open; empty when nothing is.
    const std::string& packagePath() const { return packagePath_; }

    // The chapter whose map and encounters are being edited.
    const std::string& chapter() const { return chapter_; }
    void selectChapter(const std::string& folder);
    // That chapter's map, loaded the first time it is asked for. Null if it can't be read.
    MapEditor* mapEditor();
    // That chapter's encounters, the same way. They stand on its map, so they need it too.
    EncountersEditor* encountersEditor();

    // The conversation files of that chapter: its dialogue/ folder, those its chapter.json names
    // and the package's declared ones, plus any made with newDialogue() and not saved yet.
    std::vector<std::string> dialogueFiles();
    // The file Dialogue mode shows; empty until one is picked or there is none.
    const std::string& dialogue() const { return dialogue_; }
    bool openDialogue(const std::string& path);
    // A new conversation in the chapter's dialogue/ folder, open and written at the next save.
    std::string newDialogue();
    // The open file's conversation, loaded the first time it is asked for. Null if it can't be read.
    DialogueEditor* dialogueEditor();

    // The package's definition files (its items, creatures, classes, AI profiles, kits, and the
    // spells, races, backgrounds and feats of its ruleset folders, chapters' own ones too), read
    // the first time it is asked for. Null if the forms file can't be read.
    CompendiumEditor* compendiumEditor();
    // Where Add puts a new entry of each kind; a ruleset kind has none without a ruleset folder.
    const std::map<std::string, std::string>& compendiumFolders() const { return compendiumFolders_; }

    // The cutscene files of the chapter: its cutscenes/ folder, those its chapter.json names and
    // the package's declared ones, plus any made with newCutscene() and not saved yet.
    std::vector<std::string> cutsceneFiles();
    // The file Cutscene mode shows; empty until one is picked or there is none.
    const std::string& cutscene() const { return cutscene_; }
    bool openCutscene(const std::string& path);
    // A new cutscene in the chapter's cutscenes/ folder, open and written at the next save.
    std::string newCutscene();
    // The open file, loaded the first time it is asked for. Null if it can't be read.
    CutsceneEditor* cutsceneEditor();
    // Where the chapter plays its cutscenes, from its chapter.json. Null if that can't be read.
    CutsceneHooks* cutsceneHooks();

    // One history for the whole package: every mode's edits go on it.
    yh::History& history() { return history_; }
    void undo();
    void redo();
    // Writes every changed map, chapter, conversation, cutscene and definition back to its file. False (and `status()` says why) if
    // the package is a .yore, which can't be written to, or a file would no longer load.
    bool save();
    const std::string& status() const { return status_; }

    bool handle(const SDL_Event& event);
    bool update(double deltaSeconds);
    void draw(yh::Renderer& renderer);

private:
    enum class Mode { None, Map, Encounters, Dialogue, Compendium, Cutscene, Story };

    struct Validation
    {
        std::string path;
        std::string message;
        bool isError = true;
    };

    // One chapter's map and the layout's own state for it (tool, view, selection).
    struct MapTab
    {
        explicit MapTab(yh::History& history) : editor(history) {}
        MapEditor editor;
        MapEditorPanel panel;
        std::string path;  // inside the package: "chapters/keep/map.json"
        std::string saved; // the JSON as last read or written
    };

    // One chapter's encounters and the layout's own state for them.
    struct EncountersTab
    {
        explicit EncountersTab(yh::History& history) : editor(history) {}
        EncountersEditor editor;
        EncountersPanel panel;
        std::string path;  // "chapters/keep/chapter.json"
        std::string saved; // the JSON as last read or written, in the editor's form
    };

    // One conversation file.
    struct DialogueTab
    {
        explicit DialogueTab(yh::History& history) : editor(history) {}
        DialogueEditor editor;
        std::string path;  // "chapters/keep/dialogue/wren.json"
        std::string saved; // the JSON as last read or written, in the editor's form; empty = not on disk yet
    };

    // One cutscene file.
    struct CutsceneTab
    {
        explicit CutsceneTab(yh::History& history) : editor(history) {}
        CutsceneEditor editor;
        std::string path;  // "chapters/keep/cutscenes/intro.json"
        std::string saved; // the JSON as last read or written, in the editor's form; empty = not on disk yet
    };

    void loadPackageFromFile(const std::string& path);
    void drawCutscene(yh::Renderer& renderer, const yh::Rect& area);
    DialogueEditor::Catalog dialogueCatalog(const std::string& path);
    void drawDialogue(yh::Renderer& renderer, const yh::Rect& area);
    void validate();
    void drawTabs(yh::Renderer& renderer, const yh::Rect& area);
    void drawContent(yh::Renderer& renderer, const yh::Rect& area);
    void drawValidationList(yh::Renderer& renderer, const yh::Rect& area);
    void drawToolbar(yh::Renderer& renderer, const yh::Rect& area);
    MapTab* mapTab();
    EncountersTab* encountersTab();

    yh::Ui& ui_;
    yh::Input& input_;
    yh::Font* const& title_;

    std::optional<ContentPackage> package_;
    std::string packagePath_; // file path or folder path of opened package
    std::string chapter_;     // chapter folder inside the package
    // Its undo steps point into the editors below: empty it before dropping any of them.
    yh::History history_;
    std::map<std::string, std::unique_ptr<MapTab>> maps_; // by chapter folder
    std::map<std::string, std::string> mapErrors_;        // chapters whose map couldn't be read, and why
    // The encounter editors ask the map editors where someone can stand: drop these first.
    std::map<std::string, std::unique_ptr<EncountersTab>> encounters_;
    std::map<std::string, std::string> encounterErrors_;
    std::map<std::string, std::unique_ptr<DialogueTab>> dialogues_; // by path, from any chapter
    std::map<std::string, std::string> dialogueErrors_;
    std::string dialogue_;
    DialoguePanel dialoguePanel_;
    std::unique_ptr<CompendiumEditor> compendium_;
    std::map<std::string, std::string> compendiumFolders_; // kind -> folder in the package
    std::string compendiumError_;
    std::vector<std::string> compendiumSkipped_; // files that couldn't be opened, and why
    CompendiumPanel compendiumPanel_;
    std::map<std::string, std::unique_ptr<CutsceneTab>> cutscenes_; // by path, from any chapter
    std::map<std::string, std::string> cutsceneErrors_;
    std::string cutscene_;
    CutscenePanel cutscenePanel_;
    std::map<std::string, std::unique_ptr<CutsceneHooks>> hooks_; // by chapter folder
    std::map<std::string, std::string> hookErrors_;
    std::string listedCutsceneChapter_;
    std::vector<std::string> listedCutscenes_;
    std::string listedChapter_;          // the chapter `listed_` is for; empty = list again
    std::vector<std::string> listed_;     // its dialogueFiles(), so the folder isn't read every frame
    Mode currentMode_ = Mode::None;
    std::string status_; // the last save or load, shown in the toolbar

    std::vector<Validation> validations_;
    double autoValidateTimer_ = 0;
    const double autoValidateInterval_ = 2.0; // validate every 2 seconds
};
