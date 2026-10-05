// Content package editor: Create screen with tabs, open/create, undo/redo, validation and export.

#include "CreateScreen.h"
#include "content/ContentPackage.h"
#include "content/GameMap.h"

#include <yorehold/framework/graphics/Renderer.h>
#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/save/SaveFile.h>
#include <yorehold/framework/ui/Ui.h>

#include <nlohmann/json.hpp>
#include <SDL3/SDL_events.h>

#include <filesystem>
#include <set>
#include <array>

namespace fs = std::filesystem;

namespace
{

// "chapters/goblin-keep" -> "goblin-keep"
std::string leaf(const std::string& folder)
{
    const size_t slash = folder.rfind('/');
    return slash == std::string::npos ? folder : folder.substr(slash + 1);
}

// Kit files in `folder`, by id. A broken one is left out: the package's own checks report it.
void readKits(const yh::FileSystem& files, const std::string& folder, GameMap::Kits& kits)
{
    for (const std::string& path : files.list(folder))
    {
        if (!path.ends_with(".json"))
            continue;
        const std::optional<std::string> text = files.readText(path);
        std::optional<yh::Kit> kit = text ? yh::Kit::fromJson(*text) : std::nullopt;
        if (kit)
            kits[leaf(path).substr(0, leaf(path).size() - 5)] = std::move(*kit);
    }
}

}

CreateScreen::CreateScreen(yh::Ui& ui, yh::Input& input, yh::Font* const& title)
    : ui_(ui), input_(input), title_(title), currentMode_(Mode::Map)
{
}

void CreateScreen::openPackage(const std::string& path)
{
    if (path.empty())
        return;

    // Undo steps point into the editors, so the history is emptied before they go.
    history_.clear();
    encounters_.clear();
    encounterErrors_.clear();
    maps_.clear();
    mapErrors_.clear();
    package_.reset();
    chapter_.clear();
    status_.clear();
    validations_.clear();
    packagePath_ = path;
    loadPackageFromFile(path);
}

void CreateScreen::newPackage()
{
    if (table.stateDir.empty())
    {
        status_ = "There is no folder to make a package in";
        return;
    }
    const fs::path root = fs::path(table.stateDir) / "create";
    std::string id = "new-adventure";
    std::error_code problem;
    for (int n = 2; fs::exists(root / id, problem); n++)
        id = "new-adventure-" + std::to_string(n);
    const fs::path folder = root / id;
    const std::string chapter = "chapters/chapter-one";
    fs::create_directories(folder / chapter, problem);

    const nlohmann::json manifest{
        {"format", "yorehold.content"}, {"version", 1}, {"name", "New adventure"}, {"kind", "adventure"}, {"id", id},
        {"revision", 0}, {"requires", nlohmann::json::array()}, {"defaultChapter", chapter},
        {"chapters", std::vector<std::string>{chapter}},
    };
    nlohmann::json hero{{"name", "Hero"}, {"class", "fighter"}, {"color", {220, 90, 80}}, {"at", {2, 2}}};
    const nlohmann::json chapterFile{
        {"id", "chapter-one"}, {"title", "Chapter one"}, {"map", "map.json"}, {"level", 1},
        {"party", std::vector<nlohmann::json>{std::move(hero)}},
        {"encounters", nlohmann::json::array()},
    };
    nlohmann::json map = nlohmann::json::parse(MapEditor::blankMap("Chapter one", 24, 16));
    map["markers"]["partyStart"] = {2, 2};

    std::string error;
    if (!yh::writeFileAtomically((folder / "content.json").string(), manifest.dump(2), false, &error)
        || !yh::writeFileAtomically((folder / chapter / "chapter.json").string(), chapterFile.dump(2), false, &error)
        || !yh::writeFileAtomically((folder / chapter / "map.json").string(), map.dump(2), false, &error))
    {
        status_ = "Couldn't make " + folder.generic_string() + ": " + error;
        return;
    }
    openPackage(folder.generic_string());
}

void CreateScreen::loadPackageFromFile(const std::string& path)
{
    try
    {
        // Try mounting as a folder or zip file
        yh::FileSystem files;
        if (!ContentPackage::mount(files, path, "package"))
        {
            status_ = "Couldn't open " + path + " (not a folder or .yore file)";
            validations_.push_back({path, status_, true});
            return;
        }

        // Load the manifest
        std::string error;
        auto package = ContentPackage::load(files, &error);
        if (!package)
        {
            status_ = error;
            validations_.push_back({path, error, true});
            return;
        }

        package_ = package;
        packagePath_ = path;
        chapter_ = !package->defaultChapter.empty() ? package->defaultChapter : package->chapters.empty() ? std::string() : package->chapters.front();
        currentMode_ = Mode::Map; // Start at the first tab
        validate();
    }
    catch (const std::exception& e)
    {
        status_ = std::string("Error loading package: ") + e.what();
        validations_.push_back({path, status_, true});
    }
}

void CreateScreen::selectChapter(const std::string& folder)
{
    if (package_ && std::find(package_->chapters.begin(), package_->chapters.end(), folder) != package_->chapters.end())
        chapter_ = folder;
}

CreateScreen::MapTab* CreateScreen::mapTab()
{
    if (!package_ || chapter_.empty())
        return nullptr;
    if (const auto found = maps_.find(chapter_); found != maps_.end())
        return found->second.get();
    if (mapErrors_.contains(chapter_))
        return nullptr;

    auto fail = [&](std::string why) {
        mapErrors_[chapter_] = std::move(why);
        return nullptr;
    };
    yh::FileSystem files;
    if (!ContentPackage::mount(files, packagePath_, "package"))
        return fail("Couldn't open " + packagePath_);

    // The game's own kits can be placed as well as the package's: the editor writes whole
    // objects into map.json, so the saved map doesn't need the kit files.
    GameMap::Kits kits;
    if (yh::FileSystem game; game.mountFolder(YH_GAME_ASSETS, "game"))
        readKits(game, "kits", kits);
    readKits(files, "kits", kits);
    readKits(files, chapter_ + "/kits", kits);

    // chapter.json names the map; resolved in the chapter folder first, then at the root, as the game does.
    std::string name = "map.json";
    if (const auto chapter = files.readText(chapter_ + "/chapter.json"))
        if (const nlohmann::json j = nlohmann::json::parse(*chapter, nullptr, false); j.is_object() && j.contains("map") && j["map"].is_string())
            name = j["map"].get<std::string>();
    if (name.empty() || name.front() == '/' || name.find("..") != std::string::npos || name.find(':') != std::string::npos)
        return fail(chapter_ + "/chapter.json: the map path has to stay inside the package");
    std::string path = chapter_ + "/" + name;
    if (!files.exists(path) && files.exists(name))
        path = name;
    const std::optional<std::string> text = files.readText(path);
    if (!text)
        return fail(path + " is missing");

    auto tab = std::make_unique<MapTab>(history_);
    std::string error;
    if (!tab->editor.load(*text, &error, std::move(kits)))
        return fail(path + ": " + error);
    tab->path = path;
    // Compared in the editor's own form, so a hand-written map isn't rewritten until it is changed.
    tab->saved = tab->editor.toJson();
    return maps_.emplace(chapter_, std::move(tab)).first->second.get();
}

MapEditor* CreateScreen::mapEditor()
{
    MapTab* tab = mapTab();
    return tab ? &tab->editor : nullptr;
}

CreateScreen::EncountersTab* CreateScreen::encountersTab()
{
    if (!package_ || chapter_.empty())
        return nullptr;
    if (const auto found = encounters_.find(chapter_); found != encounters_.end())
        return found->second.get();
    if (encounterErrors_.contains(chapter_))
        return nullptr;
    MapTab* map = mapTab();
    if (!map)
        return nullptr; // the map's own error says why

    auto fail = [&](std::string why) {
        encounterErrors_[chapter_] = std::move(why);
        return nullptr;
    };
    yh::FileSystem files;
    if (!ContentPackage::mount(files, packagePath_, "package"))
        return fail("Couldn't open " + packagePath_);
    const std::string path = chapter_ + "/chapter.json";
    const std::optional<std::string> text = files.readText(path);
    if (!text)
        return fail(path + " is missing");

    // What can be placed: the game's own creatures, AI profiles and items under the package's and
    // the chapter's, which is what a played package sees. A broken file only leaves itself out
    // here; the package's own checks report it.
    yh::FileSystem all;
    all.mountFolder(YH_FRAMEWORK_ASSETS, "framework");
    all.mountFolder(YH_GAME_ASSETS, "game");
    ContentPackage::mount(all, packagePath_, "package");
    yh::Compendium compendium;
    std::string problem;
    compendium.load(all, "", &problem);
    compendium.load(all, chapter_, &problem);

    auto tab = std::make_unique<EncountersTab>(history_);
    std::string error;
    // The map as it is being drawn, so a wall painted a moment ago already counts.
    MapEditor* walls = &map->editor;
    if (!tab->editor.load(*text, &error, EncountersEditor::Catalog::from(compendium), [walls](yh::Cell cell) { return walls->map().walkable(cell); }))
        return fail(path + ": " + error);
    tab->path = path;
    // Compared in the editor's own form, so a hand-written chapter isn't rewritten until it is changed.
    tab->saved = tab->editor.toJson();
    return encounters_.emplace(chapter_, std::move(tab)).first->second.get();
}

EncountersEditor* CreateScreen::encountersEditor()
{
    EncountersTab* tab = encountersTab();
    return tab ? &tab->editor : nullptr;
}

void CreateScreen::undo()
{
    const std::string label(history_.undoLabel());
    if (history_.undo())
        status_ = "Undid: " + label;
}

void CreateScreen::redo()
{
    const std::string label(history_.redoLabel());
    if (history_.redo())
        status_ = "Redid: " + label;
}

bool CreateScreen::save()
{
    if (!package_)
        return false;
    std::error_code problem;
    if (!fs::is_directory(packagePath_, problem))
    {
        status_ = "A .yore can't be saved into. Unpack it to a folder first.";
        return false;
    }
    // Everything is checked before anything is written, so a refused file doesn't leave the
    // others saved around it. Never write a map or a chapter the game would then refuse to load.
    struct File
    {
        std::string path, text;
        std::string* saved;
    };
    std::vector<File> changed;
    std::string error;
    for (auto& [chapter, tab] : maps_)
    {
        std::string text = tab->editor.toJson();
        if (text == tab->saved)
            continue;
        if (!GameMap::fromJson(text, &error))
        {
            status_ = tab->path + " not saved: " + error;
            return false;
        }
        changed.push_back({tab->path, std::move(text), &tab->saved});
    }
    for (auto& [chapter, tab] : encounters_)
    {
        std::string text = tab->editor.toJson();
        const auto map = maps_.find(chapter);
        const bool mapChanged = map != maps_.end() && map->second->editor.toJson() != map->second->saved;
        if (text == tab->saved && !mapChanged)
            continue;
        // A wall painted over a creature counts too, though only the map changed.
        for (const EncountersEditor::Problem& wrong : tab->editor.problems())
            if (wrong.error)
            {
                status_ = leaf(chapter) + " not saved: " + wrong.text;
                return false;
            }
        if (text != tab->saved)
            changed.push_back({tab->path, std::move(text), &tab->saved});
    }
    int written = 0;
    for (File& file : changed)
    {
        if (!yh::writeFileAtomically((fs::path(packagePath_) / file.path).string(), file.text, false, &error))
        {
            status_ = file.path + " not saved: " + error;
            return false;
        }
        *file.saved = std::move(file.text);
        written++;
    }
    history_.markSaved();
    status_ = written == 0 ? "Nothing to save" : written == 1 ? "Saved 1 file" : "Saved " + std::to_string(written) + " files";
    validate();
    return true;
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

    // The maps being edited, as they are now (saved or not).
    for (const auto& [chapter, why] : mapErrors_)
        validations_.push_back({chapter, why, true});
    for (auto& [chapter, tab] : maps_)
        for (const std::string& line : tab->editor.problems())
            validations_.push_back({tab->path, leaf(chapter) + ": " + line, false});
    for (const auto& [chapter, why] : encounterErrors_)
        validations_.push_back({chapter, why, true});
    for (const auto& [chapter, tab] : encounters_)
        for (const EncountersEditor::Problem& problem : tab->editor.problems())
            validations_.push_back({tab->path, leaf(chapter) + ": " + problem.text, problem.error});

    // If chapters exist and we can validate them, do so
    if (packagePath_.empty())
        return; // Can't validate without a mounted file system

    try
    {
        yh::FileSystem files;
        if (!ContentPackage::mount(files, packagePath_, "package"))
            return;

        // What is on disk, read by itself: a package has to carry everything it uses.
        std::string error;
        if (!package_->validate(files, &error))
            validations_.push_back({packagePath_, error.empty() ? std::string("Package validation failed") : error, true});
    }
    catch (const std::exception&)
    {
        // Silent fail for now
    }
}

bool CreateScreen::handle(const SDL_Event&)
{
    // The widgets and the map read the shared Input while drawing, so there is nothing to do here.
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

    ui_.begin(renderer, input_);
    // Ctrl+Z, Ctrl+Y (or Ctrl+Shift+Z) and Ctrl+S; a text box being typed in keeps its own.
    if (input_.shortcutDown() && !ui_.editing("map-marker") && !EncountersPanel::typing(ui_))
    {
        if (input_.keyPressed(SDLK_Z))
            input_.shiftDown() ? redo() : undo();
        else if (input_.keyPressed(SDLK_Y))
            redo();
        else if (input_.keyPressed(SDLK_S))
            save();
    }

    const yh::Rect screen = renderer.bounds();

    // Divide into regions: tabs at top, toolbar at bottom, content in middle, validation list on right
    const float tabHeight = 40;
    const float toolbarHeight = 50;
    const float validationWidth = 220;

    yh::Rect tabArea{0, 0, screen.w, tabHeight};
    yh::Rect toolbarArea{0, screen.h - toolbarHeight, screen.w, toolbarHeight};
    yh::Rect contentArea{0, tabHeight, screen.w - validationWidth, screen.h - tabHeight - toolbarHeight};
    yh::Rect validationArea{screen.w - validationWidth, tabHeight, validationWidth, screen.h - tabHeight - toolbarHeight};

    // Draw background
    renderer.fillRect(screen, yh::Color{20, 20, 20, 255});

    // Draw regions
    drawContent(renderer, contentArea);
    drawValidationList(renderer, validationArea);
    drawTabs(renderer, tabArea);
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

    float x = 8;
    for (const auto& [mode, name] : modes)
    {
        if (ui_.toggle({x, area.y + 5, 122, 30}, name, currentMode_ == mode))
            currentMode_ = mode;
        x += 126;
    }

    // The chapter being worked on; a click goes to the package's next one.
    const std::vector<std::string>& chapters = package_->chapters;
    if (!chapter_.empty())
    {
        const yh::Rect button{area.w - 268, area.y + 5, 260, 30};
        if (ui_.button(button, "Chapter: " + leaf(chapter_), chapters.size() > 1))
        {
            const auto current = std::find(chapters.begin(), chapters.end(), chapter_);
            chapter_ = current == chapters.end() || current + 1 == chapters.end() ? chapters.front() : *(current + 1);
        }
    }
}

void CreateScreen::drawContent(yh::Renderer& renderer, const yh::Rect& area)
{
    renderer.fillRect(area, yh::Color{30, 30, 30, 255});

    if (currentMode_ == Mode::Map)
    {
        if (MapTab* tab = mapTab())
        {
            tab->panel.draw(tab->editor, ui_, input_, renderer, area);
            return;
        }
        const auto why = mapErrors_.find(chapter_);
        ui_.label({area.x + 20, area.y + 20}, why != mapErrors_.end() ? why->second : std::string("This package has no chapter to draw a map for."),
            why != mapErrors_.end() ? ui_.theme.bad : ui_.theme.textDim);
        return;
    }

    if (currentMode_ == Mode::Encounters)
    {
        EncountersTab* tab = encountersTab();
        MapTab* map = mapTab();
        if (tab && map)
        {
            tab->panel.draw(tab->editor, map->editor.map(), ui_, input_, renderer, area);
            return;
        }
        // Creatures stand on the map, so a map that can't be read stops this mode too.
        auto why = encounterErrors_.find(chapter_);
        if (why == encounterErrors_.end() && (why = mapErrors_.find(chapter_)) == mapErrors_.end())
            ui_.label({area.x + 20, area.y + 20}, "This package has no chapter to place creatures in.", ui_.theme.textDim);
        else
            ui_.label({area.x + 20, area.y + 20}, why->second, ui_.theme.bad);
        return;
    }

    if (!ui_.theme.font)
        return;

    // Draw placeholder for each mode
    std::string modeText;
    switch (currentMode_)
    {
    case Mode::Map:
        break;
    case Mode::Encounters:
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
    ui_.theme.font->draw(renderer, {area.x + 20, area.y + 80}, "Mode editors coming in G4-G7", yh::Color{100, 100, 100, 255});
}

void CreateScreen::drawValidationList(yh::Renderer& renderer, const yh::Rect& area)
{
    renderer.fillRect(area, yh::Color{25, 25, 25, 255});
    renderer.drawRect(area, yh::Color{80, 80, 80, 255}, 1);

    if (!ui_.theme.font)
        return;

    yh::Font& font = *ui_.theme.font;
    font.draw(renderer, {area.x + 10, area.y + 10}, "Validation", yh::Color{200, 200, 200, 255});

    // Each message wraps to the panel; whatever doesn't fit below is counted.
    const float bottom = area.y + area.h - 26;
    float y = area.y + 36;
    size_t shown = 0;
    for (const auto& v : validations_)
    {
        std::vector<std::string> lines{std::string(v.isError ? "[E]" : "[W]")};
        size_t at = 0;
        while (at < v.message.size())
        {
            size_t end = v.message.find(' ', at);
            end = end == std::string::npos ? v.message.size() : end;
            std::string word = v.message.substr(at, end - at);
            const float width = area.w - 20;
            if (font.measure(lines.back() + " " + word) > width && lines.back().size() > 3)
                lines.push_back("   ");
            // A path longer than the panel is cut where it stops fitting.
            while (word.size() > 1 && font.measure(lines.back() + " " + word) > width)
            {
                size_t fit = word.size() - 1;
                while (fit > 1 && font.measure(lines.back() + " " + word.substr(0, fit)) > width)
                    fit--;
                lines.back() += " " + word.substr(0, fit);
                word.erase(0, fit);
                lines.push_back("   ");
            }
            lines.back() += " " + word;
            at = end + 1;
        }
        if (y + static_cast<float>(lines.size()) * 20 > bottom)
            break;
        const yh::Color color = v.isError ? yh::Color{255, 100, 100, 255} : yh::Color{255, 200, 100, 255};
        for (const std::string& line : lines)
        {
            font.draw(renderer, {area.x + 10, y}, line, color);
            y += 20;
        }
        y += 6;
        shown++;
    }

    if (validations_.empty())
        font.draw(renderer, {area.x + 10, y}, "No issues", yh::Color{100, 200, 100, 255});
    else if (shown < validations_.size())
        font.draw(renderer, {area.x + 10, y}, "... and " + std::to_string(validations_.size() - shown) + " more", yh::Color{150, 150, 150, 255});
}

void CreateScreen::drawToolbar(yh::Renderer& renderer, const yh::Rect& area)
{
    renderer.fillRect(area, yh::Color{40, 40, 40, 255});

    // Buttons: Undo, Redo, Save, Playtest, Export
    float x = 10;
    const float h = 40;
    const float btnWidth = 90;
    const float gap = 5;

    if (ui_.button({x, area.y + 5, btnWidth, h}, "Undo", history_.canUndo()))
        undo();
    x += btnWidth + gap;

    if (ui_.button({x, area.y + 5, btnWidth, h}, "Redo", history_.canRedo()))
        redo();
    x += btnWidth + gap;

    if (ui_.button({x, area.y + 5, btnWidth, h}, history_.dirty() ? "Save *" : "Save"))
        save();
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
    x += btnWidth + gap + 10;

    // What the next undo would take back, or what the last save or undo did.
    const std::string note = !status_.empty() ? status_ : history_.canUndo() ? "Last: " + std::string(history_.undoLabel()) : package_->name;
    ui_.label({x, area.y + 15}, note, ui_.theme.textDim);
}
