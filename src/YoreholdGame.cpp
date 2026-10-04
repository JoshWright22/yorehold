#include "YoreholdGame.h"

#include "sim/Save.h"

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


float distance(yh::Vec2 a, yh::Vec2 b)
{
    const yh::Vec2 d = a - b;
    return std::sqrt(d.x * d.x + d.y * d.y);
}

}

YoreholdGame::YoreholdGame(std::vector<std::string> openFiles) : World(files_)
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
    saves_ = !testRun_;
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
    drainEvents();
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
    applyServerAi(online_.config().value("ai", nlohmann::json::object()));
    hasSave_ = !testRun_ && chapter_ && Save::format().readFile(savePath()).has_value();
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
    drainEvents();
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


void YoreholdGame::applyScheme(yh::ControlPreset preset)
{
    scheme_ = yh::makeControlScheme(preset);
    input_.setMap(scheme_.map);
    controls_.settings.edgeScroll = scheme_.edgeScroll;
}

void YoreholdGame::drainEvents()
{
    static const bool printLog = SDL_getenv("YOREHOLD_PRINT_LOG") != nullptr;
    using Kind = World::Event::Kind;
    // Handling one can add more (a save that fails says so).
    for (std::vector<World::Event> events = takeEvents(); !events.empty(); events = takeEvents())
    {
        for (World::Event& event : events)
        {
            switch (event.kind)
            {
            case Kind::Reset:
                floaters_.clear();
                log_.clear();
                cutscene_ = {};
                cutsceneDone_ = false;
                journalOpen_ = false;
                cameraPlaced_ = false;
                break;
            case Kind::Resumed:
                log_.clear();
                bannerTime_ = 0;
                break;
            case Kind::Log:
                if (autoPlay_ || printLog) // headless runs read the story from stdout
                    std::printf("log: %s\n", event.text.c_str());
                log_.push_back(std::move(event.text));
                if (log_.size() > 200)
                    log_.erase(log_.begin(), log_.begin() + 50);
                break;
            case Kind::Floater:
            {
                using Float = World::FloatKind;
                const yh::Color color = event.floater == Float::Miss ? yh::Color{200, 200, 210, 255}
                    : event.floater == Float::Hit ? yh::Color{255, 90, 70, 255}
                    : event.floater == Float::Critical ? yh::Color{255, 200, 60, 255}
                    : event.floater == Float::Heal ? ui_.theme.good : yh::Color{150, 200, 255, 255};
                floaters_.push_back({event.at, std::move(event.text), color});
                break;
            }
            case Kind::Banner:
                banner_ = std::move(event.text);
                bannerTime_ = event.seconds;
                break;
            case Kind::Camera:
                camera_.moveTo(event.at);
                break;
            case Kind::Follow:
                controls_.resumeFollowing();
                break;
            case Kind::Ending:
                if (std::optional<yh::Cutscene> ending = yh::Cutscene::fromJson(event.text))
                {
                    cutscene_ = std::move(*ending);
                    cutsceneDone_ = false;
                    bannerTime_ = 0;
                    controls_.resumeFollowing();
                    cutscene_.start();
                }
                break;
            case Kind::Save:
                writeSave(event.text);
                break;
            case Kind::Talk:
                journalOpen_ = false;
                break;
            }
        }
    }
}

bool YoreholdGame::handleEvent(const SDL_Event& event)
{
    const bool handled = handle(event);
    drainEvents();
    return handled;
}

bool YoreholdGame::handle(const SDL_Event& event)
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
        drainEvents(); // what was said so far is printed (or not) by the old setting
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
    if (keyDown && event.key.key == SDLK_C)
    {
        act("sneak", nlohmann::json{{"on", !sneakingMine()}}.dump());
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
        return "screen: talking " + (creatures_[talkWith_].npc >= 0 ? chapter_->npcs[creatures_[talkWith_].npc].id : creatures_[talkWith_].sheet.name)
            + " at " + talk_->current()->id;
    return sneakingMine() ? "screen: exploring, sneaking" : "screen: exploring";
}

// ---------------------------------------------------------------- update

void YoreholdGame::update(double deltaSeconds)
{
    step(deltaSeconds);
    drainEvents();
}

void YoreholdGame::step(double deltaSeconds)
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
        const std::vector<std::string> changed = files_.pollChanges();
        if (!changed.empty())
            releaseAssets();
        if (std::any_of(changed.begin(), changed.end(), [](const std::string& path) {
                return path.starts_with("ai/") || path.find("/ai/") != std::string::npos || path.starts_with("creatures/") || path.find("/creatures/") != std::string::npos;
            }))
            reloadAi(online_.config().value("ai", nlohmann::json::object()));
    }
    // The server's settings are read at sign-in and again every minute, so a change there reaches
    // a running game without a restart.
    configTimer_ -= deltaSeconds;
    if (configTimer_ <= 0 && online_.state() == Online::State::SignedIn)
    {
        configTimer_ = 60;
        online_.refreshConfig();
    }
    if (online_.configVersion() != configSeen_)
    {
        configSeen_ = online_.configVersion();
        applyServerAi(online_.config().value("ai", nlohmann::json::object()));
        if (aiNotes_)
            say("AI settings from the server: " + std::to_string(serverProfiles_.size()) + " profiles, " + std::to_string(serverCreatureAi_.size()) + " creatures.");
    }
    updateSession(deltaSeconds);
    online_.update();
    if (online_.status() != onlineStatus_)
    {
        drainEvents(); // keeps printed lines in the order they happened
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
    tokens_.handleInput(tokenInput, camera_, grid_, passable);
    walk(deltaSeconds);
    if (&tokenInput == &input_ && input_.clicked(yh::actions::moveTo))
        controls_.resumeFollowing();
    shareWalking(deltaSeconds);

    // Clicking someone to talk to walks the leader over; the conversation opens on arrival.
    if (tokensListen && (input_.clicked(yh::actions::moveTo) || input_.clicked(yh::actions::select)))
        if (const std::optional<size_t> who = hoveredTalker())
            walkToTalk(*who);
    arrive();

    // Right-click menu. On an NPC or someone who surrendered: Talk walks over, Attack picks a fight with them.
    const std::optional<size_t> menuTalker = tokens_.contextChoice && tokens_.contextChoice->first < creatures_.size()
            && talkable(tokens_.contextChoice->first)
        ? std::optional<size_t>(tokens_.contextChoice->first) : std::nullopt;
    if (menuTalker && tokens_.contextChoice->second != "Inspect")
    {
        if (fighting)
            say("Not in the middle of a fight.");
        else if (tokens_.contextChoice->second == "Attack")
            act("provoke", nlohmann::json{{"creature", *menuTalker}}.dump());
        else
            walkToTalk(*menuTalker);
    }
    else if (tokens_.contextChoice && tokens_.contextChoice->first < creatures_.size())
    {
        const auto [index, action] = *tokens_.contextChoice;
        if (action == "Attack" && fighting && heroTurn && creatures_[index].team == 1 && mine(*current))
            tryAttack(index);
        else if (action == "Attack" && !fighting && creatures_[index].team == 1 && !creatures_[index].awake && sneakingMine())
            act("ambush", nlohmann::json{{"creature", index}}.dump());
        else if (action == "Inspect" || action == "Attack")
        {
            const yh::Character& c = creatures_[index].sheet;
            say(c.name + ": " + c.characterClass + ", HP " + std::to_string(c.hp) + "/" + std::to_string(c.maxHp()) + ", AC " +
                std::to_string(c.armorClass(rules_)));
        }
    }

    takeTurns(deltaSeconds, !menuOpen, [this] { heroInput(); });
    drainEvents(); // the world's camera moves land before the camera follows

    std::optional<yh::Vec2> follow = tokens_.followTarget();
    if (const std::optional<size_t> now = currentCreature(); now && encounter_ && !encounter_->finished())
        follow = tokens_.tokens[*now].position;
    controls_.update(camera_, input_, deltaSeconds, follow);

    for (Floater& floater : floaters_)
        floater.age += static_cast<float>(deltaSeconds);
    std::erase_if(floaters_, [](const Floater& f) { return f.age > 1.4f; });
}

void YoreholdGame::finishAdventure()
{
    endCutscene();
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
    drainEvents();
    autoPlay_ = false;
    newAdventure(SDL_GetTicks());
    menu_ = testRun_ ? Menu::None : Menu::Main;
}


std::optional<size_t> YoreholdGame::hoveredTalker() const
{
    if (!chapter_)
        return std::nullopt;
    const yh::Vec2 world = camera_.screenToWorld(input_.mouse());
    for (size_t i = heroCount_; i < creatures_.size(); i++)
    {
        const yh::Token& token = tokens_.tokens[i];
        if (talkable(i) && token.floor == 0 && distance(world, token.position) <= token.radius)
            return i;
    }
    return std::nullopt;
}

void YoreholdGame::heroInput()
{
    const size_t me = *currentCreature();
    const yh::Token& token = tokens_.tokens[me];

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
    setOptions({settings_.lighting, settings_.timeOfDay, settings_.sharedFog});
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
    if (!testRun_ && canSave())
        writeSave(stateJson());
}

void YoreholdGame::writeSave(const std::string& state)
{
    std::string error;
    if (Save::format().writeFile(savePath(), state, &error))
        hasSave_ = true;
    else
        say("Couldn't save: " + error);
}


bool YoreholdGame::loadAdventure()
{
    if (!chapter_)
        return false;
    std::string error;
    const std::optional<std::string> text = Save::format().readFile(savePath(), &error);
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
    render(renderer);
    drainEvents();
}

void YoreholdGame::render(yh::Renderer& renderer)
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
        if (tokens_.tokens[i].floor != dead && map().lighting().carried > 0 && !creatures_[i].sneaking)
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
    // While sneaking, show where each enemy that hasn't noticed the party is looking.
    if (!(encounter_ && !encounter_->finished()) && sneakingMine())
    {
        const std::vector<yh::Watcher> watching = watchers();
        for (size_t w = 0; w < watching.size(); w++)
        {
            if (watching[w].range <= 0 || tokens_.tokens[heroCount_ + w].floor != 0)
                continue;
            const std::vector<yh::Vec2> cone = yh::visionCone(watching[w], map().walls(), 40);
            for (size_t p = 1; p < cone.size(); p++)
            {
                // The tint is a fan of wide lines; started a little way out so they don't pile up over the token.
                renderer.drawLine(cone[0] + (cone[p] - cone[0]) * 0.2f, cone[p], {230, 60, 50, 26}, 8);
                if (p > 1)
                    renderer.drawLine(cone[p - 1], cone[p], {230, 60, 50, 170}, 2);
            }
            renderer.drawLine(cone[0], cone[1], {230, 60, 50, 170}, 2);
            renderer.drawLine(cone.back(), cone[0], {230, 60, 50, 170}, 2);
        }
    }
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
        hint += "   C: sneak";
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
        const yh::Rect sneak{10, y, 280, 40};
        if (ui_.button(sneak, sneakingMine() ? "Stop sneaking (C)" : "Sneak (C)"))
            act("sneak", nlohmann::json{{"on", !sneakingMine()}}.dump());
        uiRects_.push_back(sneak);
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

    std::snprintf(text, sizeof(text), "%s: move %d sq   actions %d", c.character->name.c_str(), c.budget.movementLeft,
        c.budget.actions);
    ui_.label({bar.x + 16, bar.y + 8}, text, ui_.theme.accent);
    if (!mine(*current))
    {
        ui_.label({bar.x + 16, bar.y + 36}, seatName(*current) + " is taking this turn.", ui_.theme.textDim);
        return;
    }
    ui_.label({bar.x + 16, bar.y + 36}, encounter_->canStrike() ? "Click an enemy to attack." : "Move on, or end your turn.",
        ui_.theme.textDim);

    const bool walking = !tokens_.tokens[*current].path.empty();
    if (ui_.button({bar.x + bar.w - 250, bar.y + 13, 100, 40}, "Dash", encounter_->canAct() && !walking))
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
    const std::string speaker = node->speaker.empty() ? creatures_[talkWith_].sheet.name : node->speaker;
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
