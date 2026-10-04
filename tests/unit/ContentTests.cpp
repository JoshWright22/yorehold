#include "YoreholdGame.h"
#include "content/Chapter.h"
#include "content/ContentPackage.h"
#include "sim/World.h"

#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/map/Pathfinding.h>

#include <nlohmann/json.hpp>
#include <SDL3/SDL.h>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <functional>
#include <set>
#include <span>

namespace
{

namespace fs = std::filesystem;
using nlohmann::json;
int checks = 0;
int failures = 0;

void check(bool passed, const char* description)
{
    ++checks;
    if (passed) return;
    ++failures;
    std::fprintf(stderr, "FAIL: %s\n", description);
}

void write(const fs::path& path, const json& data)
{
    fs::create_directories(path.parent_path());
    std::ofstream file(path, std::ios::binary);
    file << data.dump(2);
    if (!file) throw std::runtime_error("couldn't write fixture " + path.string());
}

void worldSaveTests()
{
    yh::FileSystem files;
    files.mountFolder(YH_GAME_ASSETS, "game");
    struct TestWorld : World
    {
        using World::World;
        bool load()
        {
            auto chapter = Chapter::load(chapterFiles_, "chapters/goblin-keep");
            if (!chapter) return false;
            chapter_ = std::make_unique<Chapter>(std::move(*chapter));
            rules_ = chapter_->rules;
            newAdventure(7);
            return true;
        }
    };
    TestWorld world(files);
    check(world.load(), "A world starts the keep without a window");
    const json before = json::parse(world.stateJson());
    world.setFlags({"test-flag"});
    std::string error;
    check(world.restoreState(before.dump(), &error), "A world restores its own saved state");
    check(json::parse(world.stateJson()) == before, "World save round trips preserve every field");
    json broken = before;
    broken["creatures"][0]["x"] = -1;
    check(!world.restoreState(broken.dump(), &error) && error == "saved token is outside the map",
        "A world rejects invalid saved positions with a clear error");
    check(json::parse(world.stateJson()) == before, "A rejected save leaves the world intact");
    check(!world.takeEvents().empty() && world.takeEvents().empty(), "World events are consumed once");
}

struct Scratch
{
    fs::path path = fs::temp_directory_path() / ("yorehold-content-tests-"
        + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
    Scratch() { fs::create_directories(path); }
    ~Scratch() { std::error_code error; fs::remove_all(path, error); }
};

void contentTests(const fs::path& scratch)
{
    yh::FileSystem files;
    check(files.mountFolder(YH_GAME_ASSETS, "game"), "Mount authored game files");
    std::string error;
    auto content = ContentPackage::load(files, &error);
    check(content && content->validate(files, &error), "The whole shipped content package validates");
    if (!content) return;
    auto chapter = Chapter::load(files, content->defaultChapter, &error);
    check(chapter.has_value(), "Load the keep from chapter files");
    if (!chapter) { std::fprintf(stderr, "%s\n", error.c_str()); return; }
    check(chapter->party.size() == 4 && chapter->encounters.size() == 3, "The authored keep preserves its party and encounters");
    check(chapter->map.width() == 48 && chapter->map.height() == 30, "Map size comes from the rows");
    check(!chapter->clearedCutscene.empty() && files.exists(chapter->clearedCutscene), "The chapter resolves its own ending file");
    check(chapter->clearedText == "The goblins are gone. The keep is yours!", "The writer owns the completion text");
    check(chapter->stealth.checkEvery == 5 && chapter->encounters[0].creatures[0].facing == 180.0f && !chapter->encounters[0].creatures[1].facing,
        "Stealth rules and where enemies look come from files");
    {
        // Player options: four races, six backgrounds, feats of every kind; each pairing builds
        // for every class, and every feat can be taken by someone.
        const yh::Compendium& options = chapter->compendium;
        check(options.races.size() == 4 && options.race("human") && options.race("elf") && options.race("dwarf") && options.race("halfling"),
            "The ruleset ships human, elf, dwarf and halfling");
        check(options.backgrounds.size() == 6, "The ruleset ships six backgrounds");
        std::set<std::string> kinds;
        for (const auto& [id, feat] : options.feats) kinds.insert(feat.kind);
        check(kinds == std::set<std::string>{"class", "skill", "general", "race"}, "The ruleset ships feats of every kind");
        bool builds = true;
        std::string problem;
        for (const auto& [classId, definition] : options.classes)
            for (const auto& [raceId, race] : options.races)
                for (const auto& [backgroundId, background] : options.backgrounds)
                {
                    yh::CharacterChoices made;
                    made.name = "Test";
                    made.race = raceId;
                    made.background = backgroundId;
                    for (const auto& ability : chapter->rules.abilities) made.scores[ability.id] = 14;
                    made.levels.push_back({classId, {}});
                    if (!options.build(chapter->rules, made, &problem))
                    {
                        builds = false;
                        std::fprintf(stderr, "%s %s %s: %s\n", classId.c_str(), raceId.c_str(), backgroundId.c_str(), problem.c_str());
                    }
                }
        check(builds, "Every race and background builds with every class");
        bool takeable = true;
        for (const auto& [featId, feat] : options.feats)
        {
            yh::CharacterChoices made;
            made.name = "Test";
            made.race = feat.needs.races.empty() ? "human" : feat.needs.races.front();
            for (const auto& ability : chapter->rules.abilities) made.scores[ability.id] = 16;
            const std::string classId = feat.needs.classes.empty() ? "cleric" : feat.needs.classes.front();
            for (int level = 1; level <= std::max(1, feat.needs.level); level++) made.levels.push_back({classId, {}});
            made.levels.back().picks["skills"] = feat.needs.proficiencies;
            if (feat.kind != "race") made.levels.back().picks["feats"] = {featId};
            if (!options.build(chapter->rules, made, &problem))
            {
                takeable = false;
                std::fprintf(stderr, "%s: %s\n", featId.c_str(), problem.c_str());
            }
        }
        check(takeable, "Every shipped feat can be taken");
    }
    {
        const auto shipped = json::parse(*files.readText("rulesets/yorehold/stealth.json"));
        check(shipped.at("checkEvery") == 5 && shipped.at("sneakSpeed") == 0.5 && shipped.at("darkBonus") == 5 && shipped.at("critical") == true,
            "The shipped stealth rules are the designed defaults");
    }
    {
        // The keep names no ruleset, so it plays by the game's own folder.
        const yh::Ruleset& rules = chapter->rules;
        check(rules.id == "yorehold" && chapter->rulesFolder == Chapter::defaultRuleset, "A chapter that names no ruleset uses the game's own");
        check(rules.actionsPerTurn == 2 && !rules.bonusActions && rules.strikeCostsHands && rules.magicItemLimit == 3 && rules.passiveBase == 10
            && rules.rest("short") && rules.rest("short")->perAdventure == 2 && rules.rest("long") && rules.rest("long")->recovery.reviveDowned
            && rules.levelForXp(249) == 1 && rules.levelForXp(250) == 2 && rules.reviveAfterVictory == 1 && rules.hitDie("Fighter") == 10,
            "The game's ruleset file holds its turn, rest, XP and item numbers");
        // Moving the numbers into a file changed none of them: apart from its name and the item
        // limit it is the built-in set the keep used before.
        const yh::Ruleset modern = yh::Ruleset::modern();
        yh::Ruleset renamed = rules;
        renamed.id = modern.id;
        renamed.name = modern.name;
        renamed.magicItemLimit = modern.magicItemLimit;
        check(renamed.sharedTurns && !modern.sharedTurns, "Yorehold enables shared turns while the prototype remains sequential");
        renamed.sharedTurns = modern.sharedTurns;
        check(renamed.proficiencyRanks.size() == 5 && renamed.proficiencyBonus(7, "trained") == 9
            && renamed.proficiencyBonus(20, "legendary") == 28 && renamed.proficiencyBonus(20, "untrained") == 0,
            "Yorehold uses level plus rank, with no level for untrained checks");
        renamed.proficiencyRanks = modern.proficiencyRanks;
        renamed.proficientRank = modern.proficientRank;
        renamed.untrainedRank = modern.untrainedRank;
        check(renamed.death.enabled && renamed.death.successes == 3 && renamed.death.failures == 3
            && renamed.death.saveDc == 10 && !modern.death.enabled, "Yorehold enables data-defined death saves");
        renamed.death = modern.death;
        json mine = json::parse(renamed.toJson()), theirs = json::parse(modern.toJson());
        // Conditions are checked on their own, and a full recovery has no use for a fraction.
        for (json* set : {&mine, &theirs})
        {
            set->erase("conditions");
            for (json& rest : set->at("rests"))
                if (rest.at("recovery").at("kind") == "full")
                    rest.at("recovery").erase("fraction");
        }
        check(mine == theirs, "The game's ruleset matches the numbers the keep was played with");
    }

    const yh::Grid grid(yh::GridType::Square, GameMap::cellSize);
    const auto start = chapter->party.front().at;
    std::set<std::pair<int, int>> taken;
    for (const auto& member : chapter->party)
        check(chapter->map.walkable(member.at) && taken.insert({member.at.x, member.at.y}).second, "Party placements are open and distinct");
    for (const auto& encounter : chapter->encounters)
        for (const auto& enemy : encounter.creatures)
        {
            check(chapter->map.walkable(enemy.at) && taken.insert({enemy.at.x, enemy.at.y}).second, "Enemy placements are open and distinct");
            const auto path = yh::findPath(grid, start, enemy.at, [&](yh::Cell cell) { return chapter->map.walkable(cell); });
            check(!path.empty() && path.front() == start && path.back() == enemy.at, "Every encounter is reachable");
        }
    const auto bounds = chapter->map.map().worldBounds();
    check(!chapter->map.walls().empty() && std::all_of(chapter->map.walls().begin(), chapter->map.walls().end(), [&](const yh::Wall& wall) {
        auto inside = [&](yh::Vec2 point) { return point.x >= 0 && point.y >= 0 && point.x <= bounds.w && point.y <= bounds.h; };
        return inside(wall.a) && inside(wall.b) && wall.a != wall.b && (wall.a.x == wall.b.x || wall.a.y == wall.b.y);
    }), "Loaded maps provide valid walls for vision and lighting");
    check(std::all_of(chapter->map.lights().begin(), chapter->map.lights().end(), [&](const GameMap::Light& light) {
        return std::isfinite(light.radius) && light.radius > 0 && chapter->map.inside(grid.cellAt(light.position));
    }), "Loaded lights have valid locations and radii");

    yh::Random random(1);
    const auto hero = chapter->compendium.makeCharacter(chapter->rules, "fighter", "Hero", random);
    const auto goblin = chapter->compendium.makeCreature(chapter->rules, "goblin", "Custom name", random);
    check(hero && hero->inventory.size() == 3 && hero->weapon() && hero->weapon()->id == "longsword", "Classes supply starting gear");
    check(goblin && goblin->name == "Custom name" && goblin->hp == 7 && goblin->armorClass(chapter->rules) == 13, "Creature files supply names, HP and final AC");
    const auto& originalClass = *chapter->compendium.characterClass("fighter");
    const auto classCopy = yh::Compendium::classFromJson(yh::Compendium::classToJson(originalClass));
    check(classCopy && classCopy->items == originalClass.items && classCopy->proficiencies == originalClass.proficiencies, "Class files round-trip gear and proficiencies");
    const auto& originalItem = *chapter->compendium.item("chain-shirt");
    const auto itemCopy = yh::Compendium::itemFromJson(yh::Compendium::itemToJson(originalItem));
    check(itemCopy && itemCopy->modifiers.size() == 1 && itemCopy->modifiers.front().value == originalItem.modifiers.front().value, "Item files round-trip their modifiers");
    const auto& originalCreature = *chapter->compendium.creature("goblin-boss");
    const auto creatureCopy = yh::Compendium::creatureFromJson(yh::Compendium::creatureToJson(originalCreature));
    check(creatureCopy && creatureCopy->hp == 20 && creatureCopy->token.size == originalCreature.token.size, "Creature files round-trip stats and appearance");

    const auto archive = scratch / "keep.yore";
    check(yh::FileSystem::packFolder(YH_GAME_ASSETS, archive.string()), "Export all content into a .yore archive");
    yh::FileSystem portable;
    check(ContentPackage::mount(portable, archive.string(), "portable"), "Import the archive by itself");
    auto manifest = ContentPackage::load(portable, &error);
    check(manifest && manifest->validate(portable, &error), "Imported content has all its dependencies");
    auto imported = manifest ? Chapter::load(portable, manifest->defaultChapter, &error) : std::nullopt;
    check(imported && imported->signature == chapter->signature, "Transfer preserves content identity");

    const auto overrideRoot = scratch / "overrides";
    fs::create_directories(overrideRoot);
    check(files.mountFolder(overrideRoot.string(), "overrides"), "Mount author overrides");
    const auto original = json::parse(*files.readText("chapters/goblin-keep/chapter.json"));
    const auto chapterFile = overrideRoot / "chapters/goblin-keep/chapter.json";
    // How creatures think can be rewritten freely: it isn't part of a save's identity.
    check(chapter->encounters[0].creatures[1].ai == "\"lookout\"" && chapter->aiChanges.size() == 1 && chapter->compendium.ai.contains("lookout")
        && chapter->compendium.aiFor(*chapter->compendium.creature("goblin-boss")).leader, "Chapters and creature files set AI by name or by changes");
    auto rethought = original;
    rethought["encounters"][0]["ai"] = "brute";
    rethought["encounters"][0]["creatures"][0]["ai"] = {{"fleeHp", 0.9}};
    rethought["aiChanges"] = json::array();
    write(chapterFile, rethought);
    auto same = Chapter::load(files, content->defaultChapter, &error);
    check(same && same->encounters[0].ai == "\"brute\"" && same->aiChanges.empty() && same->signature == chapter->signature, "AI edits keep old saves valid");
    rethought["encounters"][0]["ai"] = "genius";
    write(chapterFile, rethought);
    check(!Chapter::load(files, content->defaultChapter, &error) && error.find("genius") != std::string::npos, "Unknown AI names report the chapter file");

    auto data = original;
    data["party"] = json::array({original["party"][0]});
    data["encounters"] = json::array();
    data.erase("aiChanges"); // they name encounters this version no longer has
    data["endings"] = json::object();
    data["intro"] = json::array({"The author's introduction."});
    data["clearedText"] = "The author's ending.";
    write(chapterFile, data);
    auto small = Chapter::load(files, content->defaultChapter, &error);
    check(small && small->party.size() == 1 && small->encounters.empty() && small->intro.front() == "The author's introduction."
        && small->clearedText == "The author's ending.", "Chapter writers can change party size, encounters and story");
    check(small && small->signature != chapter->signature, "Content edits invalidate old save identity");
    data["party"][0]["class"] = "unknown";
    write(chapterFile, data);
    check(!Chapter::load(files, content->defaultChapter, &error) && error.find("unknown class") != std::string::npos, "Unknown class ids report the chapter file");
    data = original;
    data["encounters"][0]["creatures"][0]["at"] = original["party"][0]["at"];
    write(chapterFile, data);
    check(!Chapter::load(files, content->defaultChapter, &error) && error.find("occupied") != std::string::npos, "Overlapping placements are rejected");
    data = original;
    data["encounters"][1]["id"] = data["encounters"][0]["id"];
    write(chapterFile, data);
    check(!Chapter::load(files, content->defaultChapter, &error) && error.find("duplicate encounter") != std::string::npos, "Encounter ids are unique");
    data = original;
    data["endings"]["cleared"] = "missing.json";
    write(chapterFile, data);
    check(!Chapter::load(files, content->defaultChapter, &error) && error.find("missing.json") != std::string::npos, "Missing endings are caught before play");
    data["endings"]["cleared"] = "../outside.json";
    write(chapterFile, data);
    check(!Chapter::load(files, content->defaultChapter, &error), "Chapter paths stay within the content tree");
    write(chapterFile, original);
    check(!Chapter::load(files, "../outside", &error), "Invalid chapter folders are rejected");

    auto boss = json::parse(yh::Compendium::creatureToJson(originalCreature));
    boss["hp"] = 33;
    write(overrideRoot / "chapters/goblin-keep/creatures/goblin-boss.json", boss);
    auto custom = Chapter::load(files, content->defaultChapter, &error);
    check(custom && custom->compendium.creature("goblin-boss")->hp == 33 && custom->signature != chapter->signature,
        "Chapter definitions override shared definitions and update save identity");
    yh::Compendium compendium;
    check(compendium.load(portable, "", &error), "Load shared classes, items and creatures independently");
    write(overrideRoot / "bad/classes/fighter.json", {{"id", "fighter"}, {"items", {"missing-item"}}});
    check(!compendium.load(files, "bad", &error) && compendium.characterClass("fighter")->items == originalClass.items,
        "A broken content import leaves the existing compendium intact");

    auto badManifest = json::parse(*portable.readText("content.json"));
    badManifest["version"] = 999;
    write(overrideRoot / "content.json", badManifest);
    check(!ContentPackage::load(files, &error), "Future package versions fail clearly");
    badManifest["version"] = 1;
    badManifest["defaultChapter"] = "unlisted";
    write(overrideRoot / "content.json", badManifest);
    check(!ContentPackage::load(files, &error), "Manifest defaults must name a declared chapter");
}

void libraryTests(const fs::path& scratch)
{
    const auto library = (scratch / "library").string();
    const auto archive = scratch / "My Keep (2).yore";
    check(yh::FileSystem::packFolder(YH_GAME_ASSETS, archive.string()), "Pack an adventure to share");
    std::string error;
    check(ContentLibrary::installed(library).empty(), "A missing library folder is just empty");
    auto added = ContentLibrary::install(archive.string(), library, &error);
    check(added && added->adventures.size() == 1 && added->adventures.front().title == "The Goblin Keep"
        && added->adventures.front().folder == "chapters/goblin-keep", "Opening a .yore adds its adventures");
    check(added && added->classes == 4 && added->items == 10 && added->creatures == 3, "Added files report what they hold");
    check(added && fs::path(added->path).filename() == "my-keep-2.yore" && fs::exists(added->path) && fs::exists(archive),
        "Files are copied into the library under plain names");
    check(ContentLibrary::install(archive.string(), library, &error) && ContentLibrary::installed(library).size() == 1,
        "Adding the same file again replaces the old copy");
    check(added && ContentLibrary::install(added->path, library, &error).has_value(), "Opening a file that is already in the library is fine");

    // Classes and items alone, with no chapter to play.
    const auto defs = scratch / "defs";
    fs::create_directories(defs);
    fs::copy(fs::path(YH_GAME_ASSETS) / "items", defs / "items", fs::copy_options::recursive);
    fs::copy(fs::path(YH_GAME_ASSETS) / "classes", defs / "classes", fs::copy_options::recursive);
    write(defs / "content.json", {{"format", "yorehold.content"}, {"version", 1}, {"name", "Starter classes"}});
    const auto defsArchive = scratch / "classes.yore";
    check(yh::FileSystem::packFolder(defs.string(), defsArchive.string()), "Pack definitions without a chapter");
    const auto pack = ContentLibrary::install(defsArchive.string(), library, &error);
    check(pack && pack->name == "Starter classes" && pack->adventures.empty() && pack->classes == 4 && pack->items == 10 && pack->creatures == 0,
        "Classes and items can be shared without an adventure");

    // A pack with one new class: it joins the compendium without touching the installed adventure.
    const auto extra = scratch / "extra";
    auto wizard = json::parse(std::ifstream(fs::path(YH_GAME_ASSETS) / "classes" / "fighter.json"));
    wizard["id"] = "wizard";
    wizard["name"] = "Wizard";
    wizard["items"] = json::array();
    write(extra / "classes" / "wizard.json", wizard);
    write(extra / "content.json", {{"format", "yorehold.content"}, {"version", 1}, {"name", "Wizards"}});
    check(yh::FileSystem::packFolder(extra.string(), (scratch / "wizards.yore").string())
        && ContentLibrary::install((scratch / "wizards.yore").string(), library, &error), "Add a pack with one new class");
    const auto packs = ContentLibrary::installed(library);
    const yh::Compendium all = ContentLibrary::compendium(YH_GAME_ASSETS, packs);
    check(all.classes.size() == 5 && all.characterClass("wizard") && all.characterClass("fighter") && all.items.size() == 10,
        "Added packs extend the compendium used for making things");
    const auto unchanged = ContentLibrary::inspect(added->path, &error);
    yh::FileSystem keepFiles;
    ContentPackage::mount(keepFiles, added->path, "keep");
    const auto keepChapter = Chapter::load(keepFiles, "chapters/goblin-keep", &error);
    check(unchanged && keepChapter && !keepChapter->compendium.characterClass("wizard"), "Adventures only use what their own file carries");
    check(ContentLibrary::remove(packs.back()), "Remove the extra pack");

    {
        std::ofstream broken(scratch / "broken.yore", std::ios::binary);
        broken << "not a package";
    }
    check(!ContentLibrary::install((scratch / "broken.yore").string(), library, &error) && !error.empty()
        && !fs::exists(fs::path(library) / "broken.yore"), "Broken files are refused and not copied");
    fs::remove(defs / "items" / "longsword.json");
    check(!ContentLibrary::inspect(defs.string(), &error) && error.find("longsword") != std::string::npos,
        "Definitions that reference missing items are refused");

    {
        std::ofstream stray(fs::path(library) / "stray.yore", std::ios::binary);
        stray << "junk";
    }
    std::vector<std::string> problems;
    const auto installed = ContentLibrary::installed(library, &problems);
    check(installed.size() == 2 && problems.size() == 1 && problems.front().starts_with("stray.yore"), "Damaged library files are skipped and reported");
    check(pack && ContentLibrary::remove(*pack) && ContentLibrary::installed(library).size() == 1, "Installed files can be removed");
}

// Starting the game with a .yore (what double-clicking one does) installs and selects it.
void openFileTests(const fs::path& scratch)
{
    const auto stateDir = scratch / "open-state";
    SDL_setenv_unsafe("YOREHOLD_SAVE_DIR", stateDir.string().c_str(), 1);
    const auto source = scratch / "crypt";
    fs::copy(YH_GAME_ASSETS, source, fs::copy_options::recursive);
    auto chapter = json::parse(std::ifstream(source / "chapters" / "goblin-keep" / "chapter.json"));
    chapter["title"] = "The Sunken Crypt";
    write(source / "chapters" / "goblin-keep" / "chapter.json", chapter);
    const auto archive = scratch / "crypt.yore";
    check(yh::FileSystem::packFolder(source.string(), archive.string()), "Pack an edited adventure");
    SDL_Event enter{};
    enter.type = SDL_EVENT_KEY_DOWN;
    enter.key.key = SDLK_RETURN;
    {
        YoreholdGame game({archive.string()});
        check(fs::exists(stateDir / "library" / "crypt.yore"), "Starting the game with a .yore adds it to the library");
        game.handleEvent(enter); // the Play menu is already open: start it
        game.unload();
        check(fs::exists(stateDir / "adventure-crypt-goblin-keep.json") && !fs::exists(stateDir / "adventure.json"),
            "Installed adventures keep their own save");
    }
    {
        YoreholdGame game; // next launch: the same adventure is still selected
        game.handleEvent(enter);
        game.handleEvent(enter);
        game.unload();
        check(!fs::exists(stateDir / "adventure.json"), "The last adventure is remembered between launches");
    }
    {
        YoreholdGame game({(scratch / "broken.yore").string()});
        check(game.describe() == "screen: title" && !fs::exists(stateDir / "library" / "broken.yore"), "A broken file leaves the game usable");
    }
    SDL_unsetenv_unsafe("YOREHOLD_SAVE_DIR");
}

void mapTests()
{
    const auto original = json::parse(R"({
        "tiles": {
            "floor": {"color": [90, 100, 110]},
            "glass": {"walkable": false, "blocksSight": false},
            "wall": {"walkable": false, "blocksSight": true}
        },
        "legend": {".": "floor", "#": "wall", "g": "glass"},
        "layers": [
            {"name": "ground", "rows": ["....", "...."]},
            {"name": "walls", "rows": [" #g ", "    "]}
        ],
        "ambient": [10, 20, 30],
        "lights": [{"at": [0.5, 1.5], "radius": 2, "flame": false}]
    })");
    auto map = GameMap::fromJson(original.dump());
    check(map && map->width() == 4 && map->height() == 2 && map->walkable({0, 0}) && !map->walkable({1, 0}), "Map layers combine ground and obstacles");
    check(map && map->blocksSight({1, 0}) && !map->blocksSight({2, 0}) && !map->walkable({2, 0}), "Sight and movement blocking are independent");
    check(map && map->ambient().r == 10 && !map->lights().front().flame && map->lights().front().radius == 2 * GameMap::cellSize, "Ambient and steady lights come from files");
    check(map && !map->walkable({-1, 0}) && !map->walkable({4, 0}), "Loaded maps stop movement at their bounds");
    auto data = original;
    data["layers"][0]["rows"][0] = " ...";
    map = GameMap::fromJson(data.dump());
    check(map && !map->walkable({0, 0}), "Empty ground is not walkable");
    data = original;
    data["layers"][1]["rows"][0] = "short";
    check(!GameMap::fromJson(data.dump()), "Rows must have matching widths");
    data = original;
    data["layers"][1]["rows"][0] = " ?  ";
    check(!GameMap::fromJson(data.dump()), "Unknown tile symbols fail validation");
    data = original;
    data["ambient"] = {300, 0, 0};
    check(!GameMap::fromJson(data.dump()), "Invalid ambient colors fail validation");
    data = original;
    data["lights"][0]["at"] = {9, 1};
    check(!GameMap::fromJson(data.dump()), "Lights stay inside the map");
}

void gameErrorTests()
{
    SDL_setenv_unsafe("YOREHOLD_SEED", "1", 1);
    SDL_setenv_unsafe("YOREHOLD_CHAPTER", "missing", 1);
    YoreholdGame game;
    SDL_Event key{};
    key.type = SDL_EVENT_KEY_DOWN;
    key.key.key = SDLK_F9;
    game.handleEvent(key);
    game.update(1);
    check(game.describe().starts_with("screen: content error"), "Auto-play cannot enter an unloaded chapter");
    key.key.key = SDLK_RETURN;
    game.handleEvent(key);
    game.handleEvent(key);
    game.update(1);
    check(game.describe().starts_with("screen: content error"), "Enter cannot start an unloaded chapter");
    SDL_unsetenv_unsafe("YOREHOLD_CHAPTER");
    SDL_unsetenv_unsafe("YOREHOLD_SEED");
}

void saveTests(const fs::path& scratch)
{
    const auto saveDir = scratch / "saves";
    SDL_setenv_unsafe("YOREHOLD_SAVE_DIR", saveDir.string().c_str(), 1);
    SDL_setenv_unsafe("YOREHOLD_CONTENT", YH_GAME_ASSETS, 1);
    SDL_Event enter{};
    enter.type = SDL_EVENT_KEY_DOWN;
    enter.key.key = SDLK_RETURN;
    auto playAndSave = [&] {
        YoreholdGame game;
        game.handleEvent(enter); // Main -> Play
        game.handleEvent(enter); // New or Continue
        game.unload();
    };
    auto readSave = [&] {
        std::ifstream file(saveDir / "adventure.json", std::ios::binary);
        return json::parse(file);
    };
    playAndSave();
    auto saved = readSave();
    check(saved["version"] == 3 && saved["data"]["chapterId"] == "goblin-keep" && saved["data"].contains("chapterSignature"),
        "New saves record chapter identity and content signature");
    saved["data"]["creatures"][0]["sheet"]["xp"] = 17;
    write(saveDir / "adventure.json", saved);
    playAndSave();
    check(readSave()["data"]["creatures"][0]["sheet"]["xp"] == 17, "Continue restores a save from the same content");
    saved["version"] = 2;
    saved["data"].erase("chapterId");
    saved["data"].erase("chapterFolder");
    saved["data"].erase("chapterSignature");
    write(saveDir / "adventure.json", saved);
    playAndSave();
    check(readSave()["version"] == 3 && readSave()["data"]["creatures"][0]["sheet"]["xp"] == 17, "Version 2 keep saves still resume");
    saved["version"] = 1;
    saved["data"].erase("restsUsed");
    saved["data"]["restsLeft"] = 1;
    write(saveDir / "adventure.json", saved);
    playAndSave();
    check(readSave()["data"]["restsUsed"]["short"] == 1 && readSave()["data"]["creatures"][0]["sheet"]["xp"] == 17,
        "Version 1 keep saves migrate rests and chapter identity");
    saved = readSave();
    saved["data"]["chapterId"] = "other-chapter";
    write(saveDir / "adventure.json", saved);
    playAndSave();
    check(readSave()["data"]["creatures"][0]["sheet"]["xp"] == 0, "Saves cannot load sheets from another chapter");
    saved["data"]["chapterId"] = "goblin-keep";
    saved["data"]["chapterSignature"] = "old-content";
    write(saveDir / "adventure.json", saved);
    playAndSave();
    check(readSave()["data"]["creatures"][0]["sheet"]["xp"] == 0, "Edited content rejects old saves");
    saved = readSave();
    saved["data"]["creatures"][0]["sheet"]["xp"] = 17;
    saved["data"]["fog"]["width"] = 1;
    write(saveDir / "adventure.json", saved);
    playAndSave();
    check(readSave()["data"]["creatures"][0]["sheet"]["xp"] == 0, "Mismatched fog dimensions are rejected");
    SDL_unsetenv_unsafe("YOREHOLD_SAVE_DIR");
    SDL_unsetenv_unsafe("YOREHOLD_CONTENT");
}

// The rules of sneaking, with the keep's own map, creatures and numbers.
void stealthTests(const fs::path& scratch)
{
    yh::FileSystem files;
    check(files.mountFolder(YH_GAME_ASSETS, "game"), "Mount game files for stealth");
    std::string error;
    const auto chapter = Chapter::load(files, "chapters/goblin-keep", &error);
    check(chapter.has_value(), "Load the keep for stealth");
    if (!chapter) return;

    const float cell = GameMap::cellSize;
    const yh::Grid grid(yh::GridType::Square, cell);
    const yh::StealthRules onMap = chapter->stealthOnMap();
    check(std::fabs(onMap.checkEvery - 5 / 1.524f * cell) < 0.01f && onMap.sneakSpeed == chapter->stealth.sneakSpeed,
        "The check distance is metres in the file and world units on the map");

    // Gob guards the entry hall, looking west at the door; Snik was told nothing and looks toward where the party starts.
    const Chapter::Placement& gob = chapter->encounters[0].creatures[0];
    const Chapter::Placement& snik = chapter->encounters[0].creatures[1];
    const float pi = 3.14159265f;
    check(std::fabs(chapter->facingOf(gob) - pi) < 0.001f, "A placement's facing is degrees in the file");
    check(std::fabs(std::cos(chapter->facingOf(snik)) + 1) < 0.05f, "Without one, a creature watches the way the party comes from");

    yh::Random dice(1);
    const auto goblin = chapter->compendium.makeCreature(chapter->rules, gob.creatureId, gob.name, dice);
    const auto rogue = chapter->compendium.makeCharacter(chapter->rules, "rogue", "Cel", dice);
    check(goblin && rogue, "Make the keep's goblin and rogue");
    if (!goblin || !rogue) return;
    yh::Watcher watcher;
    watcher.position = grid.center(gob.at);
    watcher.facing = chapter->facingOf(gob);
    watcher.range = chapter->map.lighting().sight * cell;
    watcher.passivePerception = 10 + goblin->checkModifier(chapter->rules, "perception");
    const auto walls = std::span<const yh::Wall>(chapter->map.walls());
    // Along the hall toward Gob, one row below him.
    const yh::Vec2 door = grid.center({18, 13}), close = grid.center({27, 13});
    const int hopeless = -100, expert = 100;

    {
        // Looking the other way, he never gets a roll, however clumsy the sneaker.
        yh::Watcher away = watcher;
        away.facing = 0;
        yh::StealthTracker tracker(onMap);
        yh::Random random(1);
        check(tracker.move(door, close, true, hopeless, std::span(&away, 1), walls, random).empty(), "Sneak past a watcher that faces away");
        check(!yh::sees(away, close, walls) && yh::sees(watcher, close, walls), "A cone only covers what is in front");
    }
    {
        yh::StealthTracker tracker(onMap);
        yh::Random random(1);
        const auto rolls = tracker.move(door, close, true, hopeless, std::span(&watcher, 1), walls, random);
        check(!rolls.empty() && rolls.back().spotted && rolls.back().dc == watcher.passivePerception
            && rolls.back().total == rolls.back().roll + hopeless, "A sneaker inside a cone is spotted when Stealth falls short of passive Perception");
    }
    {
        // A sure hand is checked on coming into view and again every 5 m: three times over this walk.
        yh::StealthRules steady = onMap;
        steady.critical = false;
        yh::StealthTracker tracker(steady);
        yh::Random random(1);
        const auto rolls = tracker.move(door, close, true, expert, std::span(&watcher, 1), walls, random);
        check(rolls.size() == 3 && std::none_of(rolls.begin(), rolls.end(), [](const yh::StealthCheck& c) { return c.spotted; }),
            "Stealth is checked on entering a cone and every few metres after");
        if (rolls.size() == 3)
            check(std::fabs(rolls[1].at.x - rolls[2].at.x) > onMap.checkEvery * 0.75f && std::fabs(rolls[1].at.x - rolls[2].at.x) < onMap.checkEvery * 1.25f,
                "Checks are the ruleset's distance apart");
    }
    {
        // Light: the same roll counts for more in the dark, and a watcher without darkvision sees nothing there.
        const std::function<yh::LightLevel(yh::Vec2)> dark = [](yh::Vec2) { return yh::LightLevel::Dark; };
        yh::StealthTracker blind(onMap), keen(onMap);
        yh::Random random(1);
        check(blind.move(door, close, true, hopeless, std::span(&watcher, 1), walls, random, dark).empty(), "Darkness hides a sneaker from eyes that need light");
        yh::Watcher goblinEyes = watcher;
        goblinEyes.darkRange = goblin->stats.value("darkvision") / static_cast<float>(chapter->rules.feetPerSquare) * cell;
        const auto rolls = keen.move(grid.center({22, 13}), close, true, 0, std::span(&goblinEyes, 1), walls, random, dark);
        check(goblinEyes.darkRange > 0 && !rolls.empty() && rolls.front().total == rolls.front().roll + chapter->stealth.darkBonus,
            "Darkvision sees in the dark, where Stealth gets the ruleset's bonus");
    }
    {
        // An ambush: the goblins are surprised and the whole party acts before any of them.
        yh::Random random(2);
        auto fighter = chapter->compendium.makeCharacter(chapter->rules, "fighter", "Astra", random);
        auto sneak = chapter->compendium.makeCharacter(chapter->rules, "rogue", "Cel", random);
        auto first = chapter->compendium.makeCreature(chapter->rules, "goblin", "Gob", random);
        auto second = chapter->compendium.makeCreature(chapter->rules, "goblin", "Snik", random);
        yh::Encounter fight(chapter->rules, 5);
        fight.add(*fighter, 0);
        fight.add(*sneak, 0);
        fight.add(*first, 1);
        fight.add(*second, 1);
        fight.surprise(1);
        fight.start();
        int heroTurns = 0, goblinTurns = 0;
        while (fight.round() == 1)
        {
            ++(fight.current().team == 0 ? heroTurns : goblinTurns);
            fight.nextTurn();
        }
        check(heroTurns == 2 && goblinTurns == 0 && fight.round() == 2, "An ambush costs the enemies their first turn");
        bool later = false;
        for (int i = 0; i < 4; i++)
        {
            later |= fight.current().team == 1;
            fight.nextTurn();
        }
        check(later, "Surprised enemies act from the second round");
    }

    // The game itself, started from a save that puts the party where the test needs it.
    const auto saveDir = scratch / "sneak-saves";
    SDL_setenv_unsafe("YOREHOLD_SAVE_DIR", saveDir.string().c_str(), 1);
    SDL_setenv_unsafe("YOREHOLD_CONTENT", YH_GAME_ASSETS, 1);
    SDL_Event enter{};
    enter.type = SDL_EVENT_KEY_DOWN;
    enter.key.key = SDLK_RETURN;
    SDL_Event sneakKey = enter;
    sneakKey.key.key = SDLK_C;
    {
        YoreholdGame game;
        game.handleEvent(enter);
        game.handleEvent(enter);
        game.unload();
    }
    json saved;
    {
        std::ifstream file(saveDir / "adventure.json", std::ios::binary);
        saved = json::parse(file);
    }
    auto place = [&](const std::vector<yh::Cell>& cells, bool sneaking) {
        json edited = saved;
        for (size_t i = 0; i < cells.size(); i++)
        {
            const yh::Vec2 at = grid.center(cells[i]);
            edited["data"]["creatures"][i]["x"] = at.x;
            edited["data"]["creatures"][i]["y"] = at.y;
            edited["data"]["creatures"][i]["sneaking"] = sneaking;
        }
        write(saveDir / "adventure.json", edited);
    };
    auto frames = [](YoreholdGame& game, int count) {
        for (int i = 0; i < count; i++) game.update(1.0 / 60);
    };
    check(saved["data"]["creatures"][0].contains("sneaking") && saved["data"]["creatures"][0]["sneaking"] == false, "Saves record who is sneaking");

    // Behind both goblins of the entry hall, out of sight of the other rooms.
    const std::vector<yh::Cell> behind{{30, 12}, {30, 13}, {30, 17}, {30, 18}};
    place(behind, true);
    {
        YoreholdGame game;
        game.handleEvent(enter);
        game.handleEvent(enter);
        check(game.describe() == "screen: exploring, sneaking", "A save restores sneaking heroes");
        frames(game, 30);
        check(game.describe() == "screen: exploring, sneaking", "Sneaking behind the goblins' backs goes unnoticed");
        game.handleEvent(sneakKey);
        check(game.describe() == "screen: exploring", "C stops sneaking");
        frames(game, 2);
        check(game.describe() == "screen: combat round 1", "Standing up in plain sight starts the fight");
    }
    place(behind, false);
    {
        YoreholdGame game;
        game.handleEvent(enter);
        game.handleEvent(enter);
        game.handleEvent(sneakKey); // before the goblins get a look
        check(game.describe() == "screen: exploring, sneaking", "C starts sneaking");
        frames(game, 30);
        check(game.describe() == "screen: exploring, sneaking", "Sneaking from the first moment keeps the party hidden");
    }
    // In front of Gob, well inside his cone: eight rolls against two goblins do not all pass.
    place({{22, 12}, {22, 13}, {21, 12}, {21, 13}}, true);
    {
        YoreholdGame game;
        game.handleEvent(enter);
        game.handleEvent(enter);
        check(game.describe() == "screen: exploring, sneaking", "The party sneaks into the hall");
        frames(game, 2);
        check(game.describe() == "screen: combat round 1", "Being spotted inside a cone starts the fight");
    }
    SDL_unsetenv_unsafe("YOREHOLD_SAVE_DIR");
    SDL_unsetenv_unsafe("YOREHOLD_CONTENT");
}

}

void worldPlayTests(const std::function<void(bool, const char*)>& check); // WorldTests.cpp
void worldActionTests(const std::function<void(bool, const char*)>& check);
void worldTurnTests(const std::function<void(bool, const char*)>& check);
void worldPositioningTests(const std::function<void(bool, const char*)>& check);
void worldProficiencyTests(const std::function<void(bool, const char*)>& check);
void worldDeathTests(const std::function<void(bool, const char*)>& check);
void worldCharacterTests(const std::function<void(bool, const char*)>& check);

int main()
{
    try
    {
        Scratch scratch;
        contentTests(scratch.path);
        worldSaveTests();
        worldPlayTests(check);
        worldActionTests(check);
        worldTurnTests(check);
        worldPositioningTests(check);
        worldProficiencyTests(check);
        worldDeathTests(check);
        worldCharacterTests(check);
        libraryTests(scratch.path);
        mapTests();
        gameErrorTests();
        saveTests(scratch.path);
        stealthTests(scratch.path);
        openFileTests(scratch.path);
    }
    catch (const std::exception& e)
    {
        check(false, e.what());
    }
    std::printf("%d content checks, %d failures\n", checks, failures);
    return failures ? 1 : 0;
}
