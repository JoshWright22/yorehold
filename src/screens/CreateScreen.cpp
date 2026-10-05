// Content package editor: Create screen with tabs, open/create, undo/redo, validation and export.

#include "CreateScreen.h"
#include "content/ContentPackage.h"
#include "content/GameMap.h"

#include <yorehold/framework/graphics/Renderer.h>
#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/rpg/Ruleset.h>
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
    compendium_.reset();
    compendiumFolders_.clear();
    compendiumError_.clear();
    compendiumSkipped_.clear();
    compendiumPanel_.reset();
    dialogues_.clear();
    dialogueErrors_.clear();
    dialogue_.clear();
    dialoguePanel_.reset();
    listedChapter_.clear();
    listed_.clear();
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

std::vector<std::string> CreateScreen::dialogueFiles()
{
    std::set<std::string> found;
    if (!package_ || chapter_.empty())
        return {};
    yh::FileSystem files;
    if (ContentPackage::mount(files, packagePath_, "package"))
    {
        for (const std::string& path : files.list(chapter_ + "/dialogue"))
            if (path.ends_with(".json"))
                found.insert(path);
        // What chapter.json names, looked for in the chapter folder first and then at the root, as the game does.
        auto named = [&](const nlohmann::json& entry, const char* key) {
            if (!entry.is_object() || !entry.contains(key) || !entry[key].is_string())
                return;
            const std::string path = entry[key].get<std::string>();
            if (path.empty() || yh::FileSystem::normalize(path) != path)
                return;
            const std::string local = chapter_ + "/" + path;
            if (files.exists(local))
                found.insert(local);
            else if (files.exists(path))
                found.insert(path);
        };
        if (const auto text = files.readText(chapter_ + "/chapter.json"))
            if (const nlohmann::json j = nlohmann::json::parse(*text, nullptr, false); j.is_object())
            {
                // Without its own, a chapter uses the package's dialogue/surrender.json if there is one.
                named(j.contains("surrender") ? j : nlohmann::json{{"surrender", "dialogue/surrender.json"}}, "surrender");
                named(j.value("winCondition", nlohmann::json::object()), "dialogue");
                for (const char* list : {"npcs", "triggers"})
                    for (const nlohmann::json& entry : j.value(list, nlohmann::json::array()))
                        named(entry, "dialogue");
                for (const nlohmann::json& e : j.value("encounters", nlohmann::json::array()))
                {
                    named(e, "surrender");
                    if (e.is_object())
                        for (const nlohmann::json& p : e.value("creatures", nlohmann::json::array()))
                            named(p, "surrender");
                }
            }
        for (const std::string& path : package_->dialogues)
            if (files.exists(path))
                found.insert(path);
    }
    // Made here and not saved yet.
    for (const auto& [path, tab] : dialogues_)
        if (tab->saved.empty() && path.starts_with(chapter_ + "/"))
            found.insert(path);
    return {found.begin(), found.end()};
}

DialogueEditor::Catalog CreateScreen::dialogueCatalog(const std::string& path)
{
    DialogueEditor::Catalog catalog;
    yh::FileSystem all;
    all.mountFolder(YH_FRAMEWORK_ASSETS, "framework");
    all.mountFolder(YH_GAME_ASSETS, "game");
    ContentPackage::mount(all, packagePath_, "package");
    const std::optional<std::string> text = all.readText(chapter_ + "/chapter.json");
    nlohmann::json chapter = text ? nlohmann::json::parse(*text, nullptr, false) : nlohmann::json();
    if (!chapter.is_object())
        chapter = nlohmann::json::object();

    // What a check can roll comes from the chapter's ruleset, picked the way the game picks it.
    // One that can't be read leaves "modern"; the package's own checks report it.
    const std::string defaultRuleset = all.exists("rulesets/yorehold/ruleset.json") ? "rulesets/yorehold" : "modern";
    const std::string named = chapter.value("ruleset", defaultRuleset);
    yh::Ruleset rules = named == "classic" ? yh::Ruleset::classic() : yh::Ruleset::modern();
    if (named != "classic" && named != "modern")
        for (const std::string& where : {chapter_ + "/" + named + "/ruleset.json", named + "/ruleset.json", chapter_ + "/" + named, named})
            if (all.exists(where))
            {
                if (const auto ruleText = all.readText(where))
                    if (std::optional<yh::Ruleset> read = yh::Ruleset::fromJson(*ruleText))
                        rules = std::move(*read);
                break;
            }
    for (const yh::SkillDefinition& skill : rules.skills)
        catalog.skills.push_back(skill.id);
    for (const yh::AbilityDefinition& ability : rules.abilities)
        catalog.skills.push_back(ability.id);

    // Who can join, and whether the one talking in this file is one of them.
    for (const nlohmann::json& npc : chapter.value("npcs", nlohmann::json::array()))
    {
        if (!npc.is_object() || !npc.contains("id") || !npc["id"].is_string())
            continue;
        if (!npc.contains("companion") && !npc.contains("approvalStart") && !npc.contains("approvalJoinThreshold"))
            continue;
        catalog.companions.insert(npc["id"].get<std::string>());
        const std::string file = npc.value("dialogue", std::string());
        if (!file.empty() && (path == chapter_ + "/" + file || path == file))
            catalog.companion = true;
    }
    return catalog;
}

bool CreateScreen::openDialogue(const std::string& path)
{
    if (path != dialogue_)
    {
        dialogue_ = path;
        dialoguePanel_.reset();
    }
    return dialogueEditor() != nullptr;
}

std::string CreateScreen::newDialogue()
{
    if (!package_ || chapter_.empty())
        return {};
    yh::FileSystem files;
    ContentPackage::mount(files, packagePath_, "package");
    std::string id = "conversation";
    for (int n = 2; files.exists(chapter_ + "/dialogue/" + id + ".json") || dialogues_.contains(chapter_ + "/dialogue/" + id + ".json"); n++)
        id = "conversation-" + std::to_string(n);
    const std::string path = chapter_ + "/dialogue/" + id + ".json";
    auto tab = std::make_unique<DialogueTab>(history_);
    tab->editor.create(id, dialogueCatalog(path));
    tab->path = path;
    dialogues_.emplace(path, std::move(tab));
    dialogueErrors_.erase(path);
    listedChapter_.clear();
    openDialogue(path);
    return path;
}

DialogueEditor* CreateScreen::dialogueEditor()
{
    if (!package_ || dialogue_.empty())
        return nullptr;
    if (const auto found = dialogues_.find(dialogue_); found != dialogues_.end())
        return &found->second->editor;
    if (dialogueErrors_.contains(dialogue_))
        return nullptr;
    yh::FileSystem files;
    if (!ContentPackage::mount(files, packagePath_, "package"))
    {
        dialogueErrors_[dialogue_] = "Couldn't open " + packagePath_;
        return nullptr;
    }
    const std::optional<std::string> text = files.readText(dialogue_);
    auto tab = std::make_unique<DialogueTab>(history_);
    std::string error;
    if (!text || !tab->editor.load(*text, &error, dialogueCatalog(dialogue_)))
    {
        dialogueErrors_[dialogue_] = dialogue_ + ": " + (text ? error : std::string("missing"));
        return nullptr;
    }
    tab->path = dialogue_;
    // Compared in the editor's own form, so a hand-written file isn't rewritten until it is changed.
    tab->saved = tab->editor.toJson();
    return &dialogues_.emplace(dialogue_, std::move(tab)).first->second->editor;
}

CompendiumEditor* CreateScreen::compendiumEditor()
{
    if (!package_)
        return nullptr;
    if (compendium_)
        return compendium_.get();
    if (!compendiumError_.empty())
        return nullptr;

    yh::FileSystem game;
    game.mountFolder(YH_GAME_ASSETS, "game");
    yh::FileSystem files;
    if (!ContentPackage::mount(files, packagePath_, "package"))
    {
        compendiumError_ = "Couldn't open " + packagePath_;
        return nullptr;
    }
    auto editor = std::make_unique<CompendiumEditor>(history_);
    const std::optional<std::string> forms = game.readText("create/compendium.json");
    std::string error;
    if (!forms || !editor->setKinds(*forms, &error))
    {
        compendiumError_ = "create/compendium.json: " + (forms ? error : std::string("missing"));
        return nullptr;
    }

    // Ruleset folders: the game's own place for one, and any a chapter names that has a ruleset.json.
    std::vector<std::string> rulesets;
    auto addRuleset = [&](const std::string& folder) {
        if (files.exists(folder + "/ruleset.json") && std::find(rulesets.begin(), rulesets.end(), folder) == rulesets.end())
            rulesets.push_back(folder);
    };
    addRuleset("rulesets/yorehold");
    for (const std::string& chapter : package_->chapters)
        if (const auto text = files.readText(chapter + "/chapter.json"))
            if (const nlohmann::json j = nlohmann::json::parse(*text, nullptr, false); j.is_object() && j.contains("ruleset") && j["ruleset"].is_string())
            {
                const std::string named = j["ruleset"].get<std::string>();
                if (!named.empty() && yh::FileSystem::normalize(named) == named)
                {
                    addRuleset(chapter + "/" + named);
                    addRuleset(named);
                }
            }

    // The files, from the package root, the chapters' own folders and the ruleset folders.
    compendiumSkipped_.clear();
    for (const CompendiumEditor::Kind& kind : editor->kinds())
    {
        std::vector<std::string> folders;
        if (kind.ruleset)
        {
            for (const std::string& ruleset : rulesets)
                folders.push_back(ruleset + "/" + kind.form.folder);
            if (!rulesets.empty())
                compendiumFolders_[kind.form.id] = rulesets.front() + "/" + kind.form.folder;
        }
        else
        {
            folders.push_back(kind.form.folder);
            for (const std::string& chapter : package_->chapters)
                folders.push_back(chapter + "/" + kind.form.folder);
            compendiumFolders_[kind.form.id] = kind.form.folder;
        }
        for (const std::string& folder : folders)
            for (const std::string& path : files.list(folder))
            {
                if (!path.ends_with(".json"))
                    continue;
                const std::optional<std::string> text = files.readText(path);
                if (!text || !editor->addFile(kind.form.id, path, *text, &error))
                    compendiumSkipped_.push_back(text ? error : path + ": can't be read");
            }
    }

    // What the forms offer: the ruleset's abilities and skills, the built-in AI profiles and the
    // game's own ids of each kind (the package's are added by the editor).
    yh::FormOptions options;
    std::optional<yh::Ruleset> rules;
    if (!rulesets.empty())
        if (const auto text = files.readText(rulesets.front() + "/ruleset.json"))
            rules = yh::Ruleset::fromJson(*text);
    if (!rules)
        if (const auto text = game.readText("rulesets/yorehold/ruleset.json"))
            rules = yh::Ruleset::fromJson(*text);
    if (rules)
    {
        for (const yh::AbilityDefinition& ability : rules->abilities)
            options["abilities"].push_back(ability.id);
        for (const yh::SkillDefinition& skill : rules->skills)
            options["skills"].push_back(skill.id);
    }
    for (const char* preset : {"mindless", "animal", "cunning", "tactical"})
        options["ai"].push_back(preset);
    for (const CompendiumEditor::Kind& kind : editor->kinds())
    {
        std::vector<std::string>& list = options[kind.form.folder];
        for (const std::string& path : game.list(kind.ruleset ? "rulesets/yorehold/" + kind.form.folder : kind.form.folder))
            if (path.ends_with(".json"))
            {
                const std::string id = leaf(path).substr(0, leaf(path).size() - 5);
                if (std::find(list.begin(), list.end(), id) == list.end())
                    list.push_back(id);
            }
    }
    editor->setOptions(std::move(options));
    compendium_ = std::move(editor);
    return compendium_.get();
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
    for (auto& [path, tab] : dialogues_)
    {
        std::string text = tab->editor.toJson();
        if (text == tab->saved)
            continue;
        for (const DialogueEditor::Problem& wrong : tab->editor.problems())
            if (wrong.error)
            {
                status_ = leaf(path) + " not saved: " + wrong.text;
                return false;
            }
        changed.push_back({tab->path, std::move(text), &tab->saved});
    }
    std::vector<size_t> definitions;
    if (compendium_)
        for (const size_t entry : compendium_->changed())
        {
            for (const CompendiumEditor::Problem& wrong : compendium_->problems(entry))
                if (wrong.error)
                {
                    status_ = compendium_->entries()[entry].path + " not saved: " + wrong.text;
                    return false;
                }
            definitions.push_back(entry);
        }
    int written = 0;
    for (File& file : changed)
    {
        // A new conversation may be the first thing in its folder.
        fs::create_directories((fs::path(packagePath_) / file.path).parent_path(), problem);
        if (!yh::writeFileAtomically((fs::path(packagePath_) / file.path).string(), file.text, false, &error))
        {
            status_ = file.path + " not saved: " + error;
            return false;
        }
        *file.saved = std::move(file.text);
        written++;
    }
    for (const size_t entry : definitions)
    {
        const std::string& path = compendium_->entries()[entry].path;
        fs::create_directories((fs::path(packagePath_) / path).parent_path(), problem);
        if (!yh::writeFileAtomically((fs::path(packagePath_) / path).string(), compendium_->toJson(entry), false, &error))
        {
            status_ = path + " not saved: " + error;
            return false;
        }
        compendium_->markSaved(entry);
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
    for (const auto& [path, why] : dialogueErrors_)
        validations_.push_back({path, why, true});
    for (const auto& [path, tab] : dialogues_)
        for (const DialogueEditor::Problem& problem : tab->editor.problems())
            validations_.push_back({path, leaf(path) + ": " + problem.text, problem.error});
    if (!compendiumError_.empty())
        validations_.push_back({"create/compendium.json", compendiumError_, true});
    for (const std::string& why : compendiumSkipped_)
        validations_.push_back({why, why, true});
    if (compendium_)
        for (size_t i = 0; i < compendium_->entries().size(); i++)
            for (const CompendiumEditor::Problem& problem : compendium_->problems(i))
                validations_.push_back({compendium_->entries()[i].path, compendium_->entries()[i].id + ": " + problem.text, problem.error});

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
    if (input_.shortcutDown() && !ui_.editing("map-marker") && !EncountersPanel::typing(ui_) && !DialoguePanel::typing(ui_) && !compendiumPanel_.typing(ui_))
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

    if (currentMode_ == Mode::Dialogue)
    {
        drawDialogue(renderer, area);
        return;
    }

    if (currentMode_ == Mode::Compendium)
    {
        if (CompendiumEditor* editor = compendiumEditor())
            compendiumPanel_.draw(*editor, compendiumFolders_, ui_, input_, renderer, area);
        else
            ui_.label({area.x + 20, area.y + 20}, compendiumError_.empty() ? std::string("Nothing to edit.") : compendiumError_, ui_.theme.bad);
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
        break;
    case Mode::Compendium:
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
    ui_.theme.font->draw(renderer, {area.x + 20, area.y + 80}, "Mode editors coming in G6-G7", yh::Color{100, 100, 100, 255});
}

void CreateScreen::drawDialogue(yh::Renderer& renderer, const yh::Rect& area)
{
    if (chapter_.empty())
    {
        ui_.label({area.x + 20, area.y + 20}, "This package has no chapter to write conversations for.", ui_.theme.textDim);
        return;
    }
    if (listedChapter_ != chapter_)
    {
        listed_ = dialogueFiles();
        listedChapter_ = chapter_;
        if (std::find(listed_.begin(), listed_.end(), dialogue_) == listed_.end())
            openDialogue(listed_.empty() ? std::string() : listed_.front());
    }

    // Which file: the arrows step through the chapter's, New starts another.
    const float bar = 40;
    renderer.fillRect({area.x, area.y, area.w, bar}, yh::Color{36, 37, 46, 255});
    const yh::Rect row{area.x + 8, area.y + 5, std::min(area.w - 120, 560.0f), 30};
    const bool back = ui_.button({row.x, row.y, 28, row.h}, "<", listed_.size() > 1);
    const bool on = ui_.button({row.x + row.w - 28, row.y, 28, row.h}, ">", listed_.size() > 1);
    if (back || on)
    {
        const auto at = std::find(listed_.begin(), listed_.end(), dialogue_);
        const size_t index = at == listed_.end() ? 0 : static_cast<size_t>(at - listed_.begin());
        openDialogue(listed_[(index + (back ? listed_.size() - 1 : 1)) % listed_.size()]);
    }
    const std::string shown = dialogue_.starts_with(chapter_ + "/") ? dialogue_.substr(chapter_.size() + 1) : dialogue_;
    ui_.label({row.x + 38, row.y + 5}, dialogue_.empty() ? std::string("No conversations yet") : shown,
        dialogue_.empty() ? ui_.theme.textDim : ui_.theme.text);
    if (!listed_.empty())
        ui_.label({row.x + row.w + 10, row.y + 5}, std::to_string(listed_.size()) + (listed_.size() == 1 ? " file" : " files"), ui_.theme.textDim);
    if (ui_.button({area.x + area.w - 98, row.y, 90, row.h}, "New"))
        newDialogue();

    const yh::Rect body{area.x, area.y + bar, area.w, area.h - bar};
    if (dialogue_.empty())
    {
        ui_.label({body.x + 20, body.y + 20}, "This chapter has no conversations. New starts one in its dialogue folder.", ui_.theme.textDim);
        return;
    }
    if (DialogueEditor* editor = dialogueEditor())
    {
        dialoguePanel_.draw(*editor, ui_, input_, renderer, body);
        return;
    }
    const auto why = dialogueErrors_.find(dialogue_);
    ui_.label({body.x + 20, body.y + 20}, why != dialogueErrors_.end() ? why->second : std::string("This file can't be read."), ui_.theme.bad);
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
