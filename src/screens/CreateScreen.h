#pragma once

#include "content/ContentPackage.h"
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

    // The chapter whose map (and, later, encounters) is being edited.
    const std::string& chapter() const { return chapter_; }
    void selectChapter(const std::string& folder);
    // That chapter's map, loaded the first time it is asked for. Null if it can't be read.
    MapEditor* mapEditor();

    // One history for the whole package: every mode's edits go on it.
    yh::History& history() { return history_; }
    void undo();
    void redo();
    // Writes every changed map back to its map.json. False (and `status()` says why) if the
    // package is a .yore, which can't be written to, or a map would no longer load.
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

    void loadPackageFromFile(const std::string& path);
    void validate();
    void drawTabs(yh::Renderer& renderer, const yh::Rect& area);
    void drawContent(yh::Renderer& renderer, const yh::Rect& area);
    void drawValidationList(yh::Renderer& renderer, const yh::Rect& area);
    void drawToolbar(yh::Renderer& renderer, const yh::Rect& area);
    MapTab* mapTab();

    yh::Ui& ui_;
    yh::Input& input_;
    yh::Font* const& title_;

    std::optional<ContentPackage> package_;
    std::string packagePath_; // file path or folder path of opened package
    std::string chapter_;     // chapter folder inside the package
    // Its undo steps point into the map editors below: empty it before dropping any of them.
    yh::History history_;
    std::map<std::string, std::unique_ptr<MapTab>> maps_; // by chapter folder
    std::map<std::string, std::string> mapErrors_;        // chapters whose map couldn't be read, and why
    Mode currentMode_ = Mode::None;
    std::string status_; // the last save or load, shown in the toolbar

    std::vector<Validation> validations_;
    double autoValidateTimer_ = 0;
    const double autoValidateInterval_ = 2.0; // validate every 2 seconds
};
