#include "YoreholdGame.h"

#include "content/CharacterLibrary.h"
#include "sim/Save.h"

#include <yorehold/framework/assets/Skin.h>
#include <yorehold/framework/graphics/Renderer.h>
#include <yorehold/framework/save/SaveFile.h>

#include <nlohmann/json.hpp>

#include <SDL3/SDL_events.h>
#include <SDL3/SDL_filesystem.h>
#include <SDL3/SDL_keycode.h>
#include <SDL3/SDL_stdinc.h>
#include <SDL3/SDL_timer.h>

#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <random>

YoreholdGame::YoreholdGame(std::vector<std::string> openFiles) : World(files_)
{
    files_.mountFolder(YH_FRAMEWORK_ASSETS, "framework");
    files_.mountFolder(YH_GAME_ASSETS, "game");
    play_.finished = [this] { finishAdventure(); };
    play_.walked = [this](double deltaSeconds) { shareWalking(deltaSeconds); };
    applyScheme(yh::ControlPreset::BG3);
    tokens_.contextActions = {"Attack", "Talk", "Inspect"};

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
    // YOREHOLD_MENU=characters|new-character|party opens those screens (for pictures of them).
    if (const char* screen = SDL_getenv("YOREHOLD_MENU"))
    {
        openCharacters();
        if (std::string_view(screen) == "new-character")
            newCharacter();
        else if (std::string_view(screen) == "party")
            openParty();
    }
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
    if (chapterError_.empty() && loadChapter(adventure.folder, &chapterError_))
        play_.camera().setBounds(map().map().worldBounds());
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
    play_.cameraControls().settings.edgeScroll = scheme_.edgeScroll;
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
            if (event.kind == Kind::Log && (autoPlay_ || printLog)) // headless runs read the story from stdout
                std::printf("log: %s\n", event.text.c_str());
            if (event.kind == Kind::Save)
                writeSave(event.text);
            play_.show(event);
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
    if (!onTitle() && play_.handleCutscene(event))
        return true;
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
        if (keyDown && (event.key.key == SDLK_RETURN || event.key.key == SDLK_KP_ENTER) && menu_ == Menu::Party)
        {
            startParty();
            return true;
        }
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
                    : menu_ == Menu::Adventures || menu_ == Menu::Join || menu_ == Menu::Characters || menu_ == Menu::Party ? Menu::Play
                    : menu_ == Menu::NewCharacter || menu_ == Menu::LevelUp ? draftBack_ : Menu::Main);
            if (menu_ != Menu::NewCharacter && menu_ != Menu::LevelUp)
                draft_.reset();
            return true;
        }
        input_.handle(event); // the menus' buttons read the mouse
        return true;
    }
    if (!play_.handle(event))
        openMenu(Menu::Pause);
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
    play_.tick(deltaSeconds);
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
    if (!play_.update(deltaSeconds, menu_ != Menu::None))
        return;
    drainEvents(); // the world's camera moves land before the camera follows
    play_.updateCamera(deltaSeconds);
}

void YoreholdGame::finishAdventure()
{
    if (wiping())
    {
        endCutscene();
        if (client_) play_.banner("Waiting for the host", 1e9);
        return;
    }
    endCutscene();
    // In co-op the host starts the next run for everyone; joined players wait for it.
    if (client_)
    {
        play_.stopCutscene();
        play_.banner("Waiting for the host", 1e9);
        return;
    }
    // A finished adventure can't be continued. Test runs never touch the save.
    if (!testRun_)
    {
        writeBackCharacters(true);
        setParty({}); // the next run starts with the chapter's own heroes
        std::remove(savePath().c_str());
        std::remove((savePath() + ".bak").c_str());
        hasSave_ = false;
    }
    play_.stopCutscene();
    if (host_)
    {
        act("restart", nlohmann::json{{"seed", seed_ + 1}}.dump());
        return;
    }
    drainEvents();
    autoPlay_ = false;
    newAdventure(SDL_GetTicks());
    menu_ = testRun_ ? Menu::None : Menu::Main;
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

void YoreholdGame::saveAdventure()
{
    if (!testRun_ && canSave())
    {
        writeSave(stateJson());
        writeBackCharacters(false);
    }
}

std::string YoreholdGame::charactersDir() const
{
    // Test runs leave the player's characters alone unless they were given a folder of their own.
    if (testRun_ && !SDL_getenv("YOREHOLD_SAVE_DIR"))
        return {};
    const std::string dir = stateDir();
    return dir.empty() ? std::string() : dir + "characters";
}

void YoreholdGame::writeBackCharacters(bool finished)
{
    if (testRun_ || client_ || !chapter_ || charactersDir().empty())
        return;
    const std::string away = finished ? std::string() : std::filesystem::path(savePath()).filename().string();
    for (size_t i = 0; i < heroCount_; i++)
    {
        const Creature& hero = creatures_[i];
        if (hero.library.empty())
            continue;
        std::string error;
        std::optional<CharacterLibrary::Entry> entry = CharacterLibrary::find(charactersDir(), hero.library, &error);
        if (entry)
        {
            entry->choices = hero.choices;
            entry->choices.xp = hero.sheet.xp;
            entry->inventory = hero.sheet.inventory;
            entry->coins = hero.sheet.coins;
            entry->away = hero.sheet.death.dead ? std::string() : away;
        }
        bool written = entry && CharacterLibrary::write(charactersDir(), *entry, &error);
        // Death is permanent: the character stays viewable in the graveyard but can't be played again.
        if (written && hero.sheet.death.dead)
            written = CharacterLibrary::retire(charactersDir(), *entry, &error);
        if (!written)
            say("Couldn't write " + hero.sheet.name + " back to the character library: " + error);
    }
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
    play_.drawWorld(renderer);
    if (menu_ != Menu::None)
    {
        drawMenu(renderer);
        input_.endFrame();
        return;
    }
    play_.table = {inSession(), client_ != nullptr, netStatus_, scheme_.preset};
    play_.drawOverlay(renderer);
    input_.endFrame();
}
