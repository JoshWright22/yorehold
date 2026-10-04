#include "YoreholdGame.h"

#include <yorehold/framework/assets/Skin.h>
#include <yorehold/framework/graphics/Renderer.h>
#include <yorehold/framework/debug/Profiler.h>
#include <yorehold/framework/map/Pathfinding.h>
#include <yorehold/framework/rpg/Random.h>
#include <yorehold/framework/save/SaveFile.h>

#include <nlohmann/json.hpp>

#include <SDL3/SDL_events.h>
#include <SDL3/SDL_filesystem.h>
#include <SDL3/SDL_keycode.h>
#include <SDL3/SDL_misc.h>
#include <SDL3/SDL_stdinc.h>
#include <SDL3/SDL_timer.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <queue>
#include <random>

namespace
{

constexpr float cell = GameMap::cellSize;

const yh::SaveFormat& saveFormat()
{
    static const yh::SaveFormat format = [] {
        yh::SaveFormat f("yorehold.adventure", 3);
        // v1 counted short rests left (2 per adventure); v2 counts uses per rest id.
        f.migrate(1, [](nlohmann::json& data) {
            const int left = data.at("restsLeft").get<int>();
            data.erase("restsLeft");
            data["restsUsed"] = {{"short", std::max(0, 2 - left)}};
        });
        // Earlier saves always belonged to the original keep, whose placements haven't changed.
        f.migrate(2, [](nlohmann::json& data) {
            data["chapterId"] = "goblin-keep";
            data["chapterFolder"] = "chapters/goblin-keep";
        });
        return f;
    }();
    return format;
}

float distance(yh::Vec2 a, yh::Vec2 b)
{
    const yh::Vec2 d = a - b;
    return std::sqrt(d.x * d.x + d.y * d.y);
}

// "{xp}" in chapter text becomes the number.
std::string fillXp(std::string text, int xp)
{
    for (size_t at; (at = text.find("{xp}")) != std::string::npos;)
        text.replace(at, 4, std::to_string(xp));
    return text;
}

}

YoreholdGame::YoreholdGame(std::vector<std::string> openFiles)
{
    files_.mountFolder(YH_FRAMEWORK_ASSETS, "framework");
    files_.mountFolder(YH_GAME_ASSETS, "game");
    applyScheme(yh::ControlPreset::BG3);
    tokens_.contextActions = {"Attack", "Talk", "Inspect"};

    // The UI-blocking input never sees a click, and its mouse sits far off the map.
    SDL_Event away{};
    away.type = SDL_EVENT_MOUSE_MOTION;
    away.motion.x = -100000;
    away.motion.y = -100000;
    noInput_.handle(away);

    // YOREHOLD_SEED replays the same adventure (for scripted tests).
    // Test runs skip the title screen and never touch the player's save, settings or library.
    const char* seed = SDL_getenv("YOREHOLD_SEED");
    testRun_ = seed != nullptr;
    aiNotes_ = SDL_getenv("YOREHOLD_AI_NOTES") != nullptr;
    // YOREHOLD_TIME=day|dusk|night overrides the map's time of day (test runs don't read the settings file).
    if (const char* time = SDL_getenv("YOREHOLD_TIME"); time && testRun_)
        settings_.timeOfDay = std::string_view(time) == "day" ? 1 : std::string_view(time) == "dusk" ? 2 : std::string_view(time) == "night" ? 3 : 0;
    if (!testRun_)
        loadSettings();
    applySettings();
    // YOREHOLD_SKIN picks a skin for a test run (which doesn't read the settings file).
    if (const char* skin = SDL_getenv("YOREHOLD_SKIN"))
        settings_.skin = skin;
    else if (!testRun_)
        prepareSkinsFolder();
    refreshSkins();

    refreshLibrary();
    // Start on the adventure picked last time, if it's still installed.
    size_t start = 0;
    for (size_t i = 0; i < adventures_.size(); i++)
    {
        const ContentLibrary::Adventure& a = adventures_[i];
        const bool installed = a.package.starts_with(libraryDir());
        const std::string file = installed ? std::filesystem::path(a.package).filename().string() : std::string();
        if (!SDL_getenv("YOREHOLD_CONTENT") && file == settings_.lastPackage && a.folder == settings_.lastFolder)
            start = i;
    }
    // YOREHOLD_CHAPTER picks another chapter folder (for testing content).
    if (const char* folder = SDL_getenv("YOREHOLD_CHAPTER"); folder && !adventures_.empty())
    {
        start = 0;
        adventures_.front().folder = folder;
        adventures_.front().title = folder;
    }
    openAdventure(start);
    menu_ = testRun_ && chapter_ ? Menu::None : Menu::Main;
    if (seed)
        newAdventure(std::strtoull(seed, nullptr, 10));
    // Scripted co-op tests: YOREHOLD_HOST hosts at once, YOREHOLD_JOIN=address joins.
    if (SDL_getenv("YOREHOLD_HOST") && chapter_)
        hostSession();
    if (const char* address = SDL_getenv("YOREHOLD_JOIN"); address && chapter_)
    {
        if (const char* name = SDL_getenv("YOREHOLD_NAME"))
            settings_.playerName = name;
        joinSession(address);
    }
    for (const std::string& file : openFiles)
        addContent(file);
    connectOnline();
}

// The account server comes from YOREHOLD_SERVER + YOREHOLD_SERVER_KEY (the dev script sets them
// when a local server is set up), else from the settings file. Without one the game is offline.
void YoreholdGame::connectOnline()
{
    const char* server = SDL_getenv("YOREHOLD_SERVER");
    const char* key = SDL_getenv("YOREHOLD_SERVER_KEY");
    const std::string address = server ? server : settings_.server;
    if (address.empty())
        return;
    if (const char* device = SDL_getenv("YOREHOLD_DEVICE"))
        settings_.deviceId = device;
    else if (testRun_)
        settings_.deviceId = "yorehold-test-run";
    else if (settings_.deviceId.size() < 10)
    {
        static constexpr char hex[] = "0123456789abcdef";
        std::random_device random;
        settings_.deviceId.clear();
        for (int i = 0; i < 32; i++)
            settings_.deviceId += hex[random() % 16];
        saveSettings();
    }
    online_.connect(address, key ? key : settings_.serverKey, settings_.deviceId);
}

// ---------------------------------------------------------------- content library

std::string YoreholdGame::libraryDir() const
{
    const std::string dir = stateDir();
    return dir.empty() ? std::string() : dir + "library";
}

void YoreholdGame::refreshLibrary()
{
    adventures_.clear();
    packages_.clear();
    chapterError_.clear();
    compendium_ = {};

    // YOREHOLD_CONTENT plays a folder or .yore in place, without installing it (for testing content).
    if (const char* source = SDL_getenv("YOREHOLD_CONTENT"))
    {
        if (const auto package = ContentLibrary::inspect(source, &chapterError_))
            adventures_ = package->adventures;
        return; // broken content stays on the title with its error instead of quietly playing something else
    }

    std::string error;
    if (auto builtIn = ContentLibrary::inspect(YH_GAME_ASSETS, &error))
    {
        for (ContentLibrary::Adventure& a : builtIn->adventures)
        {
            a.package.clear();
            a.packageName.clear();
            adventures_.push_back(std::move(a));
        }
    }
    else
        chapterError_ = error;

    if (testRun_ || libraryDir().empty())
        return;
    std::vector<std::string> problems;
    packages_ = ContentLibrary::installed(libraryDir(), &problems);
    for (const ContentLibrary::Package& package : packages_)
        adventures_.insert(adventures_.end(), package.adventures.begin(), package.adventures.end());
    for (const std::string& problem : problems)
        std::fprintf(stderr, "Library: %s\n", problem.c_str());
    compendium_ = ContentLibrary::compendium(YH_GAME_ASSETS, packages_);
}

bool YoreholdGame::installedAdventure() const
{
    return adventure_ < adventures_.size() && !libraryDir().empty() && adventures_[adventure_].package.starts_with(libraryDir());
}

bool YoreholdGame::openAdventure(size_t index)
{
    // Art and fonts are cached by path, and two packages can use the same paths.
    releaseAssets();
    files_.unmount("skin");
    files_.unmount("import");
    chapter_.reset();
    themePath_.clear();
    cameraPlaced_ = false;
    if (index >= adventures_.size())
    {
        if (chapterError_.empty())
            chapterError_ = "No adventures are installed.";
        std::fprintf(stderr, "Chapter failed to load: %s\n", chapterError_.c_str());
        hasSave_ = false;
        newAdventure(0);
        return false;
    }
    adventure_ = index;
    adventurePage_ = index / 5;
    const ContentLibrary::Adventure& adventure = adventures_[index];
    chapterError_.clear();
    if (!adventure.package.empty() && !ContentPackage::mount(files_, adventure.package, "import"))
        chapterError_ = "Couldn't open content: " + adventure.package;
    if (chapterError_.empty())
        if (const auto content = ContentPackage::load(files_, &chapterError_))
            themePath_ = content->theme;
    std::optional<Chapter> chapter;
    if (chapterError_.empty())
        chapter = Chapter::load(files_, adventure.folder, &chapterError_);
    if (chapter)
    {
        chapter_ = std::make_unique<Chapter>(std::move(*chapter));
        rules_ = chapter_->rules;
        camera_.setBounds(map().map().worldBounds());
    }
    else
        std::fprintf(stderr, "Chapter failed to load: %s\n", chapterError_.c_str());
    mountSkin(); // on top of the adventure's own art
    hasSave_ = !testRun_ && chapter_ && saveFormat().readFile(savePath()).has_value();
    newAdventure(SDL_GetTicks());
    return chapter_ != nullptr;
}

void YoreholdGame::selectAdventure(size_t index)
{
    if (!openAdventure(index))
        return;
    settings_.lastPackage = installedAdventure() ? std::filesystem::path(adventures_[index].package).filename().string() : std::string();
    settings_.lastFolder = adventures_[index].folder;
    saveSettings();
}

void YoreholdGame::addContent(const std::string& file)
{
    if (testRun_ || libraryDir().empty())
        return;
    if (encounter_ && !encounter_->finished() && !onTitle())
    {
        say("Finish the fight before adding content.");
        return;
    }
    if (!onTitle())
        saveAdventure();
    autoPlay_ = false;

    // What was selected, to come back to it if the file adds nothing playable.
    const std::string oldPackage = adventure_ < adventures_.size() ? adventures_[adventure_].package : std::string();
    const std::string oldFolder = adventure_ < adventures_.size() ? adventures_[adventure_].folder : std::string();
    // An installed file being replaced must not be open while it is overwritten.
    releaseAssets();
    files_.unmount("import");

    std::error_code problem;
    const std::filesystem::path source = std::filesystem::absolute(file, problem);
    std::string error;
    const auto package = ContentLibrary::install(source.string(), libraryDir(), &error);
    refreshLibrary();
    std::string wantPackage = oldPackage, wantFolder = oldFolder;
    noticeBad_ = !package;
    if (!package)
        notice_ = "Couldn't add " + source.filename().string() + ": " + error;
    else
    {
        auto count = [](size_t n, const char* one) { return std::to_string(n) + " " + one + (n == 1 ? "" : (std::string_view(one) == "class" ? "es" : "s")); };
        notice_ = "Added " + package->name + ": " + count(package->adventures.size(), "adventure") + ", " + count(package->classes, "class")
            + ", " + count(package->items, "item") + ", " + count(package->creatures, "creature");
        if (!package->adventures.empty())
        {
            wantPackage = package->path;
            wantFolder = package->adventures.front().folder;
        }
    }
    size_t index = 0;
    for (size_t i = 0; i < adventures_.size(); i++)
        if (adventures_[i].package == wantPackage && adventures_[i].folder == wantFolder)
            index = i;
    if (package && !package->adventures.empty())
        selectAdventure(index);
    else
        openAdventure(index);
    menu_ = package && !package->adventures.empty() ? Menu::Play : Menu::Adventures;
}

YoreholdGame::~YoreholdGame() = default;

void YoreholdGame::unload()
{
    if (!onTitle())
        saveAdventure();
    releaseAssets();
}

// ---------------------------------------------------------------- skins

// A skin is a folder (or a .yoreskin zip of one) in the skins folder, laid out like the game's
// own assets: ui/button.png, ui/theme.json, fonts/... It only needs the files it changes;
// everything else comes from the defaults underneath.
std::string YoreholdGame::skinsDir() const
{
    const std::string dir = stateDir();
    return dir.empty() ? std::string() : dir + "skins";
}

void YoreholdGame::refreshSkins()
{
    skins_.clear();
    std::error_code error;
    const std::filesystem::path dir(skinsDir());
    if (skinsDir().empty() || !std::filesystem::is_directory(dir, error))
        return;
    for (const auto& entry : std::filesystem::directory_iterator(dir, error))
    {
        if (entry.is_directory(error) || entry.path().extension() == ".yoreskin")
            skins_.push_back(entry.path().filename().string());
    }
    std::sort(skins_.begin(), skins_.end());
}

// Puts the selected skin on top of everything else mounted, and drops cached art so it shows.
void YoreholdGame::mountSkin()
{
    releaseAssets();
    files_.unmount("skin");
    if (settings_.skin.empty() || skinsDir().empty())
        return;
    const std::filesystem::path path = std::filesystem::path(skinsDir()) / settings_.skin;
    std::error_code error;
    const bool mounted = std::filesystem::is_directory(path, error) ? files_.mountFolder(path.string(), "skin")
                       : std::filesystem::is_regular_file(path, error) && files_.mountZip(path.string(), "skin");
    if (!mounted)
        std::fprintf(stderr, "Skin \"%s\" couldn't be opened; using the default.\n", settings_.skin.c_str());
    // Looks and sounds only: a skin can't replace chapters, creatures or rules.
    files_.restrict("skin", {"ui", "fonts", "tokens", "particles", "sounds", "music"});
}

// The first time, the skins folder gets a copy of the default UI to start a skin from.
void YoreholdGame::prepareSkinsFolder()
{
    std::error_code error;
    const std::filesystem::path dir(skinsDir());
    if (skinsDir().empty() || std::filesystem::exists(dir, error))
        return;
    const std::filesystem::path copy = dir / "Default copy" / "ui";
    std::filesystem::create_directories(copy, error);
    for (const std::string& file : files_.list("ui"))
    {
        const auto bytes = files_.read(file);
        std::ofstream out(copy / std::filesystem::path(file).filename(), std::ios::binary);
        if (bytes && out)
            out.write(reinterpret_cast<const char*>(bytes->data()), static_cast<std::streamsize>(bytes->size()));
    }
    yh::writeFileAtomically((dir / "readme.txt").string(),
        "Yorehold skins\n"
        "==============\n\n"
        "Each folder in here is a skin (a .yoreskin file, which is a zip of such a folder, works too).\n"
        "Pick one in Settings > Skin. A skin only needs the files it changes; anything missing comes\n"
        "from the default look.\n\n"
        "\"Default copy\" is the default UI to start from: copy the folder, rename it, and edit the images.\n"
        "Changes show in the game as soon as you save a file.\n\n"
        "ui/panel.png                 panels and windows\n"
        "ui/button.png                buttons, plus -hover, -pressed and -disabled\n"
        "ui/button-selected.png       drawn over the chosen option in a row of options\n"
        "ui/checkbox-off.png, -on     the tick box at the left of a setting\n"
        "ui/textbox.png, -focus       text fields\n"
        "ui/bar-back.png, bar-fill    health bars and sliders (the fill is tinted, so draw it in white and greys)\n"
        "ui/slider-knob.png           the slider handle\n"
        "ui/theme.json                text colours, the drop shadow, and \"slice\": how many pixels at each\n"
        "                             image's edge are corners that don't stretch\n"
        "fonts/                       replace a font by giving a file the same name as the game's\n",
        false);
}

void YoreholdGame::releaseAssets()
{
    ui_.theme.font = nullptr;
    ui_.theme.images = {};
    title_ = nullptr;
    tokens_.initialFont = nullptr;
    tokens_.labelFont = nullptr;
    tokens_.images = {};
    assets_.reset();
}

void YoreholdGame::newAdventure(uint64_t seed)
{
    seed_ = seed;
    fights_ = 0;
    restsUsed_.clear();
    restRandom_ = yh::Random(seed ^ 0x5eedull);
    encounter_.reset();
    reach_.clear();
    pendingAttack_.reset();
    floaters_.clear();
    log_.clear();
    fog_.reset(0);
    tokens_.clearLinks();
    tokens_.tokens.clear();
    tokens_.settings.inCombat = false;
    tokens_.settings.activeTurn.reset();
    creatures_.clear();
    cutscene_ = {};
    cutsceneDone_ = false;
    autoExploreStuck_ = 0;
    flags_.clear();
    journal_.reset();
    journalOpen_ = false;
    talk_.reset();
    pendingTalk_.reset();
    rolls_ = 0;
    pendingStep_.reset();

    heroCount_ = 0;
    if (!chapter_)
        return;
    if (!chapter_->quests.empty())
        if (const std::optional<std::string> text = files_.readText(chapter_->quests))
            journal_ = yh::QuestJournal::fromJson(*text); // Chapter::load already checked it
    fog_ = yh::FogOfWar(map().width(), map().height(), GameMap::cellSize);
    lightLevels_ = yh::LightLevels(map().width(), map().height(), GameMap::cellSize);
    lightLevels_.ambient = map().lighting().ambient;
    lightLevels_.brightFraction = map().lighting().brightFraction;
    {
        std::vector<yh::Light> fixed;
        for (const GameMap::Light& l : map().lights())
            fixed.push_back({l.position, l.radius, l.color});
        lightLevels_.setFixed(fixed, map().walls());
    }

    // Everything below comes from the chapter's files; Chapter::load already checked the ids.
    yh::Random random(seed);
    for (const Chapter::PartyMember& member : chapter_->party)
    {
        creatures_.push_back({*chapter_->compendium.makeCharacter(rules_, member.classId, member.name, random)});
        yh::Token token;
        token.name = member.name;
        token.color = member.color;
        token.radius = cell * 0.4f;
        token.position = grid_.center(member.at);
        token.owner = tokens_.tokens.size() < seats_.size() ? seats_[tokens_.tokens.size()] : 0;
        tokens_.tokens.push_back(token);
        // In auto-play heroes fight like goblins that never run.
        yh::AiProfile& ai = creatures_.back().ai;
        ai = *yh::AiProfile::preset("cunning");
        ai.fleeHp = 0;
        ai.fleeLosses = 2;
        ai.fleeLeaderless = false;
    }
    heroCount_ = creatures_.size();
    selectOwnHero();
    for (size_t group = 0; group < chapter_->encounters.size(); group++)
    {
        for (const Chapter::Placement& placement : chapter_->encounters[group].creatures)
        {
            const yh::CreatureDefinition& definition = *chapter_->compendium.creature(placement.creatureId);
            creatures_.push_back({*chapter_->compendium.makeCreature(rules_, placement.creatureId, placement.name, random), 1,
                static_cast<int>(group)});
            creatures_.back().ai = definition.ai;
            yh::Token token;
            token.name = creatures_.back().sheet.name;
            token.owner = enemyOwner;
            token.color = definition.token.color;
            token.image = definition.token.image;
            token.radius = cell * definition.token.size;
            token.position = grid_.center(placement.at);
            token.floor = hidden;
            tokens_.tokens.push_back(token);
        }
    }
    npcStart_ = creatures_.size();
    for (size_t i = 0; i < chapter_->npcs.size(); i++)
    {
        const Chapter::Npc& npc = chapter_->npcs[i];
        const yh::CreatureDefinition& definition = *chapter_->compendium.creature(npc.creature);
        Creature creature{*chapter_->compendium.makeCreature(rules_, npc.creature, npc.name, random), 2,
            static_cast<int>(chapter_->encounters.size() + i)};
        creature.npc = static_cast<int>(i);
        creature.ai = definition.ai;
        creatures_.push_back(std::move(creature));
        yh::Token token;
        token.name = npc.name;
        token.owner = npcOwner;
        token.color = npc.color;
        token.radius = cell * definition.token.size;
        token.position = grid_.center(npc.at);
        token.floor = hidden;
        tokens_.tokens.push_back(token);
    }
    for (size_t i = 1; i < heroCount_; i++)
        tokens_.link(i, i - 1);

    cameraPlaced_ = false;
    for (const std::string& line : chapter_->intro)
        say(line);
    banner_ = chapter_->title;
    bannerTime_ = 3;
}

void YoreholdGame::applyScheme(yh::ControlPreset preset)
{
    scheme_ = yh::makeControlScheme(preset);
    input_.setMap(scheme_.map);
    controls_.settings.edgeScroll = scheme_.edgeScroll;
}

void YoreholdGame::say(std::string line)
{
    static const bool printLog = SDL_getenv("YOREHOLD_PRINT_LOG") != nullptr;
    if (autoPlay_ || printLog) // headless runs read the story from stdout
        std::printf("log: %s\n", line.c_str());
    log_.push_back(std::move(line));
    if (log_.size() > 200)
        log_.erase(log_.begin(), log_.begin() + 50);
}

void YoreholdGame::syncLog()
{
    if (!encounter_)
        return;
    const std::vector<std::string>& lines = encounter_->log();
    for (; encounterLogShown_ < lines.size(); encounterLogShown_++)
        say(lines[encounterLogShown_]);
}

bool YoreholdGame::handleEvent(const SDL_Event& event)
{
    const bool keyDown = event.type == SDL_EVENT_KEY_DOWN && !event.key.repeat;
    if (event.type == SDL_EVENT_DROP_FILE && event.drop.data)
    {
        addContent(event.drop.data); // a .yore dragged onto the window
        return true;
    }
    if (keyDown && event.key.key == SDLK_F6)
    {
        settings_.controls = scheme_.preset == yh::ControlPreset::Foundry ? yh::ControlPreset::BG3 : yh::ControlPreset::Foundry;
        applySettings();
        saveSettings();
        say(std::string(scheme_.name) + " controls");
        return true;
    }
    if (cutscene_.running() && !onTitle())
    {
        // Space, Esc or a click skips; everything else is ignored while it plays.
        const bool skip = (event.type == SDL_EVENT_KEY_DOWN && !event.key.repeat
                              && (event.key.key == SDLK_SPACE || event.key.key == SDLK_ESCAPE || event.key.key == SDLK_RETURN))
            || event.type == SDL_EVENT_MOUSE_BUTTON_DOWN;
        if (skip)
        {
            cutscene_.skip(camera_);
            finishAdventure();
        }
        return true;
    }
    if (keyDown && event.key.key == SDLK_F8)
    {
        aiNotes_ = !aiNotes_;
        say(aiNotes_ ? "AI notes on (F8): the log explains each creature's choice." : "AI notes off.");
        return true;
    }
    if (keyDown && event.key.key == SDLK_F9)
    {
        if (!chapter_ || client_)
            return true;
        if (onTitle())
            startNew();
        menu_ = Menu::None;
        autoPlay_ = !autoPlay_;
        say(autoPlay_ ? "Auto-play on (F9): the party explores and fights by itself." : "Auto-play off.");
        return true;
    }
    if (menu_ != Menu::None)
    {
        // Menus: Enter = the first choice on the main/play menus, Esc = back.
        if (keyDown && (event.key.key == SDLK_RETURN || event.key.key == SDLK_KP_ENTER) && (menu_ == Menu::Main || menu_ == Menu::Play))
        {
            if (menu_ == Menu::Main)
                openMenu(Menu::Play);
            else if (hasSave_)
                continueSaved();
            else
                startNew();
            return true;
        }
        if (keyDown && event.key.key == SDLK_ESCAPE)
        {
            openMenu(menu_ == Menu::Settings ? settingsBack_ : menu_ == Menu::Pause ? Menu::None
                    : menu_ == Menu::Adventures || menu_ == Menu::Join ? Menu::Play : Menu::Main);
            return true;
        }
        input_.handle(event); // the menus' buttons read the mouse
        return true;
    }
    if (talk_)
    {
        // 1-9 pick a reply, Esc walks away; the mouse still reaches the reply buttons.
        if (keyDown && event.key.key >= SDLK_1 && event.key.key <= SDLK_9)
            act("reply", nlohmann::json{{"choice", event.key.key - SDLK_1}, {"hero", leaderIndex()}}.dump());
        else if (keyDown && event.key.key == SDLK_ESCAPE)
            act("leave");
        else
            input_.handle(event);
        return true;
    }
    if (keyDown && event.key.key == SDLK_J && journal_)
    {
        journalOpen_ = !journalOpen_;
        return true;
    }
    if (keyDown && event.key.key == SDLK_ESCAPE)
    {
        if (journalOpen_)
            journalOpen_ = false;
        else
            openMenu(Menu::Pause);
        return true;
    }
    if (keyDown && event.key.key == SDLK_R && !rules_.rests.empty())
    {
        act("rest", R"({"rest": 0})"); // R = the ruleset's first (usually shortest) rest
        return true;
    }
    input_.handle(event);
    return true;
}

std::string YoreholdGame::describe() const
{
    if (!chapter_)
        return "screen: content error " + chapterError_;
    if (onTitle())
        return "screen: title";
    if (partyDown())
        return "screen: game over";
    if (encounter_ && !encounter_->finished())
        return "screen: combat round " + std::to_string(encounter_->round());
    if (talk_ && talk_->current())
        return "screen: talking " + chapter_->npcs[talkNpc_].id + " at " + talk_->current()->id;
    return "screen: exploring";
}

// ---------------------------------------------------------------- update

void YoreholdGame::update(double deltaSeconds)
{
    time_ += deltaSeconds;
    // Edited skin or art files show up straight away: everything reloads on the next draw.
    reloadTimer_ -= deltaSeconds;
    if (skinChanged_)
    {
        skinChanged_ = false;
        mountSkin();
    }
    else if (reloadTimer_ <= 0)
    {
        reloadTimer_ = 0.5;
        if (!files_.pollChanges().empty())
            releaseAssets();
    }
    updateSession(deltaSeconds);
    online_.update();
    if (online_.status() != onlineStatus_)
    {
        onlineStatus_ = online_.status();
        if (SDL_getenv("YOREHOLD_PRINT_LOG"))
            std::printf("[online] %s\n", onlineStatus_.c_str());
    }
    // The title and pause menus freeze the world, except in co-op, where it carries on for everyone else.
    const bool pauseMenu = menu_ == Menu::Pause || (menu_ == Menu::Settings && settingsBack_ == Menu::Pause);
    if (!chapter_ || (menu_ != Menu::None && !(inSession() && pauseMenu)))
        return;
    const bool menuOpen = menu_ != Menu::None;
    if (cutscene_.running())
    {
        cutscene_.update(deltaSeconds, camera_, [this](std::string_view event) {
            if (event == "finished")
                cutsceneDone_ = true;
        });
        if (cutsceneDone_ || !cutscene_.running())
            finishAdventure();
        return;
    }
    bannerTime_ = std::max(0.0, bannerTime_ - deltaSeconds);
    const bool fighting = encounter_ && !encounter_->finished();
    const std::optional<size_t> current = currentCreature();
    const bool heroTurn = current && creatures_[*current].team == 0;
    const bool mouseOnUi = overUi(input_.mouse());

    // Exploring, each player walks their own heroes. In a fight, steps are commands (updateHeroTurn).
    const yh::TokenController::Passable passable = [this](yh::Cell c) { return walkable(c); };
    const bool tokensListen = !menuOpen && !mouseOnUi && !partyDown() && !talk_ && !fighting;
    const yh::Input& tokenInput = tokensListen ? input_ : noInput_;
    tokens_.update(tokenInput, camera_, grid_, passable, deltaSeconds);
    if (&tokenInput == &input_ && input_.clicked(yh::actions::moveTo))
        controls_.resumeFollowing();
    shareWalking(deltaSeconds);

    // Clicking someone to talk to walks the leader over; the conversation opens on arrival.
    if (tokensListen && (input_.clicked(yh::actions::moveTo) || input_.clicked(yh::actions::select)))
        if (const std::optional<size_t> npc = hoveredNpc())
            walkToTalk(*npc);
    if (pendingTalk_ && !fighting)
    {
        const size_t leader = leaderIndex();
        if (tokens_.tokens[leader].path.empty())
        {
            const size_t npc = *pendingTalk_;
            pendingTalk_.reset();
            if (peaceful(npc) && grid_.distance(cellOf(leader), cellOf(npcToken(npc))) <= 1.5f)
                act("talk", nlohmann::json{{"npc", npc}}.dump());
            else
                say("Can't reach " + chapter_->npcs[npc].name + " from here.");
        }
    }

    // Right-click menu. On a peaceful NPC: Talk walks over, Attack picks a fight with them.
    const std::optional<size_t> menuNpc = tokens_.contextChoice && tokens_.contextChoice->first < creatures_.size()
            && creatures_[tokens_.contextChoice->first].npc >= 0 && creatures_[tokens_.contextChoice->first].team == 2
        ? std::optional<size_t>(creatures_[tokens_.contextChoice->first].npc) : std::nullopt;
    if (menuNpc && tokens_.contextChoice->second != "Inspect")
    {
        if (fighting)
            say("Not in the middle of a fight.");
        else if (tokens_.contextChoice->second == "Attack")
            act("provoke", nlohmann::json{{"npc", *menuNpc}}.dump());
        else
            walkToTalk(*menuNpc);
    }
    else if (tokens_.contextChoice && tokens_.contextChoice->first < creatures_.size())
    {
        const auto [index, action] = *tokens_.contextChoice;
        if (action == "Attack" && fighting && heroTurn && creatures_[index].team == 1 && mine(*current))
            tryAttack(index);
        else if (action == "Inspect" || action == "Attack")
        {
            const yh::Character& c = creatures_[index].sheet;
            say(c.name + ": " + c.characterClass + ", HP " + std::to_string(c.hp) + "/" + std::to_string(c.maxHp()) + ", AC " +
                std::to_string(c.armorClass(rules_)));
        }
    }

    // The host runs the enemies (and the heroes in auto-play); a hero's own player runs their turn.
    if (const std::optional<size_t> now = currentCreature())
    {
        if (creatures_[*now].team == 0 && !autoPlay_)
        {
            if (!menuOpen && mine(*now))
                updateHeroTurn();
        }
        else if (!client_)
            updateEnemyTurn(deltaSeconds);
    }
    else if (autoPlay_ && !partyDown() && !client_)
        autoExplore();
    updateVisibility();

    std::optional<yh::Vec2> follow = tokens_.followTarget();
    if (const std::optional<size_t> now = currentCreature(); now && encounter_ && !encounter_->finished())
        follow = tokens_.tokens[*now].position;
    controls_.update(camera_, input_, deltaSeconds, follow);

    for (Floater& floater : floaters_)
        floater.age += static_cast<float>(deltaSeconds);
    std::erase_if(floaters_, [](const Floater& f) { return f.age > 1.4f; });
}

// Wall cells are never in line of sight (their centre is behind the wall edge), so show the ones
// bordering what a view sees.
void YoreholdGame::revealWalls(int team)
{
    for (int y = 0; y < map().height(); y++)
    {
        for (int x = 0; x < map().width(); x++)
        {
            if (map().blocksSight({x, y}) || fog_.state(team, 0, {x, y}) != yh::FogState::Visible)
                continue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (map().inside({x + dx, y + dy}) && map().blocksSight({x + dx, y + dy}))
                        fog_.reveal(team, 0, {x + dx, y + dy});
        }
    }
}

GameMap::LightingMode YoreholdGame::lightingMode() const
{
    if (settings_.lighting >= 1 && settings_.lighting <= 3)
        return static_cast<GameMap::LightingMode>(settings_.lighting - 1);
    return map().lighting().mode;
}

GameMap::Time YoreholdGame::timeOfDay() const
{
    // There's no sky to change underground.
    if (map().lighting().time != GameMap::Time::Underground && settings_.timeOfDay >= 1 && settings_.timeOfDay <= 3)
        return static_cast<GameMap::Time>(settings_.timeOfDay - 1);
    return map().lighting().time;
}

int YoreholdGame::viewTeam() const
{
    if (settings_.sharedFog)
        return 0;
    // Whoever's turn it is in a fight, otherwise the selected (leading) hero.
    if (const std::optional<size_t> current = currentCreature(); current && *current < heroCount_)
        return static_cast<int>(*current) + 1;
    for (size_t i = 0; i < heroCount_; i++)
        if (tokens_.tokens[i].selected)
            return static_cast<int>(i) + 1;
    return 1;
}

void YoreholdGame::updateVisibility()
{
    const GameMap::Lighting& lighting = map().lighting();
    const bool rules = lightingMode() == GameMap::LightingMode::Rules;
    const GameMap::Sky sky = map().sky(timeOfDay());
    std::vector<yh::Vision> eyes(heroCount_);
    std::vector<yh::Light> carried;
    for (size_t i = 0; i < heroCount_; i++)
    {
        if (tokens_.tokens[i].floor == dead)
            continue;
        const float darkvision = creatures_[i].sheet.stats.value("darkvision") / std::max(1, rules_.feetPerSquare) * cell;
        eyes[i] = {tokens_.tokens[i].position, sky.sight * cell, darkvision};
        if (lighting.carried > 0)
            carried.push_back({tokens_.tokens[i].position, lighting.carried * cell});
    }
    // In rules mode a cell is only seen if some light reaches it (or it's within darkvision).
    // By day the outdoors is lit by the sky and seen from far off; under a roof it's the usual
    // sight distance and the map's own lights.
    std::function<bool(yh::Cell)> lit;
    if (rules || sky.differs)
    {
        lit = [&, rules](yh::Cell c) {
            if (sky.differs && !map().indoors(c))
                return !rules || sky.level != yh::LightLevel::Dark || lightLevels_.lit(c, carried, map().walls());
            if (sky.differs)
            {
                const yh::Vec2 at = grid_.center(c);
                const float reach = lighting.sight * cell;
                bool near = false;
                for (size_t i = 0; i < heroCount_ && !near; i++)
                    near = tokens_.tokens[i].floor != dead && distance(at, tokens_.tokens[i].position) <= reach;
                if (!near)
                    return false;
            }
            return !rules || lightLevels_.lit(c, carried, map().walls());
        };
    }

    // Team 0 is everyone's view together; it decides when enemies are spotted. With shared fog
    // off, each hero also keeps a view of their own (team 1 + index) for the screen.
    std::vector<yh::Vision> all;
    for (size_t i = 0; i < heroCount_; i++)
        if (tokens_.tokens[i].floor != dead)
            all.push_back(eyes[i]);
    fog_.update(0, 0, all, map().walls(), lit);
    revealWalls(0);
    if (!settings_.sharedFog)
    {
        for (size_t i = 0; i < heroCount_; i++)
        {
            const std::span<const yh::Vision> mine = tokens_.tokens[i].floor == dead ? std::span<const yh::Vision>() : std::span(&eyes[i], 1);
            fog_.update(static_cast<int>(i) + 1, 0, mine, map().walls(), lit);
            revealWalls(static_cast<int>(i) + 1);
        }
    }
    const int view = viewTeam();

    const bool fighting = encounter_ && !encounter_->finished();
    for (size_t i = heroCount_; i < creatures_.size(); i++)
    {
        yh::Token& token = tokens_.tokens[i];
        if (token.floor == dead)
            continue;
        const bool seen = fog_.state(0, 0, cellOf(i)) == yh::FogState::Visible;
        token.floor = fog_.state(view, 0, cellOf(i)) == yh::FogState::Visible ? 0 : hidden;
        // The host decides when a fight starts and sends everyone's positions with it.
        if (seen && !fighting && !partyDown() && creatures_[i].team == 1 && !creatures_[i].awake && !client_ && !talk_)
        {
            nlohmann::json at = nlohmann::json::array();
            for (size_t c = 0; c < creatures_.size(); c++)
            {
                at.push_back({tokens_.tokens[c].position.x, tokens_.tokens[c].position.y});
            }
            act("fight", nlohmann::json{{"group", creatures_[i].group}, {"at", at}}.dump());
            return;
        }
    }
}

void YoreholdGame::startCombat(int group)
{
    // Everyone stops on a square of their own.
    std::vector<yh::Cell> taken;
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        yh::Token& token = tokens_.tokens[i];
        token.path.clear();
        if (token.floor == dead)
            continue;
        yh::Cell spot = grid_.cellAt(token.position);
        for (int radius = 0; radius < 4; radius++)
        {
            bool found = false;
            for (int dy = -radius; dy <= radius && !found; dy++)
                for (int dx = -radius; dx <= radius && !found; dx++)
                {
                    const yh::Cell c{spot.x + dx, spot.y + dy};
                    if (map().walkable(c) && std::find(taken.begin(), taken.end(), c) == taken.end())
                    {
                        spot = c;
                        found = true;
                    }
                }
            if (found)
                break;
        }
        taken.push_back(spot);
        token.position = grid_.center(spot);
    }

    encounter_ = std::make_unique<yh::Encounter>(rules_, seed_ * 7919 + static_cast<uint64_t>(++fights_));
    encounterLogShown_ = 0;
    sideAtStart_[0] = sideAtStart_[1] = 0;
    hadLeader_[0] = hadLeader_[1] = false;
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        Creature& c = creatures_[i];
        c.fleeing = false;
        if (c.sheet.down())
            continue;
        if (c.team == 0)
            encounter_->add(c.sheet, 0);
        else if (c.group == group && c.team == 1)
        {
            c.awake = true;
            encounter_->add(c.sheet, 1);
        }
        else
            continue;
        sideAtStart_[c.team]++;
        hadLeader_[c.team] |= c.ai.leader;
    }

    if (group < static_cast<int>(chapter_->encounters.size()) && !chapter_->encounters[group].text.empty())
        say(chapter_->encounters[group].text);
    tokens_.settings.inCombat = true;
    encounter_->start();
    syncLog();
    banner_ = "Combat";
    bannerTime_ = 1.5;
    beginTurn();
}

void YoreholdGame::endCombat()
{
    tokens_.settings.inCombat = false;
    tokens_.settings.activeTurn.reset();
    reach_.clear();
    pendingAttack_.reset();
    for (yh::Token& token : tokens_.tokens)
        token.selected = false;
    pendingStep_.reset();

    if (encounter_->winningTeam() != 0)
    {
        banner_ = chapter_->defeatText;
        bannerTime_ = 1e9;
        say(chapter_->defeatText);
        return;
    }

    // Healing after a win, as the ruleset says: revive the downed, then any victory recovery.
    restRandom_ = nextRandom(0x5eedull);
    for (size_t i = 0; i < heroCount_; i++)
    {
        Creature& c = creatures_[i];
        if (c.sheet.down() && rules_.reviveAfterVictory > 0)
        {
            c.sheet.hp = std::min(rules_.reviveAfterVictory, c.sheet.maxHp());
            say(c.sheet.name + " gets back up with " + std::to_string(c.sheet.hp) + " HP.");
        }
        if (const int healed = c.sheet.recover(rules_, rules_.afterVictory, restRandom_); healed > 0)
            say(c.sheet.name + " recovers " + std::to_string(healed) + " HP.");
        if (!c.sheet.down())
            tokens_.tokens[i].floor = 0;
        c.sheet.addXp(rules_, chapter_->xpPerVictory);
    }
    selectOwnHero();
    say(fillXp(chapter_->victoryText, chapter_->xpPerVictory));

    // Every encounter with nobody left standing sets its story flags.
    std::vector<std::string> won;
    for (size_t group = 0; group < chapter_->encounters.size(); group++)
    {
        const bool beaten = std::none_of(creatures_.begin() + heroCount_, creatures_.end(),
            [&](const Creature& c) { return c.group == static_cast<int>(group) && !c.sheet.down(); });
        if (beaten)
            won.insert(won.end(), chapter_->encounters[group].set.begin(), chapter_->encounters[group].set.end());
    }
    setFlags(won);

    if (chapterCleared())
    {
        playEnding();
        return;
    }
    else
    {
        banner_ = "Victory";
        bannerTime_ = 2;
        if (!rules_.rests.empty() && restsLeft(rules_.rests.front()) != 0)
            say("Hurt? Rest (R) before pushing on.");
    }
    saveAdventure();
}

void YoreholdGame::playEnding()
{
    std::string error = "not found";
    const std::string& path = chapter_->clearedCutscene;
    const std::optional<std::string> text = path.empty() ? std::nullopt : files_.readText(path);
    std::optional<yh::Cutscene> ending = text ? yh::Cutscene::fromJson(*text, &error) : std::nullopt;
    if (!ending)
    {
        // No (working) cutscene: a plain banner and Play again.
        if (!path.empty())
            say("Ending cutscene " + path + ": " + error);
        banner_ = chapter_->clearedText;
        bannerTime_ = 1e9;
        say(chapter_->clearedText);
        return;
    }
    cutscene_ = std::move(*ending);
    cutsceneDone_ = false;
    bannerTime_ = 0;
    controls_.resumeFollowing();
    cutscene_.start();
}

void YoreholdGame::finishAdventure()
{
    // In co-op the host starts the next run for everyone; joined players wait for it.
    if (client_)
    {
        cutscene_ = {};
        banner_ = "Waiting for the host";
        bannerTime_ = 1e9;
        return;
    }
    // A finished adventure can't be continued. Test runs never touch the save.
    if (!testRun_)
    {
        std::remove(savePath().c_str());
        std::remove((savePath() + ".bak").c_str());
        hasSave_ = false;
    }
    if (host_)
    {
        cutscene_ = {};
        act("restart", nlohmann::json{{"seed", seed_ + 1}}.dump());
        return;
    }
    cutscene_ = {};
    autoPlay_ = false;
    newAdventure(SDL_GetTicks());
    menu_ = testRun_ ? Menu::None : Menu::Main;
}

bool YoreholdGame::chapterCleared() const
{
    if (!chapter_)
        return false;
    if (!chapter_->completeWhen.empty())
        return std::all_of(chapter_->completeWhen.begin(), chapter_->completeWhen.end(), [this](const std::string& f) { return flags_.contains(f); });
    // Every authored enemy is down (NPCs the party picked a fight with don't count).
    return !chapter_->encounters.empty()
        && std::none_of(creatures_.begin() + heroCount_, creatures_.end(), [](const Creature& c) { return c.npc < 0 && !c.sheet.down(); });
}

// ---------------------------------------------------------------- story, NPCs and quests

void YoreholdGame::setFlags(const std::vector<std::string>& flags)
{
    const std::set<std::string> before = flags_;
    flags_.insert(flags.begin(), flags.end());
    if (flags_ != before)
        flagsChanged(before);
}

// Tells the party what changed in the journal.
void YoreholdGame::flagsChanged(const std::set<std::string>& before)
{
    if (!journal_)
        return;
    for (const yh::Quest& quest : journal_->quests)
    {
        const yh::QuestProgress was = quest.progress(before);
        const yh::QuestProgress now = quest.progress(flags_);
        if (now.status == yh::QuestStatus::Hidden)
            continue;
        if (was.status == yh::QuestStatus::Hidden)
            say("New quest: " + quest.title + " (J: journal)");
        for (size_t i = 0; i < quest.objectives.size(); i++)
            if (now.objectiveComplete[i] && !was.objectiveComplete[i] && now.status != yh::QuestStatus::Failed)
                say("Done: " + quest.objectives[i].text);
        if (now.status != was.status && now.status == yh::QuestStatus::Completed)
        {
            say("Quest complete: " + quest.title);
            banner_ = quest.title;
            bannerTime_ = 2.5;
        }
        else if (now.status != was.status && now.status == yh::QuestStatus::Failed)
            say("Quest failed: " + quest.title);
    }
}

yh::Random YoreholdGame::nextRandom(uint64_t salt)
{
    return yh::Random(seed_ ^ salt ^ (++rolls_ * 0x9e3779b97f4a7c15ull));
}

size_t YoreholdGame::leaderIndex() const
{
    for (size_t i = 0; i < heroCount_; i++)
        if (tokens_.tokens[i].selected && mine(i) && !creatures_[i].sheet.down())
            return i;
    for (size_t i = 0; i < heroCount_; i++)
        if (mine(i) && !creatures_[i].sheet.down())
            return i;
    for (size_t i = 0; i < heroCount_; i++)
        if (!creatures_[i].sheet.down())
            return i;
    return 0;
}

std::optional<size_t> YoreholdGame::npcAt(yh::Cell c) const
{
    if (!chapter_)
        return std::nullopt;
    for (size_t i = 0; i < chapter_->npcs.size(); i++)
        if (peaceful(i) && cellOf(npcToken(i)) == c)
            return i;
    return std::nullopt;
}

bool YoreholdGame::peaceful(size_t npc) const
{
    const size_t i = npcToken(npc);
    return i < creatures_.size() && creatures_[i].team == 2 && !creatures_[i].sheet.down();
}

std::optional<size_t> YoreholdGame::hoveredNpc() const
{
    if (!chapter_)
        return std::nullopt;
    const yh::Vec2 world = camera_.screenToWorld(input_.mouse());
    for (size_t i = 0; i < chapter_->npcs.size(); i++)
    {
        const yh::Token& token = tokens_.tokens[npcToken(i)];
        if (peaceful(i) && token.floor == 0 && distance(world, token.position) <= token.radius)
            return i;
    }
    return std::nullopt;
}

void YoreholdGame::walkToTalk(size_t npc)
{
    const size_t leader = leaderIndex();
    if (creatures_[leader].sheet.down())
        return;
    yh::Token& token = tokens_.tokens[leader];
    const yh::Cell from = cellOf(leader);
    const yh::Cell goal = cellOf(npcToken(npc));
    token.path.clear();
    if (grid_.distance(from, goal) > 1.5f)
    {
        // The nearest open square next to them.
        std::vector<yh::Cell> best;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                const yh::Cell next{goal.x + dx, goal.y + dy};
                if ((dx || dy) && walkable(next))
                {
                    std::vector<yh::Cell> path = findPath(grid_, from, next, [this](yh::Cell c) { return walkable(c); });
                    if (!path.empty() && (best.empty() || path.size() < best.size()))
                        best = std::move(path);
                }
            }
        for (size_t i = 1; i < best.size(); i++)
            token.path.push_back(grid_.center(best[i]));
    }
    pendingTalk_ = npc;
}

void YoreholdGame::startTalk(size_t npc)
{
    const Chapter::Npc& who = chapter_->npcs[npc];
    std::string error = "not found";
    const std::optional<std::string> text = files_.readText(who.dialogue);
    std::optional<yh::Dialogue> dialogue = text ? yh::Dialogue::fromJson(*text, &error) : std::nullopt;
    if (!dialogue)
    {
        say(who.name + "'s dialogue " + who.dialogue + ": " + error);
        return;
    }
    for (yh::Token& token : tokens_.tokens)
        token.path.clear();
    talkNpc_ = npc;
    talk_ = std::make_unique<yh::DialogueSession>(std::move(*dialogue), flags_);
    journalOpen_ = false;
    // Node entry effects of the first line count too.
    if (talk_->flags() != flags_)
    {
        const std::set<std::string> before = flags_;
        flags_ = talk_->flags();
        flagsChanged(before);
    }
}

void YoreholdGame::chooseReply(size_t index, size_t hero)
{
    if (!talk_)
        return;
    const std::vector<const yh::DialogueChoice*> choices = talk_->choices();
    if (talk_->finished() || choices.empty())
    {
        // The last line: any key or click ends it.
        talk_.reset();
        if (chapterCleared())
            playEnding();
        else
            saveAdventure();
        return;
    }
    if (index >= choices.size())
        return;
    const yh::DialogueChoice choice = *choices[index];
    const yh::Character& speaker = creatures_[hero].sheet;
    const std::optional<yh::DialogueResult> result = talk_->choose(choice.id, [&](std::string_view skill) {
        yh::Random dice = nextRandom(0x7a1cull);
        return speaker.rollCheck(rules_, skill, yh::Advantage::None, dice);
    });
    if (!result)
        return;
    say(speaker.name + ": " + choice.text);
    if (result->roll && choice.check)
        say(speaker.name + " rolls " + choice.check->skill + ": " + result->roll->describe() + " vs " +
            std::to_string(choice.check->difficulty) + (result->passed ? ", success" : ", failure"));
    const std::set<std::string> before = flags_;
    flags_ = talk_->flags();
    if (flags_ != before)
        flagsChanged(before);
    if (talk_->finished() && !talk_->current())
    {
        talk_.reset();
        if (chapterCleared())
            playEnding();
        else
            saveAdventure();
    }
}

int YoreholdGame::restsLeft(const yh::RestDefinition& rest) const
{
    if (rest.perAdventure == 0)
        return -1; // unlimited
    const auto used = restsUsed_.find(rest.id);
    return std::max(0, rest.perAdventure - (used == restsUsed_.end() ? 0 : used->second));
}

void YoreholdGame::rest(const yh::RestDefinition& rest)
{
    if (restsLeft(rest) == 0 || (encounter_ && !encounter_->finished()) || partyDown())
        return;
    restsUsed_[rest.id]++;
    restRandom_ = nextRandom(0x5eedull);
    say("The party takes a " + (rest.name.empty() ? rest.id : rest.name) + ".");
    for (size_t i = 0; i < heroCount_; i++)
    {
        yh::Character& c = creatures_[i].sheet;
        if (c.hp >= c.maxHp())
            continue;
        std::string detail;
        const int healed = c.recover(rules_, rest.recovery, restRandom_, &detail);
        if (healed <= 0)
            continue;
        tokens_.tokens[i].floor = 0; // revived
        say(c.name + " recovers " + std::to_string(healed) + " HP" + (detail.empty() ? "." : " (" + detail + ")."));
        floaters_.push_back({tokens_.tokens[i].position, "+" + std::to_string(healed), ui_.theme.good});
    }
    if (const int left = restsLeft(rest); left >= 0)
        say(std::to_string(left) + " left.");
    saveAdventure();
}

void YoreholdGame::beginTurn()
{
    const std::optional<size_t> current = currentCreature();
    if (!current)
        return;
    tokens_.settings.activeTurn = *current;
    pendingAttack_.reset();
    enemyStep_ = EnemyStep::Think;
    enemyTimer_ = 0;
    enemyTarget_.reset();
    if (creatures_[*current].team == 0)
    {
        for (size_t i = 0; i < tokens_.tokens.size(); i++)
            tokens_.tokens[i].selected = i == *current;
        computeReach(*current);
    }
    else
        reach_.clear();
    camera_.moveTo(tokens_.tokens[*current].position);
}

void YoreholdGame::endTurn()
{
    if (const std::optional<size_t> current = currentCreature())
    {
        yh::Token& token = tokens_.tokens[*current];
        if (!token.path.empty())
            token.position = token.path.back();
        token.path.clear();
    }
    encounter_->nextTurn();
    syncLog();
    if (encounter_->finished())
        endCombat();
    else
        beginTurn();
}

void YoreholdGame::updateHeroTurn()
{
    const size_t me = *currentCreature();
    const yh::Token& token = tokens_.tokens[me];

    // Walked up to swing at someone: swing once the step has landed.
    if (pendingAttack_ && pendingStep_ && token.path.empty() && cellOf(me) == *pendingStep_)
    {
        const size_t target = *pendingAttack_;
        pendingAttack_.reset();
        pendingStep_.reset();
        if (adjacent(me, target))
            act("attack", nlohmann::json{{"target", target}}.dump());
        return;
    }

    const bool walkClick = input_.clicked(yh::actions::moveTo), selectClick = input_.clicked(yh::actions::select);
    if (!overUi(input_.mouse()) && (walkClick || selectClick))
    {
        if (const std::optional<size_t> target = hoveredCreature(); target && creatures_[*target].team == 1)
            tryAttack(*target);
        else if (walkClick && token.path.empty())
        {
            // Only onto squares its movement reaches.
            const yh::Cell to = grid_.cellAt(camera_.screenToWorld(input_.mouse()));
            if (to != standing_ && reach_.contains(to))
                act("step", nlohmann::json{{"at", {to.x, to.y}}}.dump());
        }
        return;
    }
    if (input_.keyPressed(SDLK_SPACE) && token.path.empty())
        act("end");
}

void YoreholdGame::tryAttack(size_t target)
{
    const size_t me = *currentCreature();
    if (!encounter_->current().budget.action)
    {
        say(creatures_[me].sheet.name + " has already used their action. End the turn (Space).");
        return;
    }
    if (adjacent(me, target))
    {
        act("attack", nlohmann::json{{"target", target}}.dump());
        return;
    }

    // Walk to the cheapest reachable square next to the target, then swing.
    const yh::Cell goal = cellOf(target);
    std::optional<yh::Cell> best;
    float bestCost = 0;
    for (const auto& [c, cost] : reach_)
    {
        if (grid_.distance(c, goal) <= 1.01f && (!best || cost < bestCost))
        {
            best = c;
            bestCost = cost;
        }
    }
    if (!best)
    {
        say(creatures_[target].sheet.name + " is out of reach this turn.");
        return;
    }
    pendingAttack_ = target;
    pendingStep_ = *best;
    act("step", nlohmann::json{{"at", {best->x, best->y}}}.dump());
}

void YoreholdGame::attack(size_t target)
{
    const std::optional<size_t> targetIndex = orderIndex(target);
    if (!targetIndex)
        return;
    const yh::AttackResult result = encounter_->attack(*targetIndex);
    syncLog();

    const yh::Vec2 at = tokens_.tokens[target].position;
    if (!result.hit)
        floaters_.push_back({at, "Miss", {200, 200, 210, 255}});
    else
        floaters_.push_back({at, (result.critical ? "Critical! " : "") + std::to_string(result.damageRoll.total),
                             result.critical ? yh::Color{255, 200, 60, 255} : yh::Color{255, 90, 70, 255}});

    if (creatures_[target].sheet.down())
    {
        yh::Token& token = tokens_.tokens[target];
        token.floor = dead;
        token.selected = false;
        token.path.clear();
        if (creatures_[target].npc >= 0)
            setFlags(chapter_->npcs[creatures_[target].npc].killed);
    }
    if (encounter_->finished())
        endCombat();
}

void YoreholdGame::updateEnemyTurn(double deltaSeconds)
{
    const size_t me = *currentCreature();
    yh::Token& token = tokens_.tokens[me];
    enemyTimer_ += deltaSeconds;

    switch (enemyStep_)
    {
    case EnemyStep::Think:
    {
        if (enemyTimer_ < 0.45)
            return;
        // Score everything it could do and take the best (heroes run this too in auto-play).
        std::vector<size_t> who;
        const yh::TacticalView view = tacticalView(me, who);
        yh::Random random(seed_ ^ (static_cast<uint64_t>(fights_) << 40) ^ (static_cast<uint64_t>(encounter_->round()) << 20) ^ me);
        std::vector<yh::TacticalChoice> considered;
        const yh::TacticalChoice choice = yh::decide(creatures_[me].ai, view, grid_, random, &considered);
        using Kind = yh::TacticalChoice::Kind;
        if (aiNotes_)
        {
            const char* names[] = {"holds", "attacks", "advances", "flees"};
            char score[32];
            std::snprintf(score, sizeof score, "%.1f", choice.score);
            say("[AI " + creatures_[me].ai.base + "] " + creatures_[me].sheet.name + " " + names[static_cast<int>(choice.kind)]
                + (choice.kind == Kind::Attack ? " " + creatures_[who[choice.target]].sheet.name : std::string())
                + " (" + score + ", best of " + std::to_string(considered.size()) + ")");
        }

        enemyTimer_ = 0;
        enemyTarget_.reset();
        if (choice.kind == Kind::Attack)
            enemyTarget_ = who[choice.target];
        if (choice.kind == Kind::Flee && !creatures_[me].fleeing)
            act("flee");
        if (choice.dash)
            act("dash"); // recomputes reach_
        if (choice.cell != standing_)
            act("step", nlohmann::json{{"at", {choice.cell.x, choice.cell.y}}}.dump());
        enemyStep_ = EnemyStep::Walk;
        return;
    }
    case EnemyStep::Walk:
        if (!token.path.empty())
            return;
        enemyTimer_ = 0;
        enemyStep_ = enemyTarget_ && adjacent(me, *enemyTarget_) && encounter_->current().budget.action ? EnemyStep::Strike : EnemyStep::Wait;
        return;
    case EnemyStep::Strike:
        if (enemyTimer_ < 0.25)
            return;
        enemyTimer_ = 0;
        enemyStep_ = EnemyStep::Wait;
        act("attack", nlohmann::json{{"target", *enemyTarget_}}.dump());
        return;
    case EnemyStep::Wait:
        if (enemyTimer_ < 0.6)
            return;
        // Running, far enough from everyone and out of their sight (or walled off from them): it's gone.
        // While the party can still see it they get a chance to chase it down.
        if (creatures_[me].fleeing)
        {
            const yh::CellCosts away = distanceToFoes(creatures_[me].team);
            const auto distance = away.find(cellOf(me));
            const bool watched = creatures_[me].team != 0 && fog_.state(0, 0, cellOf(me)) == yh::FogState::Visible;
            if (distance == away.end() || (distance->second >= creatures_[me].ai.escapeAt && !watched))
            {
                act("escape");
                return;
            }
        }
        act("end");
        return;
    }
}

// Walking distance from every cell to the nearest standing foe of `team`, through other creatures.
yh::CellCosts YoreholdGame::distanceToFoes(int team) const
{
    yh::CellCosts distance;
    using Entry = std::pair<float, yh::Cell>;
    auto later = [](const Entry& a, const Entry& b) { return a.first > b.first; };
    std::priority_queue<Entry, std::vector<Entry>, decltype(later)> queue(later);
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        if (creatures_[i].team == team || creatures_[i].sheet.down() || !orderIndex(i))
            continue;
        distance[cellOf(i)] = 0;
        queue.push({0.0f, cellOf(i)});
    }
    std::vector<yh::Cell> neighbours;
    while (!queue.empty())
    {
        const auto [cost, c] = queue.top();
        queue.pop();
        if (cost > distance[c])
            continue;
        grid_.neighbours(c, neighbours);
        for (const yh::Cell next : neighbours)
        {
            if (!walkable(next) || (next.x != c.x && next.y != c.y && (!walkable({next.x, c.y}) || !walkable({c.x, next.y}))))
                continue;
            const float nextCost = cost + grid_.stepCost(c, next, 0);
            const auto known = distance.find(next);
            if (known == distance.end() || nextCost < known->second)
            {
                distance[next] = nextCost;
                queue.push({nextCost, next});
            }
        }
    }
    return distance;
}

yh::TacticalView YoreholdGame::tacticalView(size_t me, std::vector<size_t>& who)
{
    yh::TacticalView view;
    who.clear();
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        const Creature& c = creatures_[i];
        if (c.sheet.down() || !orderIndex(i))
            continue;
        if (i == me)
            view.self = who.size();
        yh::TacticalUnit unit;
        unit.team = c.team;
        unit.at = cellOf(i);
        unit.hp = c.sheet.hp;
        unit.maxHp = c.sheet.maxHp();
        unit.armorClass = c.sheet.armorClass(rules_);
        unit.attackBonus = c.sheet.attackModifier(rules_);
        const std::optional<yh::DiceExpression> damage = yh::DiceExpression::parse(c.sheet.damageDice(rules_));
        unit.averageDamage = damage ? std::max(1.0f, static_cast<float>(damage->minimum() + damage->maximum()) / 2) : 1.0f;
        unit.speed = c.sheet.speedSquares(rules_);
        unit.leader = c.ai.leader;
        view.units.push_back(unit);
        who.push_back(i);
    }
    const int team = creatures_[me].team;
    view.action = encounter_->current().budget.action;
    if (view.action)
    {
        computeReach(me, creatures_[me].sheet.speedSquares(rules_));
        view.dashReach = reach_;
    }
    computeReach(me);
    view.reach = reach_;
    view.foeDistance = distanceToFoes(team);
    view.sideAtStart = sideAtStart_[team == 0 ? 0 : 1];
    view.hadLeader = hadLeader_[team == 0 ? 0 : 1];
    view.fleeing = creatures_[me].fleeing;
    return view;
}

void YoreholdGame::autoExplore()
{
    // The leader heads for the nearest enemy still standing; the rest follow their links.
    const size_t leader = 0;
    const bool hurt = std::any_of(creatures_.begin(), creatures_.begin() + heroCount_,
        [](const Creature& c) { return !c.sheet.down() && c.sheet.hp * 2 < c.sheet.maxHp(); });
    // Use the first rest that still has uses (short before long).
    for (size_t i = 0; i < rules_.rests.size(); i++)
    {
        if (hurt && restsLeft(rules_.rests[i]) != 0)
        {
            act("rest", nlohmann::json{{"rest", i}}.dump());
            break;
        }
    }
    yh::Token& token = tokens_.tokens[leader];
    if (!token.path.empty() || creatures_[leader].sheet.down())
        return;
    std::optional<yh::Cell> goal;
    float nearest = 0;
    for (size_t i = heroCount_; i < creatures_.size(); i++)
    {
        if (creatures_[i].sheet.down() || creatures_[i].team != 1)
            continue;
        const float d = grid_.distance(cellOf(leader), cellOf(i));
        if (!goal || d < nearest)
        {
            goal = cellOf(i);
            nearest = d;
        }
    }
    if (!goal)
        return;
    const std::vector<yh::Cell> path = findPath(grid_, cellOf(leader), *goal, [this](yh::Cell c) { return walkable(c); });
    if (path.size() < 4)
    {
        if (path.empty() && autoExploreStuck_++ == 0)
            say("Auto-play: no path to the next enemy.");
        return;
    }
    // Stop a few squares short; spotting them starts the fight anyway.
    for (size_t i = 1; i + 2 < path.size() && i < 8; i++)
        token.path.push_back(grid_.center(path[i]));
}

// ---------------------------------------------------------------- saves

std::string YoreholdGame::stateDir() const
{
    if (const char* dir = SDL_getenv("YOREHOLD_SAVE_DIR"))
        return std::string(dir) + "/";
    char* pref = SDL_GetPrefPath("Yorehold", "Yorehold");
    if (!pref)
        return {};
    std::string dir = pref;
    SDL_free(pref);
    return dir;
}

std::string YoreholdGame::savePath() const
{
    const std::string dir = stateDir();
    if (dir.empty())
        return {};
    // Each installed adventure keeps its own autosave; the built-in ones share the original file.
    if (chapter_ && installedAdventure())
        return dir + "adventure-" + std::filesystem::path(adventures_[adventure_].package).stem().string() + "-" + chapter_->id + ".json";
    return dir + "adventure.json";
}

// ---------------------------------------------------------------- settings

void YoreholdGame::applySettings()
{
    applyScheme(settings_.controls);
    controls_.settings.zoomToCursor = settings_.zoomToCursor;
    controls_.settings.edgeScroll = settings_.edgeScroll;
    controls_.settings.followSelection = settings_.cameraFollows;
    controls_.settings.keyPanSpeed = settings_.panSpeed;
    controls_.settings.edgeScrollSpeed = settings_.panSpeed;
    int count = 0;
    if (SDL_Window** windows = SDL_GetWindows(&count))
    {
        if (count > 0)
            SDL_SetWindowFullscreen(windows[0], settings_.fullscreen);
        SDL_free(windows);
    }
}

void YoreholdGame::saveSettings() const
{
    if (testRun_ || stateDir().empty())
        return;
    const nlohmann::json j{
        {"controls", settings_.controls == yh::ControlPreset::Foundry ? "foundry" : "bg3"},
        {"zoomToCursor", settings_.zoomToCursor},
        {"edgeScroll", settings_.edgeScroll},
        {"cameraFollows", settings_.cameraFollows},
        {"panSpeed", settings_.panSpeed},
        {"fullscreen", settings_.fullscreen},
        {"lighting", std::array<const char*, 4>{"map", "off", "mood", "rules"}[std::clamp(settings_.lighting, 0, 3)]},
        {"timeOfDay", std::array<const char*, 4>{"map", "day", "dusk", "night"}[std::clamp(settings_.timeOfDay, 0, 3)]},
        {"sharedFog", settings_.sharedFog},
        {"playerName", settings_.playerName},
        {"joinAddress", settings_.joinAddress},
        {"lastPackage", settings_.lastPackage},
        {"lastFolder", settings_.lastFolder},
        {"skin", settings_.skin},
        {"server", settings_.server},
        {"serverKey", settings_.serverKey},
        {"deviceId", settings_.deviceId},
    };
    yh::writeFileAtomically(stateDir() + "settings.json", j.dump(2), false);
}

void YoreholdGame::loadSettings()
{
    const std::optional<std::string> text = yh::readTextFile(stateDir() + "settings.json");
    if (!text)
        return;
    // Unknown or broken values keep their defaults; settings never block the game from starting.
    const nlohmann::json j = nlohmann::json::parse(*text, nullptr, false);
    if (!j.is_object())
        return;
    try
    {
        Settings s;
        s.controls = j.value("controls", std::string("bg3")) == "foundry" ? yh::ControlPreset::Foundry : yh::ControlPreset::BG3;
        s.zoomToCursor = j.value("zoomToCursor", s.zoomToCursor);
        s.edgeScroll = j.value("edgeScroll", s.edgeScroll);
        s.cameraFollows = j.value("cameraFollows", s.cameraFollows);
        s.panSpeed = j.value("panSpeed", s.panSpeed);
        s.panSpeed = std::isfinite(s.panSpeed) ? std::clamp(s.panSpeed, 200.0f, 3000.0f) : Settings{}.panSpeed;
        s.fullscreen = j.value("fullscreen", s.fullscreen);
        const std::string lighting = j.value("lighting", std::string("map"));
        s.lighting = lighting == "off" ? 1 : lighting == "mood" ? 2 : lighting == "rules" ? 3 : 0;
        const std::string time = j.value("timeOfDay", std::string("map"));
        s.timeOfDay = time == "day" ? 1 : time == "dusk" ? 2 : time == "night" ? 3 : 0;
        s.sharedFog = j.value("sharedFog", s.sharedFog);
        s.playerName = j.value("playerName", s.playerName).substr(0, 32);
        s.joinAddress = j.value("joinAddress", s.joinAddress).substr(0, 253);
        s.lastPackage = j.value("lastPackage", s.lastPackage);
        s.lastFolder = j.value("lastFolder", s.lastFolder);
        s.skin = j.value("skin", s.skin).substr(0, 200);
        s.server = j.value("server", s.server).substr(0, 253);
        s.serverKey = j.value("serverKey", s.serverKey).substr(0, 128);
        s.deviceId = j.value("deviceId", s.deviceId).substr(0, 128);
        settings_ = s;
    }
    catch (const nlohmann::json::exception&)
    {
        // A value of the wrong type: keep the defaults.
    }
}

void YoreholdGame::saveAdventure()
{
    // Only between fights: the encounter points into creatures_ and isn't saved. A joined player's
    // game belongs to the host, who keeps the save.
    if (!chapter_ || testRun_ || client_ || (encounter_ && !encounter_->finished()) || partyDown() || chapterCleared() || cutscene_.running())
        return;
    std::string error;
    if (saveFormat().writeFile(savePath(), stateJson(), &error))
        hasSave_ = true;
    else
        say("Couldn't save: " + error);
}

std::string YoreholdGame::stateJson() const
{
    nlohmann::json data;
    data["chapterId"] = chapter_->id;
    data["chapterFolder"] = chapter_->folder;
    data["chapterSignature"] = chapter_->signature;
    data["seed"] = seed_;
    data["fights"] = fights_;
    data["restsUsed"] = restsUsed_;
    data["flags"] = flags_;
    data["rolls"] = rolls_;
    data["fog"] = nlohmann::json::parse(fog_.toJson());
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        const yh::Token& token = tokens_.tokens[i];
        data["creatures"].push_back({
            {"sheet", nlohmann::json::parse(creatures_[i].sheet.toJson())},
            {"awake", creatures_[i].awake},
            {"fled", creatures_[i].fled},
            {"team", creatures_[i].team},
            {"x", token.path.empty() ? token.position.x : token.path.back().x},
            {"y", token.path.empty() ? token.position.y : token.path.back().y},
        });
    }
    return data.dump();
}

bool YoreholdGame::loadAdventure()
{
    if (!chapter_)
        return false;
    std::string error;
    const std::optional<std::string> text = saveFormat().readFile(savePath(), &error);
    if (!text)
        return false;
    if (restoreState(*text, &error))
    {
        say(chapter_->resumeText);
        return true;
    }
    newAdventure(SDL_GetTicks());
    say("Couldn't load the save (" + error + "). Starting fresh.");
    return false;
}

bool YoreholdGame::restoreState(std::string_view text, std::string* problem)
{
    if (!chapter_)
        return false;
    std::string error;
    try
    {
        const nlohmann::json data = nlohmann::json::parse(text);
        if (data.at("chapterId") != chapter_->id || data.at("chapterFolder") != chapter_->folder)
            throw std::runtime_error("this save belongs to another chapter");
        if (data.contains("chapterSignature") && data.at("chapterSignature") != chapter_->signature)
            throw std::runtime_error("the chapter's content has changed since this save");
        const nlohmann::json& saved = data.at("creatures");
        const auto& fogData = data.at("fog");
        if (fogData.at("width") != map().width() || fogData.at("height") != map().height()
            || fogData.at("cellSize") != GameMap::cellSize)
            throw std::runtime_error("the map has changed since this save");
        std::optional<yh::FogOfWar> fog = yh::FogOfWar::fromJson(data.at("fog").dump(), &error);
        if (!fog || !saved.is_array() || saved.size() != creatures_.size())
            throw std::runtime_error(fog ? "the map has changed since this save" : error);
        // Check all state before applying any of it, including positions and recovery counters.
        const auto seed = data.at("seed").get<uint64_t>();
        const int fights = data.at("fights").get<int>();
        auto rests = data.at("restsUsed").get<std::map<std::string, int>>();
        if (fights < 0 || std::any_of(rests.begin(), rests.end(), [](const auto& entry) { return entry.second < 0; }))
            throw std::runtime_error("invalid adventure counters");
        auto flags = data.value("flags", std::set<std::string>{});
        const auto rolls = data.value("rolls", uint64_t{0});
        // Only in a co-op snapshot: who plays which hero.
        auto seats = data.value("seats", seats_);
        if (!seats.empty() && seats.size() != chapter_->party.size())
            throw std::runtime_error("seats don't match the party");
        std::vector<yh::Character> sheets;
        std::vector<yh::Vec2> positions;
        std::vector<bool> awake, fled;
        std::vector<int> teams;
        for (const nlohmann::json& c : saved)
        {
            std::optional<yh::Character> sheet = yh::Character::fromJson(c.at("sheet").dump(), &error);
            if (!sheet)
                throw std::runtime_error(error);
            sheets.push_back(std::move(*sheet));
            const yh::Vec2 position{c.at("x").get<float>(), c.at("y").get<float>()};
            if (!std::isfinite(position.x) || !std::isfinite(position.y) || position.x < 0 || position.y < 0
                || position.x >= map().width() * GameMap::cellSize || position.y >= map().height() * GameMap::cellSize)
                throw std::runtime_error("saved token is outside the map");
            positions.push_back(position);
            awake.push_back(c.at("awake").get<bool>());
            fled.push_back(c.value("fled", false) && sheets.back().down());
            // Only an NPC's side can change (a peaceful one the party attacked).
            const int team = c.value("team", creatures_[teams.size()].team);
            if (team != creatures_[teams.size()].team && !(creatures_[teams.size()].npc >= 0 && (team == 1 || team == 2)))
                throw std::runtime_error("saved creature is on the wrong side");
            teams.push_back(team);
        }
        seats_ = std::move(seats);
        newAdventure(seed);
        fog_ = std::move(*fog);
        fights_ = fights;
        restsUsed_ = std::move(rests);
        flags_ = std::move(flags);
        rolls_ = rolls;
        for (size_t i = 0; i < creatures_.size(); i++)
        {
            creatures_[i].sheet = std::move(sheets[i]);
            creatures_[i].awake = awake[i];
            creatures_[i].fled = fled[i];
            creatures_[i].team = teams[i];
            yh::Token& token = tokens_.tokens[i];
            if (creatures_[i].npc >= 0)
                token.owner = teams[i] == 1 ? enemyOwner : npcOwner;
            token.position = positions[i];
            token.path.clear();
            if (creatures_[i].sheet.down())
                token.floor = dead;
        }
        log_.clear();
        bannerTime_ = 0;
        return true;
    }
    catch (const std::exception& e)
    {
        if (problem)
            *problem = e.what();
        return false;
    }
}

bool YoreholdGame::partyDown() const
{
    return heroCount_ > 0 && std::all_of(creatures_.begin(), creatures_.begin() + heroCount_, [](const Creature& c) { return c.sheet.down(); });
}

// ---------------------------------------------------------------- grid helpers

void YoreholdGame::computeReach(size_t mover, int extra)
{
    reach_.clear();
    standing_ = cellOf(mover);
    const float budget = static_cast<float>(encounter_->current().budget.movementLeft + extra) + 0.01f;
    auto open = [&](yh::Cell c) { return walkable(c) && !occupied(c, mover); };

    using Entry = std::pair<float, yh::Cell>;
    auto later = [](const Entry& a, const Entry& b) { return a.first > b.first; };
    std::priority_queue<Entry, std::vector<Entry>, decltype(later)> queue(later);
    reach_[standing_] = 0;
    queue.push({0.0f, standing_});
    std::vector<yh::Cell> neighbours;
    while (!queue.empty())
    {
        const auto [cost, c] = queue.top();
        queue.pop();
        if (cost > reach_[c])
            continue;
        grid_.neighbours(c, neighbours);
        for (const yh::Cell next : neighbours)
        {
            if (!open(next))
                continue;
            // Same rule as findPath: no cutting corners past walls or creatures.
            if (next.x != c.x && next.y != c.y && (!open({next.x, c.y}) || !open({c.x, next.y})))
                continue;
            const float nextCost = cost + grid_.stepCost(c, next, 0);
            if (nextCost > budget)
                continue;
            const auto known = reach_.find(next);
            if (known == reach_.end() || nextCost < known->second)
            {
                reach_[next] = nextCost;
                queue.push({nextCost, next});
            }
        }
    }
}

bool YoreholdGame::occupied(yh::Cell c, size_t except) const
{
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        if (i != except && tokens_.tokens[i].floor != dead && cellOf(i) == c)
            return true;
    }
    return false;
}

bool YoreholdGame::walkable(yh::Cell c) const
{
    return map().walkable(c) && !npcAt(c);
}

std::optional<size_t> YoreholdGame::orderIndex(size_t creature) const
{
    if (!encounter_)
        return std::nullopt;
    const auto& order = encounter_->order();
    for (size_t i = 0; i < order.size(); i++)
    {
        if (order[i].character == &creatures_[creature].sheet)
            return i;
    }
    return std::nullopt;
}

std::optional<size_t> YoreholdGame::currentCreature() const
{
    if (!encounter_ || !encounter_->started() || encounter_->finished())
        return std::nullopt;
    const yh::Character* current = encounter_->order()[encounter_->currentIndex()].character;
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        if (&creatures_[i].sheet == current)
            return i;
    }
    return std::nullopt;
}

yh::Cell YoreholdGame::cellOf(size_t creature) const
{
    const yh::Token& token = tokens_.tokens[creature];
    return grid_.cellAt(token.path.empty() ? token.position : token.path.back());
}

bool YoreholdGame::adjacent(size_t a, size_t b) const
{
    return grid_.distance(cellOf(a), cellOf(b)) <= 1.01f;
}

std::optional<size_t> YoreholdGame::hoveredCreature() const
{
    const yh::Vec2 world = camera_.screenToWorld(input_.mouse());
    for (size_t i = creatures_.size(); i-- > 0;)
    {
        const yh::Token& token = tokens_.tokens[i];
        if (token.floor == 0 && distance(world, token.position) <= token.radius)
            return i;
    }
    return std::nullopt;
}

bool YoreholdGame::overUi(yh::Vec2 screen) const
{
    return std::any_of(uiRects_.begin(), uiRects_.end(), [screen](const yh::Rect& r) { return r.contains(screen); });
}

// ---------------------------------------------------------------- drawing

void YoreholdGame::draw(yh::Renderer& renderer)
{
    if (!assets_)
    {
        assets_ = std::make_unique<yh::Assets>(files_, renderer);
        ui_.theme = {}; // an adventure without a theme file doesn't keep the last one's
        if (const auto text = files_.readText(themePath_))
        {
            std::string error;
            if (const auto theme = yh::UiTheme::fromJson(*text, &error))
                ui_.theme = *theme;
            else
                std::fprintf(stderr, "UI theme %s: %s\n", themePath_.c_str(), error.c_str());
        }
        // Every widget image comes from ui/, where the selected skin's files sit on top of the defaults.
        yh::loadUiImages(ui_.theme, *assets_, "ui");
        ui_.theme.font = assets_->font("fonts/AtkinsonHyperlegible-Bold.ttf", 18);
        title_ = assets_->font("fonts/Cinzel.ttf", 54);
        tokens_.initialFont = assets_->font("fonts/Cinzel.ttf", 34);
        tokens_.labelFont = ui_.theme.font;
        tokens_.useAssets(*assets_);
    }
    if (!chapter_)
    {
        renderer.clear({6, 7, 12, 255});
        drawMenu(renderer);
        input_.endFrame();
        return;
    }
    map().bindTileset(renderer);
    camera_.setViewport(renderer.bounds().size());
    if (!cameraPlaced_)
    {
        // Needs the viewport size, so it waits for the first frame.
        camera_.jumpTo(tokens_.tokens[0].position, 1.0f);
        cameraPlaced_ = true;
    }

    drawWorld(renderer);
    if (menu_ != Menu::None)
    {
        drawMenu(renderer);
        input_.endFrame();
        return;
    }
    if (cutscene_.running())
    {
        cutscene_.draw(renderer, ui_.theme.font, title_);
        input_.endFrame();
        return;
    }
    yh::debug::value("camera x", camera_.position().x);
    yh::debug::value("camera y", camera_.position().y);
    yh::debug::value("camera follows", controls_.following() ? 1 : 0);
    yh::debug::value("camera zoom", camera_.zoom());
    yh::debug::value("party visibility", static_cast<int>(fog_.state(viewTeam(), 0, cellOf(0))));
    yh::debug::value("leader floor", tokens_.tokens[0].floor);
    yh::debug::value("party x", tokens_.tokens[0].position.x);
    yh::debug::value("party y", tokens_.tokens[0].position.y);
    yh::debug::value("pointer inside", input_.mouseInside() ? 1 : 0);
    drawBars(renderer);
    drawHud(renderer);
    input_.endFrame();
}

void YoreholdGame::drawWorld(yh::Renderer& renderer)
{
    renderer.clear({6, 7, 12, 255});
    const yh::Rect view = camera_.visibleWorld();
    camera_.apply(renderer);
    map().map().draw(renderer, view, camera_.zoom(), 0);
    grid_.draw(renderer, view.intersect(map().map().worldBounds()), camera_.zoom(), {0, 0, 0, 45});

    // Torch flames; the light itself comes from the lighting pass.
    for (size_t i = 0; i < map().lights().size(); i++)
    {
        const GameMap::Light& torch = map().lights()[i];
        if (!torch.flame)
            continue;
        const float flicker = 1 + 0.15f * std::sin(static_cast<float>(time_) * 11 + i * 1.7f);
        renderer.fillCircle(torch.position, 10 * flicker, {255, 140, 50, 255});
        renderer.fillCircle(torch.position, 5 * flicker, {255, 235, 170, 255});
    }

    // The fallen: a dark mark where they dropped.
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        const yh::Token& token = tokens_.tokens[i];
        if (token.floor != dead || creatures_[i].fled || fog_.state(viewTeam(), 0, grid_.cellAt(token.position)) == yh::FogState::Unexplored)
            continue;
        const float r = token.radius * 0.7f;
        renderer.fillCircle(token.position, token.radius, creatures_[i].team == 0 ? yh::Color{90, 90, 100, 255} : yh::Color{70, 30, 25, 255});
        renderer.drawLine(token.position - yh::Vec2{r, r}, token.position + yh::Vec2{r, r}, {20, 10, 10, 255}, 5);
        renderer.drawLine(token.position + yh::Vec2{-r, r}, token.position + yh::Vec2{r, -r}, {20, 10, 10, 255}, 5);
    }
    tokens_.draw(renderer, camera_, grid_);
    renderer.pop();

    std::vector<yh::Light> lights;
    for (size_t i = 0; i < map().lights().size(); i++)
    {
        const GameMap::Light& torch = map().lights()[i];
        const float flicker = torch.flame ? 1 + 0.04f * std::sin(static_cast<float>(time_) * 9 + i * 2.3f) : 1.0f;
        lights.push_back({torch.position, torch.radius * flicker, torch.color});
    }
    for (size_t i = 0; i < heroCount_; i++)
    {
        if (tokens_.tokens[i].floor != dead && map().lighting().carried > 0)
            lights.push_back({tokens_.tokens[i].position, map().lighting().carried * cell, {255, 215, 160, 255}});
    }
    if (lightingMode() != GameMap::LightingMode::Off)
    {
        const GameMap::Sky sky = map().sky(timeOfDay());
        std::vector<yh::Shade> shaded;
        if (sky.differs)
            for (const yh::Rect& area : map().indoorAreas())
                shaded.push_back({area, sky.indoors});
        lighting_.ambient = sky.outdoors;
        lighting_.apply(renderer, camera_, lights, map().walls(), shaded);
    }

    camera_.apply(renderer);
    const std::optional<size_t> current = currentCreature();
    if (current)
    {
        // Where the current hero can still move, and a ring on whoever's turn it is.
        const bool heroTurn = creatures_[*current].team == 0;
        if (heroTurn && tokens_.tokens[*current].path.empty())
        {
            for (const auto& [c, cost] : reach_)
            {
                if (c != standing_)
                    renderer.fillRect({c.x * cell + 2, c.y * cell + 2, cell - 4, cell - 4}, {90, 170, 255, 45});
            }
        }
        const yh::Cell here = grid_.cellAt(tokens_.tokens[*current].position);
        const float pulse = 0.5f + 0.5f * std::sin(static_cast<float>(time_) * 5);
        renderer.drawRect({here.x * cell + 1, here.y * cell + 1, cell - 2, cell - 2},
            {255, 210, 90, static_cast<uint8_t>(140 + 100 * pulse)}, 3);

        if (heroTurn && !overUi(input_.mouse()))
        {
            if (const std::optional<size_t> target = hoveredCreature(); target && creatures_[*target].team == 1)
            {
                const yh::Cell t = cellOf(*target);
                renderer.drawRect({t.x * cell + 1, t.y * cell + 1, cell - 2, cell - 2}, {255, 70, 50, 255}, 3);
            }
        }
    }
    fog_.draw(renderer, view, viewTeam(), 0, {0, 0, 0, 255}, {4, 6, 14, 175});
    renderer.pop();
    tokens_.drawOverlay(renderer, camera_, grid_);
}

void YoreholdGame::drawBars(yh::Renderer& renderer)
{
    const bool fighting = encounter_ && !encounter_->finished();
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        const yh::Token& token = tokens_.tokens[i];
        const yh::Character& c = creatures_[i].sheet;
        if (token.floor != 0 || (!fighting && c.hp == c.maxHp()))
            continue;
        const yh::Vec2 top = camera_.worldToScreen(token.position - yh::Vec2{0, token.radius + 10});
        const float w = 46 * std::clamp(camera_.zoom(), 0.6f, 1.5f);
        const yh::Rect back{top.x - w / 2, top.y - 6, w, 6};
        renderer.fillRect({back.x - 1, back.y - 1, back.w + 2, back.h + 2}, {0, 0, 0, 200});
        const float fraction = std::clamp(static_cast<float>(c.hp) / std::max(1, c.maxHp()), 0.0f, 1.0f);
        renderer.fillRect({back.x, back.y, back.w * fraction, back.h}, creatures_[i].team == 0 ? ui_.theme.good : ui_.theme.bad);
    }

    for (const Floater& floater : floaters_)
    {
        const yh::Vec2 at = camera_.worldToScreen(floater.world) - yh::Vec2{0, 30 + floater.age * 45};
        yh::Color color = floater.color;
        color.a = static_cast<uint8_t>(255 * std::clamp(1.4f - floater.age, 0.0f, 1.0f));
        if (ui_.theme.font)
        {
            const float width = ui_.theme.font->measure(floater.text);
            ui_.theme.font->draw(renderer, at + yh::Vec2{-width / 2 + 2, 2}, floater.text, {0, 0, 0, color.a});
            ui_.theme.font->draw(renderer, at - yh::Vec2{width / 2, 0}, floater.text, color);
        }
    }
}

void YoreholdGame::drawHud(yh::Renderer& renderer)
{
    ui_.begin(renderer, input_);
    uiRects_.clear();
    const yh::Rect screen = renderer.bounds();

    if (!netStatus_.empty())
        ui_.label({screen.w / 2 - 160, screen.h - 58}, netStatus_, ui_.theme.textDim);
    if (bannerTime_ > 0 && title_)
    {
        const float alpha = static_cast<float>(std::clamp(bannerTime_, 0.0, 1.0));
        const yh::Rect area{0, screen.h * 0.22f, screen.w, 80};
        renderer.fillRect({0, area.y - 10, screen.w, area.h + 20}, {0, 0, 0, static_cast<uint8_t>(120 * alpha)});
        title_->drawCentered(renderer, area, banner_, {255, 214, 140, static_cast<uint8_t>(255 * alpha)});
    }

    drawParty(renderer);
    if (encounter_ && !encounter_->finished())
    {
        drawInitiative(renderer);
        drawCombatBar(renderer);
    }

    // Adventure log, bottom right.
    const yh::Rect logArea{screen.w - 440, screen.h - 160, 430, 150};
    ui_.log(logArea, log_);
    uiRects_.push_back(logArea);

    if (journal_)
        drawJournal(renderer);
    if (talk_)
    {
        drawDialogue(renderer);
        return;
    }
    if (!encounter_ || encounter_->finished())
    {
        std::string hint = scheme_.preset == yh::ControlPreset::BG3
            ? "Left-click: walk / select / talk   Drag: box-select   WASD / edges: pan   Wheel: zoom   F6: Foundry controls"
            : "Right-click: walk / talk   Left: select / drag   Right-drag: pan   Wheel: zoom   F6: BG3 controls";
        if (journal_)
            hint += "   J: journal";
        ui_.label({12, screen.h - 30}, hint, ui_.theme.textDim);
    }

    if (partyDown() || chapterCleared())
    {
        const yh::Rect button{screen.w / 2 - 110, screen.h * 0.22f + 110, 220, 44};
        if (ui_.button(button, client_ ? "Waiting for the host" : partyDown() ? "Try again" : "Play again", !client_))
            act("restart", nlohmann::json{{"seed", SDL_GetTicks()}}.dump());
        uiRects_.push_back(button);
    }
    else if (!encounter_ || encounter_->finished())
    {
        // One button per rest the ruleset offers, under the party cards.
        const bool hurt = std::any_of(creatures_.begin(), creatures_.begin() + heroCount_,
            [](const Creature& c) { return c.sheet.hp < c.sheet.maxHp(); });
        float y = 10 + heroCount_ * 66.0f + 4;
        for (size_t i = 0; i < rules_.rests.size(); i++, y += 46)
        {
            const yh::RestDefinition& r = rules_.rests[i];
            const int left = restsLeft(r);
            std::string text = r.name.empty() ? r.id : r.name;
            if (i == 0)
                text += " (R)";
            if (left >= 0)
                text += "  " + std::to_string(left) + " left";
            const yh::Rect button{10, y, 280, 40};
            if (ui_.button(button, text, hurt && left != 0))
                act("rest", nlohmann::json{{"rest", i}}.dump());
            uiRects_.push_back(button);
        }
    }
}

// ---------------------------------------------------------------- menus

void YoreholdGame::openMenu(Menu menu)
{
    if (menu == Menu::Settings)
        settingsBack_ = menu_ == Menu::Pause ? Menu::Pause : Menu::Main;
    menu_ = menu;
}

void YoreholdGame::startNew()
{
    if (!chapter_)
        return;
    newAdventure(SDL_GetTicks());
    menu_ = Menu::None;
    notice_.clear();
}

void YoreholdGame::continueSaved()
{
    if (!chapter_)
        return;
    menu_ = Menu::None;
    notice_.clear();
    if (!loadAdventure())
        say("No save to continue. Starting a new adventure.");
}

void YoreholdGame::drawMenu(yh::Renderer& renderer)
{
    ui_.begin(renderer, input_);
    const yh::Rect screen = renderer.bounds();
    const bool paused = menu_ == Menu::Pause || (menu_ == Menu::Settings && settingsBack_ == Menu::Pause);
    renderer.fillRect(screen, paused ? yh::Color{4, 4, 10, 150} : yh::Color{4, 4, 10, 200});
    if (title_)
    {
        title_->drawCentered(renderer, {0, screen.h * 0.12f, screen.w, 80}, paused ? "Paused" : "Yorehold", {255, 214, 140, 255});
        if (!paused && ui_.theme.font)
        {
            const std::string subtitle = chapter_ ? chapter_->title : "Content couldn't be loaded";
            ui_.label({screen.w / 2 - ui_.theme.font->measure(subtitle) / 2, screen.h * 0.12f + 88}, subtitle, ui_.theme.textDim);
        }
    }
    if (!paused && !online_.status().empty() && ui_.theme.font)
        ui_.label({screen.w - 16 - ui_.theme.font->measure(online_.status()), screen.h - 30}, online_.status(), ui_.theme.textDim);

    const float w = menu_ == Menu::Adventures ? 460.0f : 280.0f, h = 48, gap = 14, x = screen.w / 2 - w / 2;
    float y = screen.h * 0.36f;
    auto button = [&](std::string_view text, bool enabled = true) {
        const bool clicked = ui_.button({x, y, w, h}, text, enabled);
        y += h + gap;
        return clicked;
    };
    auto quitGame = [] {
        SDL_Event quit{};
        quit.type = SDL_EVENT_QUIT;
        SDL_PushEvent(&quit);
    };

    switch (menu_)
    {
    case Menu::None:
        break;
    case Menu::Main:
        if (button("Play (Enter)", chapter_ != nullptr))
            openMenu(Menu::Play);
        if (button("Create"))
            openMenu(Menu::Create);
        if (button("Settings"))
            openMenu(Menu::Settings);
        if (button("Exit"))
            quitGame();
        break;
    case Menu::Play:
        if (hasSave_ && button("Continue (Enter)", chapter_ != nullptr))
            continueSaved();
        else if (button(hasSave_ ? "New adventure" : "New adventure (Enter)", chapter_ != nullptr))
            startNew();
        if (button("Join co-op", chapter_ != nullptr))
            openMenu(Menu::Join);
        if (button("Adventures (" + std::to_string(adventures_.size()) + ")"))
            openMenu(Menu::Adventures);
        if (button("Back (Esc)"))
            openMenu(Menu::Main);
        break;
    case Menu::Join:
    {
        // The host picks Host co-op in their pause menu; both need the same adventure selected.
        const yh::Rect panel{x - 110, y, w + 220, 214};
        ui_.panel(panel);
        ui_.label({panel.x + 20, panel.y + 16}, "Join a friend's game: " + (chapter_ ? chapter_->title : std::string()), ui_.theme.accent);
        ui_.label({panel.x + 20, panel.y + 58}, "Host address");
        ui_.textBox("join-address", {panel.x + 180, panel.y + 50, panel.w - 200, 40}, settings_.joinAddress, 253);
        ui_.label({panel.x + 20, panel.y + 112}, "Your name");
        ui_.textBox("join-name", {panel.x + 180, panel.y + 104, panel.w - 200, 40}, settings_.playerName, 32);
        ui_.label({panel.x + 20, panel.y + 164}, client_ ? "Connecting..." : "The host's port is " + std::to_string(coopPort()) + ". Same adventure on both sides.",
            ui_.theme.textDim);
        y += panel.h + gap;
        if (button(client_ ? "Cancel" : "Join", chapter_ != nullptr))
        {
            if (client_)
                endSession("Cancelled.");
            else
            {
                saveSettings();
                joinSession(settings_.joinAddress);
            }
        }
        if (button("Back (Esc)"))
        {
            if (client_)
                endSession("Cancelled.");
            openMenu(Menu::Play);
        }
        break;
    }
    case Menu::Adventures:
    {
        // Five to a page; the last button turns the page when there are more.
        const size_t perPage = 5, pages = (adventures_.size() + perPage - 1) / perPage;
        if (adventurePage_ >= pages)
            adventurePage_ = 0;
        std::optional<size_t> picked;
        for (size_t i = adventurePage_ * perPage; i < adventures_.size() && i < (adventurePage_ + 1) * perPage; i++)
        {
            const ContentLibrary::Adventure& a = adventures_[i];
            std::string text = a.title;
            if (!a.packageName.empty() && a.packageName != a.title)
                text += "  (" + a.packageName + ")";
            if (ui_.toggle({x, y, w, h}, text, i == adventure_ && chapter_ != nullptr))
                picked = i;
            y += h + gap;
        }
        if (pages > 1 && button("More (" + std::to_string(adventurePage_ + 1) + "/" + std::to_string(pages) + ")"))
            adventurePage_ = (adventurePage_ + 1) % pages;
        // Installed files with nothing to play (classes, items, creatures only).
        for (const ContentLibrary::Package& package : packages_)
            if (package.adventures.empty())
            {
                ui_.label({x, y}, package.name + ": " + std::to_string(package.classes) + " classes, " + std::to_string(package.items)
                    + " items, " + std::to_string(package.creatures) + " creatures", ui_.theme.textDim);
                y += 26;
            }
        ui_.label({x, y}, "Open a .yore file, or drop it on this window, to add it.", ui_.theme.textDim);
        y += 26 + gap;
        if (button("Back (Esc)"))
            openMenu(Menu::Play);
        if (picked)
        {
            selectAdventure(*picked);
            notice_.clear();
            if (chapter_)
                menu_ = Menu::Play;
            return; // the fonts were reloaded; draw the menu again next frame
        }
        break;
    }
    case Menu::Create:
    {
        const yh::Rect panel{screen.w / 2 - 300, y, 600, 200};
        ui_.panel(panel);
        ui_.label({panel.x + 20, panel.y + 18}, "Create: design, build, playtest and share", ui_.theme.accent);
        ui_.label({panel.x + 20, panel.y + 50}, "Plan the game and UI before building its editing tools.");
        ui_.label({panel.x + 20, panel.y + 76}, "Maps, walls, lights, tokens, encounters and dialogue.");
        ui_.label({panel.x + 20, panel.y + 102}, "Chapter writers own placement, story and cutscenes.");
        ui_.label({panel.x + 20, panel.y + 134}, "The visual editor is planned; content files work now.", ui_.theme.textDim);
        ui_.label({panel.x + 20, panel.y + 164}, "Compendium: " + std::to_string(compendium_.classes.size()) + " classes, "
            + std::to_string(compendium_.items.size()) + " items, " + std::to_string(compendium_.creatures.size())
            + " creatures (add more by opening .yore files)", ui_.theme.textDim);
        y += panel.h + gap;
        if (button("Back (Esc)"))
            openMenu(Menu::Main);
        break;
    }
    case Menu::Settings:
        y = screen.h * 0.25f + 20;
        drawSettings({screen.w / 2 - 260, y, 520, 454});
        y += 454 + gap;
        if (button("Back (Esc)"))
            openMenu(settingsBack_);
        break;
    case Menu::Pause:
        if (button("Resume (Esc)"))
            openMenu(Menu::None);
        if (button("Settings"))
            openMenu(Menu::Settings);
        if (!inSession() && button("Host co-op (port " + std::to_string(coopPort()) + ")", !testRun_ || SDL_getenv("YOREHOLD_HOST")))
            hostSession();
        else if (host_ && button("Stop hosting"))
            endSession("The host stopped the game.");
        if (button(client_ ? "Leave and quit to title" : "Save and quit to title"))
        {
            saveAdventure();
            autoPlay_ = false;
            if (inSession())
                endSession(client_ ? "You left." : "The host left.");
            openMenu(Menu::Main);
        }
        break;
    }
    if (!notice_.empty() && !paused)
    {
        const yh::Rect noticeArea{20, screen.h - (chapterError_.empty() ? 108.0f : 180.0f), screen.w - 40, 64};
        ui_.panel(noticeArea);
        ui_.label({noticeArea.x + 12, noticeArea.y + 12}, notice_, noticeBad_ ? ui_.theme.bad : ui_.theme.good);
    }
    if (!chapterError_.empty())
    {
        const yh::Rect errorArea{20, screen.h - 108, screen.w - 40, 64};
        ui_.panel(errorArea);
        ui_.label({errorArea.x + 12, errorArea.y + 12}, chapterError_, ui_.theme.bad);
    }
    ui_.label({12, screen.h - 30}, "F12: screenshot + note    F3: frame times", ui_.theme.textDim);
}

void YoreholdGame::drawSettings(const yh::Rect& area)
{
    ui_.panel(area);
    const Settings before = settings_;
    const float x = area.x + 20, w = area.w - 40, h = 36;
    float y = area.y + 16;

    ui_.label({x, y + 10}, "Controls", ui_.theme.textDim);
    const float half = (w - 120 - 10) / 2;
    if (ui_.toggle({x + 120, y, half, h}, "BG3", settings_.controls == yh::ControlPreset::BG3))
        settings_.controls = yh::ControlPreset::BG3;
    if (ui_.toggle({x + 130 + half, y, half, h}, "Foundry", settings_.controls == yh::ControlPreset::Foundry))
        settings_.controls = yh::ControlPreset::Foundry;
    y += h + 12;
    ui_.checkbox({x, y, w, h}, "Zoom toward the cursor (off: screen centre)", settings_.zoomToCursor);
    y += h + 8;
    ui_.checkbox({x, y, w, h}, "Pan when the mouse touches a screen edge", settings_.edgeScroll);
    y += h + 8;
    ui_.checkbox({x, y, w, h}, "Camera follows the moving character", settings_.cameraFollows);
    y += h + 8;
    ui_.checkbox({x, y, w, h}, "Fullscreen", settings_.fullscreen);
    y += h + 8;
    ui_.checkbox({x, y, w, h}, "Shared party view (off: only the selected hero's)", settings_.sharedFog);
    y += h + 12;
    ui_.label({x, y + 10}, "Lighting", ui_.theme.textDim);
    const std::array<const char*, 4> modes{"Map", "Off", "Mood", "Rules"};
    const float quarter = (w - 120 - 30) / 4;
    for (int i = 0; i < 4; i++)
        if (ui_.toggle({x + 120 + i * (quarter + 10), y, quarter, h}, modes[i], settings_.lighting == i))
            settings_.lighting = i;
    y += h + 8;
    ui_.label({x, y + 10}, "Time of day", ui_.theme.textDim);
    const std::array<const char*, 4> times{"Map", "Day", "Dusk", "Night"};
    for (int i = 0; i < 4; i++)
        if (ui_.toggle({x + 120 + i * (quarter + 10), y, quarter, h}, times[i], settings_.timeOfDay == i))
            settings_.timeOfDay = i;
    y += h + 8;
    // Skins: click to step through the ones in the skins folder.
    ui_.label({x, y + 10}, "Skin", ui_.theme.textDim);
    const float folderWidth = 110;
    if (ui_.button({x + 120, y, w - 120 - folderWidth - 10, h}, settings_.skin.empty() ? std::string("Default") : settings_.skin))
    {
        refreshSkins();
        const auto current = std::find(skins_.begin(), skins_.end(), settings_.skin);
        settings_.skin = settings_.skin.empty() ? (skins_.empty() ? std::string() : skins_.front())
                       : current == skins_.end() || current + 1 == skins_.end() ? std::string() : *(current + 1);
    }
    if (ui_.button({x + w - folderWidth, y, folderWidth, h}, "Folder", !skinsDir().empty()))
    {
        prepareSkinsFolder();
        SDL_OpenURL(("file:///" + std::filesystem::path(skinsDir()).generic_string()).c_str());
    }
    y += h + 14;
    char text[48];
    std::snprintf(text, sizeof(text), "Pan speed  %.0f", settings_.panSpeed);
    ui_.label({x, y}, text, ui_.theme.textDim);
    ui_.slider({x + 160, y + 2, w - 160, 18}, settings_.panSpeed, 200, 3000);

    const bool changed = before.controls != settings_.controls || before.zoomToCursor != settings_.zoomToCursor
        || before.edgeScroll != settings_.edgeScroll || before.cameraFollows != settings_.cameraFollows
        || before.fullscreen != settings_.fullscreen || before.panSpeed != settings_.panSpeed
        || before.lighting != settings_.lighting || before.sharedFog != settings_.sharedFog
        || before.timeOfDay != settings_.timeOfDay || before.skin != settings_.skin;
    if (before.skin != settings_.skin)
        skinChanged_ = true; // swapped at the next update, once this frame's text has been drawn
    if (changed)
    {
        applySettings();
        settingsDirty_ = true;
    }
    // Sliders change every frame while dragged; write the file once the mouse is let go.
    if (settingsDirty_ && !input_.buttonDown(yh::MouseButton::Left))
    {
        saveSettings();
        settingsDirty_ = false;
    }
}

void YoreholdGame::drawParty(yh::Renderer& renderer)
{
    const std::optional<size_t> current = currentCreature();
    const bool fighting = encounter_ && !encounter_->finished();
    char text[96];
    for (size_t i = 0; i < heroCount_; i++)
    {
        const yh::Character& c = creatures_[i].sheet;
        const yh::Rect area{10, 10 + i * 66.0f, 280, 58};
        uiRects_.push_back(area);
        ui_.panel(area);
        if (current && *current == i)
            renderer.drawRect(area, ui_.theme.accent, 3);
        else if (tokens_.tokens[i].selected)
            renderer.drawRect(area, {200, 180, 140, 255}, 3);
        // Portrait slot: a dark square with the hero's colour inside.
        const yh::Rect portrait{area.x + 8, area.y + 9, 40, 40};
        renderer.fillRect(portrait, ui_.theme.panelBorder);
        renderer.fillRect({portrait.x + 3, portrait.y + 3, 34, 34}, c.down() ? yh::Color{80, 80, 90, 255} : tokens_.tokens[i].color);

        std::snprintf(text, sizeof(text), "%s  Lv %d %s", c.name.c_str(), c.level, c.characterClass.c_str());
        ui_.label({area.x + 58, area.y + 6}, inSession() ? c.name + "  (" + seatName(i) + ")" : std::string(text),
            c.down() ? ui_.theme.textDim : mine(i) ? ui_.theme.text : ui_.theme.textDim);
        const float fraction = std::clamp(static_cast<float>(c.hp) / std::max(1, c.maxHp()), 0.0f, 1.0f);
        ui_.bar({area.x + 58, area.y + 32, 110, 16}, fraction, fraction > 0.5f ? ui_.theme.good : ui_.theme.bad);
        std::snprintf(text, sizeof(text), c.down() ? "Down" : "%d/%d  AC %d", c.hp, c.maxHp(), c.armorClass(rules_));
        ui_.label({area.x + 176, area.y + 30}, text, ui_.theme.textDim);

        // Clicking a portrait while exploring makes that hero the leader.
        if (!fighting && !c.down() && mine(i) && ui_.hovered(area) && input_.buttonClicked(yh::MouseButton::Left))
        {
            for (size_t j = 0; j < tokens_.tokens.size(); j++)
                tokens_.tokens[j].selected = j == i;
        }
    }
}

void YoreholdGame::drawInitiative(yh::Renderer& renderer)
{
    const auto& order = encounter_->order();
    const float boxWidth = 120, gap = 6;
    const float total = order.size() * (boxWidth + gap) - gap;
    float x = std::max(310.0f, renderer.bounds().w / 2 - total / 2);
    const yh::Rect strip{x - 8, 8, total + 16, 64};
    ui_.panel(strip);
    uiRects_.push_back(strip);

    char text[64];
    std::snprintf(text, sizeof(text), "Round %d", encounter_->round());
    ui_.label({strip.x + 8, strip.y + strip.h + 4}, text, ui_.theme.accent);
    for (size_t i = 0; i < order.size(); i++, x += boxWidth + gap)
    {
        const yh::Combatant& c = order[i];
        const yh::Rect box{x, 14, boxWidth, 52};
        const bool now = i == encounter_->currentIndex();
        renderer.fillRect(box, now ? yh::Color{92, 76, 116, 255} : yh::Color{46, 40, 54, 255});
        renderer.fillRect({box.x, box.y, box.w, 4}, c.team == 0 ? ui_.theme.good : ui_.theme.bad); // team stripe
        renderer.drawRect(box, now ? ui_.theme.accent : ui_.theme.panelBorder, 3);
        const yh::Color color = c.character->down() ? ui_.theme.textDim : ui_.theme.text;
        std::snprintf(text, sizeof(text), "%d  %s", c.initiative, c.character->name.c_str());
        ui_.label({box.x + 7, box.y + 6}, text, color);
        const float fraction = std::clamp(static_cast<float>(c.character->hp) / std::max(1, c.character->maxHp()), 0.0f, 1.0f);
        ui_.bar({box.x + 6, box.y + 32, box.w - 12, 10}, fraction, c.team == 0 ? ui_.theme.good : ui_.theme.bad);
    }
}

void YoreholdGame::drawCombatBar(yh::Renderer& renderer)
{
    const std::optional<size_t> current = currentCreature();
    if (!current)
        return;
    const yh::Rect screen = renderer.bounds();
    const yh::Rect bar{10, screen.h - 76, std::min(660.0f, screen.w - 490), 66};
    ui_.panel(bar);
    uiRects_.push_back(bar);

    const yh::Combatant& c = encounter_->current();
    char text[160];
    if (creatures_[*current].team != 0)
    {
        std::snprintf(text, sizeof(text), "%s is taking their turn...", c.character->name.c_str());
        ui_.label({bar.x + 16, bar.y + 22}, text, ui_.theme.bad);
        return;
    }

    std::snprintf(text, sizeof(text), "%s: move %d sq   action %s", c.character->name.c_str(), c.budget.movementLeft,
        c.budget.action ? "ready" : "used");
    ui_.label({bar.x + 16, bar.y + 8}, text, ui_.theme.accent);
    if (!mine(*current))
    {
        ui_.label({bar.x + 16, bar.y + 36}, seatName(*current) + " is taking this turn.", ui_.theme.textDim);
        return;
    }
    ui_.label({bar.x + 16, bar.y + 36}, c.budget.action ? "Click an enemy to attack." : "Move on, or end your turn.",
        ui_.theme.textDim);

    const bool walking = !tokens_.tokens[*current].path.empty();
    if (ui_.button({bar.x + bar.w - 250, bar.y + 13, 100, 40}, "Dash", c.budget.action && !walking))
        act("dash");
    if (ui_.button({bar.x + bar.w - 140, bar.y + 13, 128, 40}, "End turn", !walking))
        act("end");
}

void YoreholdGame::drawDialogue(yh::Renderer& renderer)
{
    const yh::DialogueNode* node = talk_->current();
    if (!node)
        return;
    const yh::Rect screen = renderer.bounds();
    const std::vector<const yh::DialogueChoice*> choices = talk_->choices();
    // Between the party cards and the log.
    const float width = std::clamp(screen.w - 300 - 460, 360.0f, 720.0f);
    const float line = ui_.lineHeight();
    std::vector<std::string> lines{node->text};
    if (ui_.theme.font)
        lines = ui_.theme.font->wrap(node->text, width - 32);
    const size_t buttons = std::max<size_t>(1, choices.size());
    const float height = 16 + line + lines.size() * line + 10 + buttons * 44 + 8;
    const yh::Rect area{300, screen.h - height - 10, width, height};
    ui_.panel(area);
    uiRects_.push_back(area);

    float y = area.y + 12;
    const std::string speaker = node->speaker.empty() ? chapter_->npcs[talkNpc_].name : node->speaker;
    ui_.label({area.x + 16, y}, speaker, ui_.theme.accent);
    y += line;
    for (const std::string& text : lines)
    {
        ui_.label({area.x + 16, y}, text);
        y += line;
    }
    y += 10;
    if (choices.empty())
    {
        if (ui_.button({area.x + 16, y, area.w - 32, 38}, "1. (Leave)"))
            act("reply", nlohmann::json{{"choice", 0}, {"hero", leaderIndex()}}.dump());
        return;
    }
    for (size_t i = 0; i < choices.size(); i++, y += 44)
    {
        std::string text = std::to_string(i + 1) + ". " + choices[i]->text;
        if (choices[i]->check)
            text += "  [" + choices[i]->check->skill + " " + std::to_string(choices[i]->check->difficulty) + "]";
        if (ui_.button({area.x + 16, y, area.w - 32, 38}, text))
        {
            act("reply", nlohmann::json{{"choice", i}, {"hero", leaderIndex()}}.dump());
            return; // the conversation may be gone now
        }
    }
}

// A small tracker of the active quests (top right); J opens the whole journal.
void YoreholdGame::drawJournal(yh::Renderer& renderer)
{
    const yh::Rect screen = renderer.bounds();
    const std::vector<yh::QuestEntry> entries = journal_->entries(flags_);
    const float line = ui_.lineHeight();
    if (!journalOpen_)
    {
        std::vector<const yh::QuestEntry*> active;
        for (const yh::QuestEntry& e : entries)
            if (e.progress.status == yh::QuestStatus::Active)
                active.push_back(&e);
        if (active.empty() || (encounter_ && !encounter_->finished()))
            return;
        float rows = 0;
        for (const yh::QuestEntry* e : active)
            rows += 1 + e->quest->objectives.size();
        const yh::Rect area{screen.w - 350, 10, 340, 14 + rows * line};
        ui_.panel(area);
        uiRects_.push_back(area);
        float y = area.y + 7;
        for (const yh::QuestEntry* e : active)
        {
            ui_.label({area.x + 12, y}, e->quest->title, ui_.theme.accent);
            y += line;
            for (size_t i = 0; i < e->quest->objectives.size(); i++, y += line)
                ui_.label({area.x + 22, y}, (e->progress.objectiveComplete[i] ? "[x] " : "[ ] ") + e->quest->objectives[i].text,
                    e->progress.objectiveComplete[i] ? ui_.theme.textDim : ui_.theme.text);
        }
        return;
    }

    const yh::Rect area{screen.w / 2 - 300, screen.h * 0.12f, 600, screen.h * 0.7f};
    ui_.panel(area);
    uiRects_.push_back(area);
    if (title_)
        title_->drawCentered(renderer, {area.x, area.y + 10, area.w, 60}, "Journal", {255, 214, 140, 255});
    float y = area.y + 84;
    if (entries.empty())
        ui_.label({area.x + 24, y}, "Nothing yet.", ui_.theme.textDim);
    for (const yh::QuestEntry& e : entries)
    {
        const char* status = e.progress.status == yh::QuestStatus::Completed ? "  (complete)"
            : e.progress.status == yh::QuestStatus::Failed                 ? "  (failed)"
                                                                           : "";
        ui_.label({area.x + 24, y}, e.quest->title + status,
            e.progress.status == yh::QuestStatus::Active ? ui_.theme.accent : ui_.theme.textDim);
        y += line;
        if (!e.quest->description.empty())
        {
            std::vector<std::string> lines{e.quest->description};
            if (ui_.theme.font)
                lines = ui_.theme.font->wrap(e.quest->description, area.w - 60);
            for (const std::string& text : lines)
            {
                ui_.label({area.x + 36, y}, text, ui_.theme.textDim);
                y += line;
            }
        }
        for (size_t i = 0; i < e.quest->objectives.size(); i++, y += line)
            ui_.label({area.x + 36, y}, (e.progress.objectiveComplete[i] ? "[x] " : "[ ] ") + e.quest->objectives[i].text);
        y += 12;
    }
    ui_.label({area.x + 24, area.y + area.h - 34}, "J or Esc: close", ui_.theme.textDim);
}
