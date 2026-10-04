#pragma once

#include "content/Chapter.h"
#include "content/ContentPackage.h"
#include "online/Online.h"
#include "sim/World.h"

#include <yorehold/framework/Host.h>
#include <yorehold/framework/animation/Cutscene.h>
#include <yorehold/framework/assets/Assets.h>
#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/graphics/Camera.h>
#include <yorehold/framework/graphics/Lighting.h>
#include <yorehold/framework/input/ControlScheme.h>
#include <yorehold/framework/map/CameraControls.h>
#include <yorehold/framework/net/Session.h>
#include <yorehold/framework/ui/Ui.h>

#include <map>
#include <memory>
#include <optional>
#include <set>
#include <string>
#include <unordered_map>
#include <vector>

// Everything the game reads: the framework's and the game's files, then the adventure and the skin.
struct GameFiles
{
    yh::FileSystem files_;
};

// Runs an authored chapter: explore its map and fight its encounters, then play its ending.
class YoreholdGame : public yh::Game, private GameFiles, protected World
{
public:
    // `openFiles`: .yore files the game was started with (double-clicked); they join the library.
    explicit YoreholdGame(std::vector<std::string> openFiles = {});
    ~YoreholdGame() override;

    void unload() override;
    void update(double deltaSeconds) override;
    void draw(yh::Renderer& renderer) override;
    bool handleEvent(const SDL_Event& event) override;
    std::string describe() const override;

private:
    // Damage numbers and "Miss!" that float up from a token.
    struct Floater
    {
        yh::Vec2 world;
        std::string text;
        yh::Color color;
        float age = 0;
    };

    int configSeen_ = 0;
    double configTimer_ = 0;

    void applyScheme(yh::ControlPreset preset);
    // Shows, prints and plays what the world has to tell since the last time.
    void drainEvents();
    void step(double deltaSeconds);
    bool handle(const SDL_Event& event);
    void render(yh::Renderer& renderer);

    // The player's own hero's turn in a fight: clicks and keys.
    void heroInput();

    std::optional<size_t> hoveredTalker() const;
    void drawDialogue(yh::Renderer& renderer);
    void drawJournal(yh::Renderer& renderer);

    // One autosave slot, written after victories and rests (never mid-fight).
    std::string savePath() const;
    void saveAdventure();
    void writeSave(const std::string& state);
    bool loadAdventure();

    // Co-op (Coop.cpp). Everything that changes the shared game goes through act(): alone it
    // applies at once, hosting it goes through the session's rules, joined it goes to the host.
    // Each player moves their own heroes while exploring and their positions are shared.
    bool inSession() const { return host_ || client_; }
    void act(std::string_view type, const std::string& data = "{}") override;
    void hostSession();
    void joinSession(const std::string& address);
    void endSession(const std::string& reason);
    void updateSession(double deltaSeconds);
    void assignSeats();
    void shareWalking(double deltaSeconds);
    static int coopPort();

    // Title menus and the in-game pause menu (Esc). Settings are shared by both.
    enum class Menu { None, Main, Play, Adventures, Create, Settings, Pause, Join };
    struct Settings
    {
        yh::ControlPreset controls = yh::ControlPreset::BG3;
        bool zoomToCursor = false;
        bool edgeScroll = true;
        bool cameraFollows = true;
        float panSpeed = 900;
        bool fullscreen = false;
        int lighting = 0; // 0 = as the map says, else 1 + GameMap::LightingMode
        int timeOfDay = 0; // 0 = as the map says, else 1 + GameMap::Time (day, dusk, night)
        bool sharedFog = true; // the whole party's view; off = only what the selected hero sees
        std::string playerName = "Player";
        std::string joinAddress = "127.0.0.1"; // the last co-op host joined
        std::string lastPackage; // the adventure picked last time: installed file name ("" = built in)
        std::string lastFolder;
        std::string skin; // a name in the skins folder ("" = the default look)
        std::string server;   // the account server, like http://127.0.0.1:7350 ("" = play offline)
        std::string serverKey;
        std::string deviceId;  // names this install to the server; made up on first use
    };
    void connectOnline();
    void drawMenu(yh::Renderer& renderer);
    void drawSettings(const yh::Rect& area);
    void openMenu(Menu menu);
    void startNew();
    void continueSaved();

    // Adventures come from the built-in content plus every .yore in the library folder.
    std::string libraryDir() const;
    void refreshLibrary();
    bool openAdventure(size_t index);
    void selectAdventure(size_t index);
    void addContent(const std::string& file);
    bool installedAdventure() const;
    void releaseAssets();

    // Skins: folders (or .yoreskin zips) in the skins folder whose files replace the default look.
    std::string skinsDir() const;
    void refreshSkins();
    void mountSkin();
    void prepareSkinsFolder();
    std::vector<std::string> skins_; // folder and file names in skinsDir()
    double reloadTimer_ = 0;         // until the next check for edited skin files
    bool skinChanged_ = false;
    void applySettings();
    void saveSettings() const;
    void loadSettings();
    std::string stateDir() const;

    // After the end cutscene, back to the title (the finished adventure's save is removed).
    void finishAdventure();

    std::optional<size_t> hoveredCreature() const;

    void drawWorld(yh::Renderer& renderer);
    void drawHud(yh::Renderer& renderer);
    void drawParty(yh::Renderer& renderer);
    void drawInitiative(yh::Renderer& renderer);
    void drawCombatBar(yh::Renderer& renderer);
    void drawBars(yh::Renderer& renderer);
    bool overUi(yh::Vec2 screen) const;

    std::string chapterError_; // why no chapter is loaded (shown on the title)
    std::string themePath_;
    std::vector<ContentLibrary::Adventure> adventures_;
    std::vector<ContentLibrary::Package> packages_; // installed .yore files
    yh::Compendium compendium_; // built-in plus installed definitions, for making characters and adventures
    size_t adventure_ = 0; // index into adventures_
    size_t adventurePage_ = 0;
    std::string notice_; // result of the last added file, shown on the title menus
    bool noticeBad_ = false;
    yh::Camera camera_;
    yh::CameraControls controls_;
    yh::ControlScheme scheme_;
    yh::Input input_;
    yh::Input noInput_; // fed to the token controller while the mouse is over the UI
    yh::Lighting lighting_;
    yh::Ui ui_;
    std::unique_ptr<yh::Assets> assets_;
    yh::Font* title_ = nullptr;

    std::vector<std::string> log_;
    std::vector<Floater> floaters_;
    std::vector<yh::Rect> uiRects_; // last frame's panels, so clicks on them don't walk the party
    std::string banner_;
    double bannerTime_ = 0;
    double time_ = 0;
    bool cameraPlaced_ = false;
    bool journalOpen_ = false;

    // Co-op. The host is player 0 and owns the enemies; seats_ says who plays each hero.
    std::unique_ptr<yh::SessionHost> host_;
    std::unique_ptr<yh::SessionClient> client_;
    std::string sessionEnded_; // set by the client's disconnect handler, handled after its update
    bool reseat_ = false; // someone joined or left: deal the heroes out again after the host's update
    std::string netStatus_;
    Online online_;
    std::string onlineStatus_; // the last one printed
    double syncTimer_ = 0;
    std::string lastSync_;
    yh::Cutscene cutscene_;
    bool cutsceneDone_ = false; // set by the cutscene's "finished" event, handled after its update
    Menu menu_ = Menu::Main;
    Menu settingsBack_ = Menu::Main; // where Settings' Back button goes
    Settings settings_;
    bool settingsDirty_ = false;
    bool onTitle() const { return menu_ != Menu::None && menu_ != Menu::Pause; }
    bool hasSave_ = false; // checked once, for the title's Continue button
    bool testRun_ = false; // YOREHOLD_SEED set: fixed seed, no autosaves
};
