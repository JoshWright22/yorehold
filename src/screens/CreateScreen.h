#pragma once

#include "content/ContentPackage.h"
#include <yorehold/framework/ui/Ui.h>
#include <yorehold/framework/input/ControlScheme.h>

#include <functional>
#include <memory>
#include <optional>
#include <string>
#include <vector>

union SDL_Event;

class World;

// Shared undo/redo history for a content package.
class PackageHistory
{
public:
    struct State
    {
        std::string json; // serialized package state
        std::string description;
    };

    void clear() { states_.clear(); current_ = 0; }
    void push(std::string json, std::string description);
    bool canUndo() const { return current_ > 0; }
    bool canRedo() const { return current_ < states_.size() - 1; }
    std::optional<std::string> undo();
    std::optional<std::string> redo();

private:
    std::vector<State> states_;
    size_t current_ = 0;
};

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
    void newPackage();
    bool isOpen() const { return package_.has_value(); }

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

    void loadPackageFromFile(const std::string& path);
    void createNewPackage();
    void savePackageState();
    void validate();
    void drawTabs(yh::Renderer& renderer, const yh::Rect& area);
    void drawContent(yh::Renderer& renderer, const yh::Rect& area);
    void drawValidationList(yh::Renderer& renderer, const yh::Rect& area);
    void drawToolbar(yh::Renderer& renderer, const yh::Rect& area);

    yh::Ui& ui_;
    yh::Input& input_;
    yh::Font* const& title_;

    std::optional<ContentPackage> package_;
    std::string packagePath_; // file path or folder path of opened package
    PackageHistory history_;
    Mode currentMode_ = Mode::None;

    std::vector<Validation> validations_;
    double autoValidateTimer_ = 0;
    const double autoValidateInterval_ = 2.0; // validate every 2 seconds

    // Dialog for opening/creating
    bool showNewDialog_ = false;
    std::string newPackageName_;
    std::string newPackageKind_ = "adventure"; // default kind
};
