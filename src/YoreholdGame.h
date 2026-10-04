#pragma once

#include "content/Chapter.h"
#include "content/CharacterDraft.h"
#include "content/CharacterLibrary.h"
#include "content/ContentPackage.h"
#include "online/Online.h"
#include "screens/CreateScreen.h"
#include "screens/PlayScreen.h"
#include "sim/World.h"

#include <yorehold/framework/Host.h>
#include <yorehold/framework/assets/Assets.h>
#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/input/ControlScheme.h>
#include <yorehold/framework/net/Session.h>
#include <yorehold/framework/ui/Ui.h>

#include <memory>
#include <optional>
#include <string>
#include <vector>

// Everything the game reads: the framework's and the game's files, then the adventure and the skin.
struct GameFiles
{
    yh::FileSystem files_;
};

// The game: the title and pause menus, settings, the adventure library, saves and co-op around
// the World being played, which the PlayScreen shows.
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

protected:
    // For test scenes: picks the aimed action as its button on the action bar would.
    void armAction(std::string action) { play_.arm(std::move(action)); }
    yh::Camera& playCamera() { return play_.camera(); }

private:
    int configSeen_ = 0;
    double configTimer_ = 0;

    void applyScheme(yh::ControlPreset preset);
    // Shows, prints and plays what the world has to tell since the last time.
    void drainEvents();
    void step(double deltaSeconds);
    bool handle(const SDL_Event& event);
    void render(yh::Renderer& renderer);

    // One autosave slot, written after victories and rests (never mid-fight).
    std::string savePath() const;
    void saveAdventure();
    void writeSave(const std::string& state);
    bool loadAdventure();
    // The character library beside the save. Heroes that came from it are written back when the
    // player leaves (still "away" in this adventure) and when the adventure ends (free again).
    std::string charactersDir() const;
    void writeBackCharacters(bool finished);

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

    // Title menus and the in-game pause menu (Esc), in screens/Menus.cpp. Settings are shared by both.
    enum class Menu { None, Main, Play, Adventures, Create, Settings, Pause, Join, Characters, NewCharacter, LevelUp, Party };
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
        bool reactionPrompts = false;
        std::string playerName = "Player";
        std::string joinAddress = "127.0.0.1"; // the last co-op host joined
        std::string lastPackage; // the adventure picked last time: installed file name ("" = built in)
        std::string lastFolder;
        std::string lastCreatePackage; // the content package opened last time in Create mode
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

    // Skins and settings (Settings.cpp). Skins are folders (or .yoreskin zips) in the skins folder
    // whose files replace the default look.
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

    // Play > Characters: the library, making a character and levelling one up (screens/CharacterScreens.cpp).
    void loadCharacters();
    void openCharacters();
    void showCharacter(size_t index);
    void newCharacter(Menu back = Menu::Characters);
    // Play > New adventure: one seat per hero the chapter has, each with its ready-made hero, a
    // character from the library or one made on the spot. Enter on the Play menu skips it.
    void openParty();
    void drawParty(const yh::Rect& screen);
    void startParty();
    // Characters left "away" in this adventure's save are free again once it is started over.
    void releaseCharacters();
    std::vector<std::optional<CharacterLibrary::Entry>> partyChoice_; // per seat; empty = the ready-made hero
    size_t partySeat_ = 0;
    Menu draftBack_ = Menu::Characters; // where making a character returns to
    void drawCharacters(const yh::Rect& screen);
    void drawDraft(const yh::Rect& screen);
    void drawSheet(const yh::Rect& area, const yh::Character* sheet, const yh::CharacterChoices& choices, const std::string& problem);
    void finishDraft();
    yh::Ruleset creationRules_;       // the game's own ruleset, which every character is made under
    yh::Compendium creationCompendium_; // compendium_ plus the ruleset's races, backgrounds and feats
    std::string creationError_;
    std::vector<CharacterLibrary::Entry> characters_;
    size_t characterPick_ = 0;
    size_t characterPage_ = 0;
    std::optional<yh::Character> pickedSheet_; // characters_[characterPick_] as built now
    std::string pickedProblem_;
    std::optional<CharacterDraft> draft_;
    yh::Random draftDice_{1};

    // After the end cutscene, back to the title (the finished adventure's save is removed).
    void finishAdventure();

    std::string chapterError_; // why no chapter is loaded (shown on the title)
    std::string themePath_;
    std::vector<ContentLibrary::Adventure> adventures_;
    std::vector<ContentLibrary::Package> packages_; // installed .yore files
    yh::Compendium compendium_; // built-in plus installed definitions, for making characters and adventures
    size_t adventure_ = 0; // index into adventures_
    size_t adventurePage_ = 0;
    std::string notice_; // result of the last added file, shown on the title menus
    bool noticeBad_ = false;
    // Create screen: edit content packages (CreateScreen.cpp)
    void openCreateScreen();
    void closeCreateScreen();
    void playtestPackage();
    void exportPackage(const std::string& path);

    yh::ControlScheme scheme_;
    yh::Input input_;
    yh::Ui ui_;
    std::unique_ptr<yh::Assets> assets_;
    yh::Font* title_ = nullptr;
    PlayScreen play_{*this, ui_, input_, title_};
    CreateScreen create_{ui_, input_, title_};

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
    Menu menu_ = Menu::Main;
    Menu settingsBack_ = Menu::Main; // where Settings' Back button goes
    Settings settings_;
    bool settingsDirty_ = false;
    bool onTitle() const { return menu_ != Menu::None && menu_ != Menu::Pause; }
    bool hasSave_ = false; // checked once, for the title's Continue button
    bool testRun_ = false; // YOREHOLD_SEED set: fixed seed, no autosaves
};
