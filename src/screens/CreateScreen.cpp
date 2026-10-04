// Content package editor: Create screen with tabs, open/create, undo/redo, validation and export.

#include "CreateScreen.h"
#include "content/ContentPackage.h"

#include <yorehold/framework/graphics/Renderer.h>
#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/ui/Ui.h>

#include <nlohmann/json.hpp>
#include <SDL3/SDL_events.h>

#include <filesystem>
#include <fstream>
#include <set>
#include <array>

namespace fs = std::filesystem;

// PackageHistory implementation

void PackageHistory::push(std::string json, std::string description)
{
    // Truncate future if we're not at the end
    if (current_ < states_.size() - 1)
        states_.erase(states_.begin() + current_ + 1, states_.end());

    states_.push_back({std::move(json), std::move(description)});
    current_ = states_.size() - 1;
}

std::optional<std::string> PackageHistory::undo()
{
    if (!canUndo())
        return std::nullopt;
    current_--;
    return states_[current_].json;
}

std::optional<std::string> PackageHistory::redo()
{
    if (!canRedo())
        return std::nullopt;
    current_++;
    return states_[current_].json;
}

// CreateScreen implementation

CreateScreen::CreateScreen(yh::Ui& ui, yh::Input& input, yh::Font* const& title)
    : ui_(ui), input_(input), title_(title), currentMode_(Mode::Map)
{
}

void CreateScreen::openPackage(const std::string& path)
{
    if (path.empty())
        return;

    packagePath_ = path;
    loadPackageFromFile(path);
    savePackageState();
    validate();
}

void CreateScreen::newPackage()
{
    showNewDialog_ = true;
}

void CreateScreen::loadPackageFromFile(const std::string& path)
{
    try
    {
        // Try mounting as a folder or zip file
        yh::FileSystem files;
        if (!ContentPackage::mount(files, path, "package"))
        {
            validations_.push_back({path, "Failed to mount package (not a folder or .yore file)", true});
            return;
        }

        // Load the manifest
        auto package = ContentPackage::load(files);
        if (!package)
        {
            validations_.push_back({path, "Invalid content.json manifest", true});
            return;
        }

        package_ = package;
        packagePath_ = path;
        currentMode_ = Mode::Map; // Start at the first tab

        // Save to history
        auto j = nlohmann::json{
            {"name", package->name},
            {"kind", package->kind},
            {"id", package->id},
            {"revision", package->revision},
            {"ruleset", package->ruleset},
            {"requires", package->requires},
            {"chapters", package->chapters},
            {"defaultChapter", package->defaultChapter}
        };
        history_.clear();
        history_.push(j.dump(), "Opened package");

        validate();
    }
    catch (const std::exception& e)
    {
        validations_.push_back({path, std::string("Error loading package: ") + e.what(), true});
    }
}

void CreateScreen::createNewPackage()
{
    if (newPackageName_.empty())
        return;

    package_ = ContentPackage{
        .name = newPackageName_,
        .kind = newPackageKind_,
        .id = "", // will be set on export
        .revision = 0,
        .ruleset = "",
        .requires = {},
        .defaultChapter = "",
        .chapters = {}
    };

    packagePath_ = ""; // not yet saved
    currentMode_ = Mode::Map;
    showNewDialog_ = false;
    newPackageName_.clear();

    // Save initial state to history
    auto j = nlohmann::json{
        {"name", package_->name},
        {"kind", package_->kind},
        {"id", package_->id},
        {"revision", package_->revision},
        {"chapters", package_->chapters}
    };
    history_.clear();
    history_.push(j.dump(), "New package");

    validate();
}

void CreateScreen::savePackageState()
{
    if (!package_)
        return;

    auto j = nlohmann::json{
        {"name", package_->name},
        {"kind", package_->kind},
        {"id", package_->id},
        {"revision", package_->revision},
        {"ruleset", package_->ruleset},
        {"requires", package_->requires},
        {"chapters", package_->chapters},
        {"defaultChapter", package_->defaultChapter}
    };
    history_.push(j.dump(), "Edit");
}

void CreateScreen::validate()
{
    validations_.clear();

    if (!package_)
        return;

    // Basic validation
    if (package_->name.empty())
        validations_.push_back({"name", "Package name is empty", false});

    if (package_->name.length() > 80)
        validations_.push_back({"name", "Package name is longer than 80 characters", true});

    if (!package_->kind.empty())
    {
        const std::set<std::string> validKinds{"adventure", "ruleset", "compendium", "character_class", "race", "feat"};
        if (!validKinds.count(package_->kind))
            validations_.push_back({"kind", "Unknown package kind: " + package_->kind, true});
    }

    if (!package_->id.empty())
    {
        for (const char c : package_->id)
        {
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
            {
                validations_.push_back({"id", "ID contains invalid characters", true});
                break;
            }
        }
    }

    if (!package_->chapters.empty() && package_->defaultChapter.empty())
        validations_.push_back({"chapters", "defaultChapter must be set when chapters exist", true});

    // If chapters exist and we can validate them, do so
    if (packagePath_.empty())
        return; // Can't validate without a mounted file system

    try
    {
        yh::FileSystem files;
        if (!ContentPackage::mount(files, packagePath_, "package"))
            return;

        if (!package_->validate(files))
            validations_.push_back({packagePath_, "Package validation failed", true});
    }
    catch (const std::exception&)
    {
        // Silent fail for now
    }
}

bool CreateScreen::handle(const SDL_Event& event)
{
    // TODO: Handle keyboard shortcuts like Ctrl+Z for undo, Ctrl+S for save, etc.
    return false;
}

bool CreateScreen::update(double deltaSeconds)
{
    autoValidateTimer_ += deltaSeconds;
    if (autoValidateTimer_ >= autoValidateInterval_)
    {
        autoValidateTimer_ = 0;
        validate();
    }
    return true;
}

void CreateScreen::draw(yh::Renderer& renderer)
{
    if (!package_)
        return;

    const yh::Rect screen = renderer.bounds();

    // Divide into regions: tabs at top, toolbar at bottom, content in middle, validation list on right
    const float tabHeight = 40;
    const float toolbarHeight = 50;
    const float validationWidth = 300;

    yh::Rect tabArea{0, 0, screen.w, tabHeight};
    yh::Rect toolbarArea{0, screen.h - toolbarHeight, screen.w, toolbarHeight};
    yh::Rect contentArea{0, tabHeight, screen.w - validationWidth, screen.h - tabHeight - toolbarHeight};
    yh::Rect validationArea{screen.w - validationWidth, tabHeight, validationWidth, screen.h - tabHeight - toolbarHeight};

    // Draw background
    renderer.fillRect(screen, yh::Color{20, 20, 20, 255});

    // Draw regions
    drawTabs(renderer, tabArea);
    drawContent(renderer, contentArea);
    drawValidationList(renderer, validationArea);
    drawToolbar(renderer, toolbarArea);
}

void CreateScreen::drawTabs(yh::Renderer& renderer, const yh::Rect& area)
{
    renderer.fillRect(area, yh::Color{40, 40, 40, 255});

    // Tab names
    const std::array<std::pair<Mode, std::string_view>, 6> modes{{
        {Mode::Map, "Map"},
        {Mode::Encounters, "Encounters"},
        {Mode::Dialogue, "Dialogue"},
        {Mode::Compendium, "Compendium"},
        {Mode::Cutscene, "Cutscene"},
        {Mode::Story, "Story"}
    }};

    float x = 10;
    for (const auto& [mode, name] : modes)
    {
        const yh::Color color = (currentMode_ == mode) ? yh::Color{255, 214, 140, 255} : yh::Color{200, 200, 200, 255};
        if (ui_.theme.font)
            ui_.theme.font->draw(renderer, {x, area.y + 10}, std::string(name), color);
        x += 100;
    }
}

void CreateScreen::drawContent(yh::Renderer& renderer, const yh::Rect& area)
{
    renderer.fillRect(area, yh::Color{30, 30, 30, 255});

    if (!ui_.theme.font)
        return;

    // Draw placeholder for each mode
    std::string modeText;
    switch (currentMode_)
    {
    case Mode::Map:
        modeText = "Map Editor (tiles, walls, lights, markers, objects)";
        break;
    case Mode::Encounters:
        modeText = "Encounters Editor (creatures, groups, AI, loot)";
        break;
    case Mode::Dialogue:
        modeText = "Dialogue Editor (conversations, choices, triggers)";
        break;
    case Mode::Compendium:
        modeText = "Compendium Editor (classes, items, creatures, races)";
        break;
    case Mode::Cutscene:
        modeText = "Cutscene Editor (animations, camera, effects)";
        break;
    case Mode::Story:
        modeText = "Story Editor (chapters, transitions, flags, quests)";
        break;
    case Mode::None:
        modeText = "No mode selected";
        break;
    }

    const std::string subtitle = "Package: " + (package_->name.empty() ? "(unnamed)" : package_->name);
    ui_.theme.font->draw(renderer, {area.x + 20, area.y + 20}, subtitle, yh::Color{200, 200, 200, 255});
    ui_.theme.font->draw(renderer, {area.x + 20, area.y + 50}, modeText, yh::Color{150, 150, 150, 255});
    ui_.theme.font->draw(renderer, {area.x + 20, area.y + 80}, "Mode editors coming in G2-G7", yh::Color{100, 100, 100, 255});
}

void CreateScreen::drawValidationList(yh::Renderer& renderer, const yh::Rect& area)
{
    renderer.fillRect(area, yh::Color{25, 25, 25, 255});
    renderer.drawRect(area, yh::Color{80, 80, 80, 255}, 1);

    if (!ui_.theme.font)
        return;

    const int maxLines = (area.h - 40) / 20;
    ui_.theme.font->draw(renderer, {area.x + 10, area.y + 10}, "Validation", yh::Color{200, 200, 200, 255});

    float y = area.y + 30;
    int shown = 0;
    for (const auto& v : validations_)
    {
        if (shown >= maxLines)
            break;

        const yh::Color color = v.isError ? yh::Color{255, 100, 100, 255} : yh::Color{255, 200, 100, 255};
        const std::string line = "[" + std::string(v.isError ? "E" : "W") + "] " + v.message.substr(0, 30);
        ui_.theme.font->draw(renderer, {area.x + 10, y}, line, color);
        y += 20;
        shown++;
    }

    if (validations_.empty())
        ui_.theme.font->draw(renderer, {area.x + 10, y}, "No issues", yh::Color{100, 200, 100, 255});
    else if (shown < validations_.size())
    {
        ui_.theme.font->draw(renderer, {area.x + 10, y}, "... and more", yh::Color{150, 150, 150, 255});
    }
}

void CreateScreen::drawToolbar(yh::Renderer& renderer, const yh::Rect& area)
{
    renderer.fillRect(area, yh::Color{40, 40, 40, 255});

    ui_.begin(renderer, input_);

    // Buttons: Undo, Redo, New, Open, Save, Playtest, Export, Close
    float x = 10;
    const float h = 40;
    const float btnWidth = 80;
    const float gap = 5;

    if (ui_.button({x, area.y + 5, btnWidth, h}, "Undo", history_.canUndo()))
    {
        if (auto state = history_.undo())
        {
            // TODO: Load state from JSON
        }
    }
    x += btnWidth + gap;

    if (ui_.button({x, area.y + 5, btnWidth, h}, "Redo", history_.canRedo()))
    {
        if (auto state = history_.redo())
        {
            // TODO: Load state from JSON
        }
    }
    x += btnWidth + gap;

    if (ui_.button({x, area.y + 5, btnWidth, h}, "Playtest"))
    {
        if (table.playtest)
            table.playtest();
    }
    x += btnWidth + gap;

    if (ui_.button({x, area.y + 5, btnWidth, h}, "Export"))
    {
        if (table.export_package && !packagePath_.empty())
            table.export_package(packagePath_);
    }

    ui_.end();
}
