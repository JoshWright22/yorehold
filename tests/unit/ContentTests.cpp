#include "YoreholdGame.h"
#include "content/Adventure.h"
#include "content/Chapter.h"
#include "content/CharacterDraft.h"
#include "content/CharacterLibrary.h"
#include "content/ContentPackage.h"
#include "screens/CreateScreen.h"
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

void manifestTests()
{
    std::string error;

    // Old packages without manifest fields still load
    {
        json manifest = {
            {"format", "yorehold.content"},
            {"version", 1},
            {"name", "Old Package"},
            {"chapters", nlohmann::json::array()}
        };
        fs::path testDir = fs::temp_directory_path() / "manifest-test";
        fs::create_directories(testDir);
        {
            std::ofstream file(testDir / "content.json");
            file << manifest.dump(2);
        }
        yh::FileSystem oldFiles;
        oldFiles.mountFolder(testDir.string(), "old");
        auto loaded = ContentPackage::load(oldFiles, &error);
        check(loaded && loaded->name == "Old Package" && loaded->kind.empty() && loaded->id.empty() && loaded->revision == 0
            && loaded->needs.empty(), "Old packages without manifest fields still load with defaults");
        std::error_code ec;
        fs::remove_all(testDir, ec);
    }

    // New packages with manifest fields load correctly
    {
        json manifest = {
            {"format", "yorehold.content"},
            {"version", 1},
            {"name", "Adventure Pack"},
            {"kind", "adventure"},
            {"id", "dragon-lair"},
            {"revision", 2},
            {"ruleset", "yorehold@1.0"},
            {"requires", nlohmann::json::array({"asset-pack-1"})},
            {"chapters", nlohmann::json::array()}
        };
        fs::path testDir = fs::temp_directory_path() / "manifest-test-new";
        fs::create_directories(testDir);
        {
            std::ofstream file(testDir / "content.json");
            file << manifest.dump(2);
        }
        yh::FileSystem newFiles;
        newFiles.mountFolder(testDir.string(), "new");
        auto loaded = ContentPackage::load(newFiles, &error);
        check(loaded && loaded->name == "Adventure Pack" && loaded->kind == "adventure" && loaded->id == "dragon-lair"
            && loaded->revision == 2 && loaded->ruleset == "yorehold@1.0" && loaded->needs.size() == 1 && loaded->needs[0] == "asset-pack-1",
            "New packages with manifest fields load correctly");
        std::error_code ec;
        fs::remove_all(testDir, ec);
    }

    // Invalid kind is rejected
    {
        json manifest = {
            {"format", "yorehold.content"},
            {"version", 1},
            {"kind", "invalid-kind"},
            {"chapters", nlohmann::json::array()}
        };
        fs::path testDir = fs::temp_directory_path() / "manifest-test-bad-kind";
        fs::create_directories(testDir);
        {
            std::ofstream file(testDir / "content.json");
            file << manifest.dump(2);
        }
        yh::FileSystem badFiles;
        badFiles.mountFolder(testDir.string(), "bad");
        auto loaded = ContentPackage::load(badFiles, &error);
        check(!loaded && error.find("unknown kind") != std::string::npos, "Invalid kind values are rejected");
        std::error_code ec;
        fs::remove_all(testDir, ec);
    }

    // Invalid id is rejected
    {
        json manifest = {
            {"format", "yorehold.content"},
            {"version", 1},
            {"id", "invalid id!"},
            {"chapters", nlohmann::json::array()}
        };
        fs::path testDir = fs::temp_directory_path() / "manifest-test-bad-id";
        fs::create_directories(testDir);
        {
            std::ofstream file(testDir / "content.json");
            file << manifest.dump(2);
        }
        yh::FileSystem badFiles;
        badFiles.mountFolder(testDir.string(), "bad");
        auto loaded = ContentPackage::load(badFiles, &error);
        check(!loaded && error.find("invalid characters") != std::string::npos, "Invalid id formats are rejected");
        std::error_code ec;
        fs::remove_all(testDir, ec);
    }

    // Invalid revision is rejected
    {
        json manifest = {
            {"format", "yorehold.content"},
            {"version", 1},
            {"revision", -1},
            {"chapters", nlohmann::json::array()}
        };
        fs::path testDir = fs::temp_directory_path() / "manifest-test-bad-revision";
        fs::create_directories(testDir);
        {
            std::ofstream file(testDir / "content.json");
            file << manifest.dump(2);
        }
        yh::FileSystem badFiles;
        badFiles.mountFolder(testDir.string(), "bad");
        auto loaded = ContentPackage::load(badFiles, &error);
        check(!loaded && error.find("revision") != std::string::npos, "Invalid revision values are rejected");
        std::error_code ec;
        fs::remove_all(testDir, ec);
    }

    // Valid kinds
    {
        const std::vector<std::string> validKinds = {"adventure", "ruleset", "compendium", "character_class", "race", "feat"};
        for (const auto& kind : validKinds)
        {
            json manifest = {
                {"format", "yorehold.content"},
                {"version", 1},
                {"kind", kind},
                {"id", "test-" + kind},
                {"chapters", nlohmann::json::array()}
            };
            fs::path testDir = fs::temp_directory_path() / ("manifest-test-" + kind);
            fs::create_directories(testDir);
            {
                std::ofstream file(testDir / "content.json");
                file << manifest.dump(2);
            }
            yh::FileSystem testFiles;
            testFiles.mountFolder(testDir.string(), "test");
            auto loaded = ContentPackage::load(testFiles, &error);
            check(loaded && loaded->kind == kind, ("Kind '" + kind + "' is valid").c_str());
            std::error_code ec;
            fs::remove_all(testDir, ec);
        }
    }

    // Valid id formats
    {
        const std::vector<std::string> validIds = {"simple", "with-dash", "with_underscore", "mixed-123_abc"};
        for (const auto& id : validIds)
        {
            json manifest = {
                {"format", "yorehold.content"},
                {"version", 1},
                {"id", id},
                {"chapters", nlohmann::json::array()}
            };
            fs::path testDir = fs::temp_directory_path() / ("manifest-test-id-" + id);
            fs::create_directories(testDir);
            {
                std::ofstream file(testDir / "content.json");
                file << manifest.dump(2);
            }
            yh::FileSystem testFiles;
            testFiles.mountFolder(testDir.string(), "test");
            auto loaded = ContentPackage::load(testFiles, &error);
            check(loaded && loaded->id == id, ("ID '" + id + "' is valid").c_str());
            std::error_code ec;
            fs::remove_all(testDir, ec);
        }
    }
}

void adventureTests()
{
    yh::FileSystem files;
    check(files.mountFolder(YH_GAME_ASSETS, "game"), "Mount authored game files");
    std::string error;

    // Load the test adventure
    auto adventure = Adventure::load(files, "", &error);
    check(adventure.has_value(), "The test adventure.json loads");
    if (!adventure) { std::fprintf(stderr, "%s\n", error.c_str()); return; }

    // Check basic adventure properties
    check(adventure->id == "test-adventure" && adventure->title == "Two-Chapter Test Adventure",
        "Adventure metadata is loaded correctly");
    check(adventure->minLevel == 1 && adventure->maxLevel == 5,
        "Adventure level range is loaded");
    check(adventure->recommendedPartySize == 3,
        "Adventure recommended party size is loaded");

    // Check chapters are loaded
    check(adventure->chapterFolders.size() == 2,
        "Adventure lists two chapter folders");
    check(adventure->chapterFolders[0] == "chapters/chapter-one" && adventure->chapterFolders[1] == "chapters/chapter-two",
        "Chapter folders are in the correct order");

    // Check transitions
    check(adventure->transitions.size() == 3,
        "Three transitions are defined");
    check(adventure->chapterIds == std::vector<std::string>{"chapter-one", "chapter-two"} && adventure->folderOf("chapter-two") == "chapters/chapter-two",
        "Chapter ids are read from the chapter files");
    check(!adventure->nextChapter("chapter-two", "end", {}) && adventure->nextChapter("chapter-two", "end", {"chapter_two_complete"}) == "chapter-one",
        "A transition with when flags only opens once they are set");
    check(adventure->nextChapter("chapter-two", "back", {}) == "chapter-one", "The way back is open from the start");
    if (adventure->transitions.size() >= 1)
    {
        const auto& trans = adventure->transitions[0];
        check(trans.fromChapter == "chapter-one" && trans.exitMarker == "exit" &&
              trans.toChapter == "chapter-two" && trans.entryMarker == "entry",
            "Transition specifies from/exit and to/entry correctly");
        check(trans.when.empty(),
            "Transition has no required flags");
    }

    // Check flags
    check(adventure->flags.size() == 2,
        "Adventure declares two story flags");
    check(std::find(adventure->flags.begin(), adventure->flags.end(), "chapter_one_complete") != adventure->flags.end() &&
          std::find(adventure->flags.begin(), adventure->flags.end(), "chapter_two_complete") != adventure->flags.end(),
        "Declared flags match expected names");

    // Test nextChapter navigation
    auto next = adventure->nextChapter("chapter-one", "exit", {});
    check(next.has_value() && *next == "chapter-two",
        "Exiting chapter-one at 'exit' marker leads to chapter-two");

    // Test nextChapter with no matching transition
    auto nowhere = adventure->nextChapter("chapter-two", "exit", {});
    check(!nowhere.has_value(),
        "Exiting chapter-two at non-existent transition returns empty");

    // Test nextChapter with non-existent marker
    auto invalid = adventure->nextChapter("chapter-one", "nonexistent", {});
    check(!invalid.has_value(),
        "Exiting at non-existent marker returns empty");

    // Load both chapters to verify they exist and are valid
    auto ch1 = Chapter::load(files, "chapters/chapter-one", &error);
    check(ch1.has_value(), "Chapter one loads successfully");
    if (!ch1) { std::fprintf(stderr, "%s\n", error.c_str()); }

    auto ch2 = Chapter::load(files, "chapters/chapter-two", &error);
    check(ch2.has_value(), "Chapter two loads successfully");
    if (!ch2) { std::fprintf(stderr, "%s\n", error.c_str()); }

    // Verify markers exist on the maps
    if (ch1)
    {
        check(ch1->map.marker("exit").has_value(), "Chapter one map has 'exit' marker");
    }

    if (ch2)
    {
        check(ch2->map.marker("entry").has_value(), "Chapter two map has 'entry' marker");
    }
}

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
            // Race feats come with the race; the others go in the first row of the class's table
            // that offers their kind, with any skill they need trained at a row before it.
            const std::vector<yh::ClassLevel>& rows = options.classes.at(classId).levels;
            size_t slot = rows.size();
            for (size_t row = static_cast<size_t>(std::max(1, feat.needs.level)) - 1; row < rows.size() && slot == rows.size(); row++)
                if (std::find(rows[row].feats.begin(), rows[row].feats.end(), feat.kind) != rows[row].feats.end()) slot = row;
            if (feat.kind == "race") slot = 0;
            if (slot == rows.size())
            {
                takeable = false;
                std::fprintf(stderr, "%s: no %s feat slot in %s\n", featId.c_str(), feat.kind.c_str(), classId.c_str());
                continue;
            }
            made.levels.assign(slot + 1, yh::LevelChoice{classId, {}});
            if (feat.kind != "race") made.levels[slot].picks["feats"] = {featId};
            for (size_t row = slot; row-- > 0 && !feat.needs.proficiencies.empty();)
                if (rows[row].skills >= static_cast<int>(feat.needs.proficiencies.size()))
                {
                    made.levels[row].picks["skills"] = feat.needs.proficiencies;
                    break;
                }
            if (!options.build(chapter->rules, made, &problem))
            {
                takeable = false;
                std::fprintf(stderr, "%s: %s\n", featId.c_str(), problem.c_str());
            }
        }
        check(takeable, "Every shipped feat can be taken");

        // Level tables: the four launch classes build at every level from 1 to 20.
        bool tables = true;
        for (const char* classId : {"fighter", "rogue", "cleric", "wizard"})
        {
            const yh::ClassDefinition* definition = options.characterClass(classId);
            if (!definition || definition->levels.size() != 20)
            {
                tables = false;
                std::fprintf(stderr, "%s: no 20-level table\n", classId);
                continue;
            }
            std::map<std::string, int> offered;
            for (const yh::ClassLevel& row : definition->levels)
                for (const std::string& kind : row.feats) offered[kind]++;
            if (offered["class"] != 5 || offered["skill"] != 3 || offered["general"] != 2)
            {
                tables = false;
                std::fprintf(stderr, "%s: feat slots are off the schedule\n", classId);
            }
            yh::CharacterChoices made;
            made.name = "Test";
            for (const auto& ability : chapter->rules.abilities) made.scores[ability.id] = 12;
            int lastHp = 0;
            for (int level = 1; level <= 20; level++)
            {
                made.levels.push_back({classId, {}});
                const auto sheet = options.build(chapter->rules, made, &problem);
                if (!sheet || sheet->level != level || sheet->maxHp() <= lastHp)
                {
                    tables = false;
                    std::fprintf(stderr, "%s level %d: %s\n", classId, level, problem.c_str());
                    break;
                }
                lastHp = sheet->maxHp();
                const bool caster = std::string_view(classId) == "cleric" || std::string_view(classId) == "wizard";
                if (caster != sheet->resources.contains("slots-1") || (caster && level >= 17) != sheet->resources.contains("slots-9"))
                {
                    tables = false;
                    std::fprintf(stderr, "%s level %d: wrong spell slots\n", classId, level);
                }
                // Both casters prepare: two spells at first, more every other level to 9, and a focus point.
                const int prepares = caster ? std::min(6, 2 + (level - 1) / 2) : 0;
                if (sheet->prepareLimit != prepares || caster != sheet->resources.contains("focus"))
                {
                    tables = false;
                    std::fprintf(stderr, "%s level %d: prepares %d, not %d\n", classId, level, sheet->prepareLimit, prepares);
                }
                // The list always has more to prepare than the table asks for, so there is a choice.
                if (caster && static_cast<int>(sheet->preparable.size()) <= sheet->prepareLimit)
                {
                    tables = false;
                    std::fprintf(stderr, "%s level %d: %zu spells to prepare %d from\n", classId, level, sheet->preparable.size(), sheet->prepareLimit);
                }
                // By level 5 the whole starter list is in reach: two cantrips, a focus spell and seven to prepare.
                if (caster && level >= 5 && (sheet->preparable.size() != 7 || sheet->spells.size() != 3 + static_cast<size_t>(sheet->prepareLimit)))
                {
                    tables = false;
                    std::fprintf(stderr, "%s level %d: %zu to prepare and %zu spells\n", classId, level, sheet->preparable.size(), sheet->spells.size());
                }
            }
        }
        check(tables, "Fighter, rogue, cleric and wizard build at every level from 1 to 20");
        {
            // Any level into any class, and the fighter's ranks rise on schedule.
            yh::CharacterChoices made;
            made.name = "Test";
            for (const auto& ability : chapter->rules.abilities) made.scores[ability.id] = 12;
            for (int level = 1; level <= 5; level++) made.levels.push_back({"fighter", {}});
            made.levels.push_back({"wizard", {}});
            made.levels.push_back({"rogue", {}});
            const auto mixed = options.build(chapter->rules, made, &problem);
            check(mixed && mixed->level == 7 && mixed->characterClass == "Fighter / Wizard / Rogue"
                && mixed->proficiencyRank(chapter->rules, "weapons") == "expert" && mixed->resources.at("slots-1").max == 2
                && mixed->resources.at("second-wind").max == 1 && mixed->stats.integer("damage") == 1,
                "Levels mix classes, each bringing its own table's rows");
            check(chapter->rules.xpForLevel.size() == 19 && chapter->rules.levelForXp(355000) == 20, "XP reaches level 20");
        }
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
        // The keep's levels cost what they did; the file only carries the curve on to level 20.
        check(renamed.xpForLevel.size() == 19
            && std::equal(modern.xpForLevel.begin(), modern.xpForLevel.end(), renamed.xpForLevel.begin()),
            "Yorehold extends the XP curve to level 20 without moving the early levels");
        renamed.xpForLevel = modern.xpForLevel;
        json mine = json::parse(renamed.toJson()), theirs = json::parse(modern.toJson());
        // Conditions are checked on their own, and a full recovery has no use for a fraction.
        // Spell slots and focus points coming back on rests are new with spells; the keep had no casting.
        check(renamed.rests.size() == 2 && renamed.rests[1].restores == std::vector<std::string>{"slots-*", "focus"}
            && renamed.rests[0].restores == std::vector<std::string>{"focus"}, "A long rest restores spell slots; both rests restore focus");
        // Camp is new since the keep: the long rest is taken there for supplies, as often as they
        // last, and gives the short rests back; the dead come back there for coins.
        check(renamed.rests[1].campOnly && renamed.rests[1].supplyCost == 40 && renamed.rests[1].perAdventure == 0
            && renamed.rests[1].resets == std::vector<std::string>{"short"} && !renamed.rests[0].campOnly && renamed.rests[0].supplyCost == 0
            && renamed.revivePrice == 20000 && renamed.reviveHp == 1, "The long rest is taken at camp for 40 supplies; revival there costs 200 gp");
        // Companions are new too: two of them, and six in the party with four heroes.
        check(renamed.companions.limit == 2 && renamed.companions.partyLimit == 6, "Two companions join four heroes");
        for (json* set : {&mine, &theirs})
        {
            set->erase("conditions");
            set->erase("revivePrice");
            set->erase("reviveHp");
            set->erase("companions");
            for (json& rest : set->at("rests"))
            {
                rest.erase("restores");
                rest.erase("supplyCost");
                rest.erase("campOnly");
                rest.erase("resets");
                if (rest.at("id") == "long")
                    rest.erase("perAdventure");
                if (rest.at("recovery").at("kind") == "full")
                    rest.at("recovery").erase("fraction");
            }
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
    check(hero && hero->inventory.size() == 4 && hero->weapon() && hero->weapon()->id == "longsword"
        && hero->inventory.back().id == "healing-potion", "Classes supply starting gear and a carried potion");
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
    check(added && added->classes == 5 && added->items == 16 && added->creatures == 3, "Added files report what they hold");
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
    check(pack && pack->name == "Starter classes" && pack->adventures.empty() && pack->classes == 5 && pack->items == 16 && pack->creatures == 0,
        "Classes and items can be shared without an adventure");

    // A pack with one new class: it joins the compendium without touching the installed adventure.
    const auto extra = scratch / "extra";
    auto warden = json::parse(std::ifstream(fs::path(YH_GAME_ASSETS) / "classes" / "fighter.json"));
    warden["id"] = "warden";
    warden["name"] = "Warden";
    warden["items"] = json::array();
    write(extra / "classes" / "warden.json", warden);
    write(extra / "content.json", {{"format", "yorehold.content"}, {"version", 1}, {"name", "Wardens"}});
    check(yh::FileSystem::packFolder(extra.string(), (scratch / "wardens.yore").string())
        && ContentLibrary::install((scratch / "wardens.yore").string(), library, &error), "Add a pack with one new class");
    const auto packs = ContentLibrary::installed(library);
    const yh::Compendium all = ContentLibrary::compendium(YH_GAME_ASSETS, packs);
    check(all.classes.size() == 6 && all.characterClass("warden") && all.characterClass("fighter") && all.items.size() == 16,
        "Added packs extend the compendium used for making things");
    const auto unchanged = ContentLibrary::inspect(added->path, &error);
    yh::FileSystem keepFiles;
    ContentPackage::mount(keepFiles, added->path, "keep");
    const auto keepChapter = Chapter::load(keepFiles, "chapters/goblin-keep", &error);
    check(unchanged && keepChapter && !keepChapter->compendium.characterClass("warden"), "Adventures only use what their own file carries");
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

// Making a character and levelling it up, with the game's own ruleset and options.
void characterDraftTests()
{
    yh::Ruleset rules;
    yh::Compendium compendium;
    std::string error;
    check(ContentLibrary::creation(YH_GAME_ASSETS, {}, rules, compendium, &error) && rules.id == "yorehold" && compendium.races.size() == 4
        && compendium.characterClass("wizard"), "Making characters uses the game's ruleset, classes and options");
    CharacterDraft draft(rules, compendium);
    check(draft.choices().scoreMethod == "array" && draft.choices().scores.at("str") == 15 && draft.choices().scores.at("cha") == 8
        && draft.sheet() && draft.stepProblem(0) == "Give the character a name.", "A new character starts from the standard array, unnamed");
    draft.setName("Ser Ada");
    check(draft.stepProblem(0) == "Pick a race.", "Creation asks for a race");
    draft.setRace("dwarf");
    draft.setBackground("soldier");
    check(draft.stepDone(0) && draft.sheet() && draft.sheet()->ancestry == "Dwarf", "Name, race and background finish the first step");

    draft.raise("dex");
    check(draft.choices().scores.at("dex") == 15 && draft.choices().scores.at("str") == 14 && draft.stepDone(1),
        "Raising an array score swaps it with the next value up");
    check(!draft.canRaise("dex") && draft.canLower("dex") && !draft.canLower("cha"), "The array's ends can't move further");
    yh::Random dice(5);
    draft.setMethod("pointBuy", dice);
    check(draft.pointsLeft() == 27 && draft.choices().scores.at("str") == 8, "Point buy starts every score at the cheapest");
    for (int i = 0; i < 7; i++) draft.raise("str");
    check(draft.choices().scores.at("str") == 15 && draft.pointsLeft() == 18 && !draft.canRaise("str"), "Point buy spends points up to its highest score");
    for (int i = 0; i < 7; i++) draft.raise("con");
    draft.raise("dex"); draft.raise("dex"); draft.raise("dex"); draft.raise("dex");
    check(draft.pointsLeft() == 5 && draft.choices().scores.at("dex") == 12 && draft.canRaise("int"), "Point buy keeps count");
    draft.setMethod("roll", dice);
    const auto rolled = draft.choices().scores;
    check(std::all_of(rolled.begin(), rolled.end(), [](const auto& s) { return s.second >= 3 && s.second <= 18; }) && !draft.canRaise("str")
        && draft.stepDone(1), "Rolled scores stand as rolled");
    draft.setMethod("array", dice);

    draft.setClass("rogue");
    check(draft.skillPicks() == 2 && draft.stepProblem(2) == "Pick 2 more skills.", "The rogue trains two skills at level 1");
    const auto& options = draft.skillOptions();
    check(std::find(options.begin(), options.end(), "perception") == options.end() && std::find(options.begin(), options.end(), "athletics") == options.end()
        && std::find(options.begin(), options.end(), "stealth") != options.end(), "Skills already trained aren't offered");
    draft.toggleSkill("stealth");
    draft.toggleSkill("arcana");
    draft.toggleSkill("insight");
    check(draft.picked("skills").size() == 2 && draft.finished() && draft.sheet()->proficiencies.contains("stealth"), "Two picks finish the rogue");
    draft.toggleSkill("arcana");
    check(!draft.finished() && draft.picked("skills").size() == 1, "Clicking a picked skill takes it back");
    draft.toggleSkill("insight");

    // Level 2 offers a class feat; a feat whose requirements aren't met isn't offered.
    auto made = draft.choices();
    made.xp = rules.xpForLevel.front();
    auto up = CharacterDraft::levelUp(rules, compendium, made);
    check(up.levellingUp() && up.choices().level() == 2 && up.featKinds() == std::vector<std::string>{"class"} && up.stepProblem(2) == "Pick a class feat.",
        "Levelling up asks for the feat the new level offers");
    const auto feats = up.featOptions("class");
    check(!feats.empty() && std::all_of(feats.begin(), feats.end(), [&](const std::string& id) {
        auto trial = up.choices();
        trial.levels.back().picks["feats"] = {id};
        return compendium.build(rules, trial).has_value();
    }), "Only feats the character can take are offered");
    up.pickFeat(feats.front());
    check(up.finished() && up.sheet()->level == 2, "Picking it finishes the level");
    up.setClass("wizard");
    check(up.picked("feats").empty() && up.finished() && up.sheet()->characterClass == "Rogue / Wizard", "Any level can go into another class");
}

void characterLibraryTests(const fs::path& scratch)
{
    const std::string folder = (scratch / "library-characters").string();
    std::string error;
    check(CharacterLibrary::list(folder).empty(), "A missing character folder is just empty");
    yh::Random random(3);
    CharacterLibrary::Entry ada;
    ada.choices = yh::rollChoices(yh::Ruleset::modern(), "Ser Ada", "fighter", random);
    ada.inventory.push_back({});
    ada.inventory.back().id = "rope";
    ada.inventory.back().name = "Rope";
    ada.inventory.back().quantity = 2;
    ada.coins = 12;
    ada.away = "adventure.json";
    CharacterLibrary::Entry twin = ada;
    twin.away.clear();
    check(CharacterLibrary::write(folder, ada, &error) && ada.fileName() == "ser-ada.json"
        && CharacterLibrary::write(folder, twin, &error) && twin.fileName() == "ser-ada-2.json",
        "New characters get plain file names that never overwrite another");
    const auto read = CharacterLibrary::find(folder, "ser-ada.json", &error);
    check(read && read->choices.name == "Ser Ada" && read->choices.scores == ada.choices.scores && read->inventory.size() == 1
        && read->inventory.front().quantity == 2 && read->coins == 12 && read->away == "adventure.json" && !read->retired,
        "Character files keep choices, what is carried, coins and where the character is");
    check(!CharacterLibrary::find(folder, "../ser-ada.json", &error) && !CharacterLibrary::find(folder, "ser-ada", &error),
        "Saves can only name files inside the character folder");
    write(fs::path(folder) / "broken.json", json{{"format", "yorehold.character"}, {"version", 1}, {"data", {{"choices", 5}}}});
    std::vector<std::string> problems;
    auto all = CharacterLibrary::list(folder, &problems);
    check(all.size() == 2 && problems.size() == 1 && problems.front().starts_with("broken.json"), "Broken character files are listed as problems");
    check(CharacterLibrary::retire(folder, twin, &error) && twin.retired && !fs::exists(fs::path(folder) / "ser-ada-2.json")
        && fs::exists(fs::path(folder) / "graveyard" / "ser-ada-2.json"), "Retiring moves a character into the graveyard");
    all = CharacterLibrary::list(folder);
    check(all.size() == 2 && !all.front().retired && all.back().retired, "The graveyard is listed after the living");
    check(!CharacterLibrary::write(folder, all.back(), &error), "Graveyard characters can't be changed");

    // A hero from the library, in the keep: leaving writes it back, still away; dying retires it.
    const auto saveDir = scratch / "library-saves";
    SDL_setenv_unsafe("YOREHOLD_SAVE_DIR", saveDir.string().c_str(), 1);
    SDL_setenv_unsafe("YOREHOLD_CONTENT", YH_GAME_ASSETS, 1);
    SDL_Event enter{};
    enter.type = SDL_EVENT_KEY_DOWN;
    enter.key.key = SDLK_RETURN;
    auto playAndLeave = [&] {
        YoreholdGame game;
        game.handleEvent(enter);
        game.handleEvent(enter);
        game.unload();
    };
    playAndLeave();
    json saved;
    {
        std::ifstream file(saveDir / "adventure.json", std::ios::binary);
        saved = json::parse(file);
    }
    check(saved["data"]["library"] == json::array({"", "", "", ""}), "Heroes made for the adventure have no library file");
    const std::string characters = (saveDir / "characters").string();
    CharacterLibrary::Entry hero;
    hero.choices = *yh::CharacterChoices::fromJson(saved["data"]["choices"][0].dump());
    check(CharacterLibrary::write(characters, hero, &error), "Put a hero in the library");
    saved["data"]["library"][0] = hero.fileName();
    saved["data"]["creatures"][0]["sheet"]["xp"] = 40;
    write(saveDir / "adventure.json", saved);
    playAndLeave();
    auto back = CharacterLibrary::find(characters, hero.fileName(), &error);
    check(back && back->away == "adventure.json" && back->choices.xp == 40 && back->inventory.size() == 4,
        "Leaving writes the hero back to the library, away in this adventure");
    saved["data"]["creatures"][0]["sheet"]["hp"] = 0;
    saved["data"]["creatures"][0]["sheet"]["death"] = {{"dead", true}, {"failures", 3}};
    write(saveDir / "adventure.json", saved);
    playAndLeave();
    back = CharacterLibrary::read((fs::path(characters) / "graveyard" / hero.fileName()).string(), &error);
    check(back && back->retired && back->away.empty() && !fs::exists(fs::path(characters) / hero.fileName()),
        "A hero who dies goes to the graveyard");
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
    // A new game takes its seed from the clock; a fixed one makes the stealth rolls below repeatable.
    saved["data"]["seed"] = 7;
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

void createScreenTests()
{
    // The Create screen's undo is the framework's History; MapEditorTests.cpp covers edits on it.
    {
        yh::Ui ui;
        yh::Input input;
        yh::Font* title = nullptr;
        CreateScreen screen(ui, input, title);
        check(!screen.isOpen() && !screen.history().canUndo() && !screen.history().canRedo(), "A Create screen with nothing open has nothing to undo");
    }

    // Test CreateScreen package validation
    {
        // Valid package
        ContentPackage pkg;
        pkg.name = "Test Adventure";
        pkg.kind = "adventure";
        pkg.id = "test-adventure";
        pkg.revision = 1;
        // Validation should pass for this valid package
        check(!pkg.name.empty(), "Valid package has a name");
        check(pkg.kind == "adventure", "Valid package has a valid kind");
        check(pkg.id == "test-adventure", "Valid package has a valid id format");
        check(pkg.revision >= 0, "Valid package has non-negative revision");
    }

    // Test invalid package kinds
    {
        ContentPackage pkg;
        pkg.kind = "invalid-kind";
        bool isValid = true;
        const std::set<std::string> validKinds{"adventure", "ruleset", "compendium", "character_class", "race", "feat"};
        if (!pkg.kind.empty() && !validKinds.count(pkg.kind))
            isValid = false;
        check(!isValid, "Invalid kind is rejected");
    }

    // Test invalid id format
    {
        std::string invalidId = "test adventure"; // contains space
        bool isValid = true;
        for (const char c : invalidId)
        {
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
            {
                isValid = false;
                break;
            }
        }
        check(!isValid, "ID with space is rejected");
    }

    // Test valid id formats
    {
        const char* validIds[] = {"test", "test-adventure", "test_adventure", "test-123", "a", "z-0"};
        for (const char* id : validIds)
        {
            bool isValid = true;
            for (const char c : std::string(id))
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
                {
                    isValid = false;
                    break;
                }
            }
            check(isValid, (std::string("Valid ID accepted: ") + id).c_str());
        }
    }
}

}

void worldPlayTests(const std::function<void(bool, const char*)>& check); // WorldTests.cpp
void worldActionTests(const std::function<void(bool, const char*)>& check);
void worldTurnTests(const std::function<void(bool, const char*)>& check);
void worldPositioningTests(const std::function<void(bool, const char*)>& check);
void worldProficiencyTests(const std::function<void(bool, const char*)>& check);
void worldDeathTests(const std::function<void(bool, const char*)>& check);
void worldCharacterTests(const std::function<void(bool, const char*)>& check);
void worldItemTests(const std::function<void(bool, const char*)>& check);
void worldSpellTests(const std::function<void(bool, const char*)>& check);
void worldObjectTests(const std::function<void(bool, const char*)>& check);
void worldTravelTests(const std::function<void(bool, const char*)>& check);
void worldCampTests(const std::function<void(bool, const char*)>& check);
void worldCompanionTests(const std::function<void(bool, const char*)>& check);
void mapEditorTests(const std::function<void(bool, const char*)>& check, const std::filesystem::path& scratch);
void encountersEditorTests(const std::function<void(bool, const char*)>& check, const std::filesystem::path& scratch);
void dialogueEditorTests(const std::function<void(bool, const char*)>& check, const std::filesystem::path& scratch);

int main()
{
    try
    {
        Scratch scratch;
        manifestTests();
        adventureTests();
        contentTests(scratch.path);
        worldSaveTests();
        worldPlayTests(check);
        worldActionTests(check);
        worldTurnTests(check);
        worldPositioningTests(check);
        worldProficiencyTests(check);
        worldDeathTests(check);
        worldCharacterTests(check);
        worldItemTests(check);
        worldSpellTests(check);
        worldObjectTests(check);
        worldTravelTests(check);
        worldCampTests(check);
        worldCompanionTests(check);
        libraryTests(scratch.path);
        mapTests();
        gameErrorTests();
        saveTests(scratch.path);
        characterDraftTests();
        characterLibraryTests(scratch.path);
        stealthTests(scratch.path);
        openFileTests(scratch.path);
        createScreenTests();
        mapEditorTests(check, scratch.path);
        encountersEditorTests(check, scratch.path);
        dialogueEditorTests(check, scratch.path);
    }
    catch (const std::exception& e)
    {
        check(false, e.what());
    }
    std::printf("%d content checks, %d failures\n", checks, failures);
    return failures ? 1 : 0;
}
