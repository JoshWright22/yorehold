#pragma once

#include "Chapter.h"
#include "ContentPackage.h"
#include "Online.h"

#include <yorehold/framework/Host.h>
#include <yorehold/framework/animation/Cutscene.h>
#include <yorehold/framework/assets/Assets.h>
#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/graphics/Camera.h>
#include <yorehold/framework/graphics/Lighting.h>
#include <yorehold/framework/input/ControlScheme.h>
#include <yorehold/framework/map/CameraControls.h>
#include <yorehold/framework/map/FogOfWar.h>
#include <yorehold/framework/map/Tokens.h>
#include <yorehold/framework/net/Session.h>
#include <yorehold/framework/rpg/Combat.h>
#include <yorehold/framework/rpg/Dialogue.h>
#include <yorehold/framework/rpg/QuestJournal.h>
#include <yorehold/framework/rpg/Random.h>
#include <yorehold/framework/ui/Ui.h>

#include <map>
#include <memory>
#include <optional>
#include <set>
#include <string>
#include <unordered_map>
#include <vector>

// Runs an authored chapter: explore its map and fight its encounters, then play its ending.
class YoreholdGame : public yh::Game
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
    // Everything on the map that has a sheet. Same order as tokens_.tokens; heroes come first.
    struct Creature
    {
        yh::Character sheet;
        int team = 0;  // 0 = party, 1 = enemies, 2 = neutral NPCs (who turn into enemies if attacked)
        int group = -1; // index into the chapter's encounters (enemies that wake up together); NPCs fight alone
        bool awake = false;
        int npc = -1; // index into the chapter's NPCs
        yh::AiProfile ai;     // how it fights when the game plays it
        bool fleeing = false; // its morale broke this fight: it runs until it gets away or is cornered
        bool fled = false;    // it got away: out of the adventure, and no body is left behind
    };

    // Damage numbers and "Miss!" that float up from a token.
    struct Floater
    {
        yh::Vec2 world;
        std::string text;
        yh::Color color;
        float age = 0;
    };

    enum class EnemyStep { Think, Walk, Strike, Wait };

    // What a creature's AI sees on its turn (see yh::decide). `who` maps the view's units back to creatures_.
    yh::TacticalView tacticalView(size_t me, std::vector<size_t>& who);
    yh::CellCosts distanceToFoes(int team) const;

    size_t heroCount_ = 0; // the chapter's party; creatures_ lists heroes first
    static constexpr int dead = -1;   // token floor for fallen creatures (the controller ignores them)
    static constexpr int hidden = 1;  // token floor for enemies the party can't see

    void newAdventure(uint64_t seed);
    void applyScheme(yh::ControlPreset preset);
    void say(std::string line);
    void syncLog();

    // Exploration and combat.
    void updateVisibility();
    GameMap::LightingMode lightingMode() const;
    GameMap::Time timeOfDay() const;
    int viewTeam() const; // fog view on screen: 0 = the party, 1 + i = hero i alone
    void revealWalls(int team);
    void startCombat(int group);
    void endCombat();
    void beginTurn();
    void endTurn();
    void updateHeroTurn();
    void updateEnemyTurn(double deltaSeconds);
    void attack(size_t target);
    void tryAttack(size_t target);
    bool partyDown() const;
    bool chapterCleared() const;
    void autoExplore();

    // Talking to (or attacking) the chapter's NPCs. They come last in creatures_.
    size_t npcStart_ = 0;
    size_t npcToken(size_t npc) const { return npcStart_ + npc; }
    bool peaceful(size_t npc) const; // standing and not fighting the party
    std::optional<size_t> hoveredNpc() const;
    size_t leaderIndex() const; // the selected hero, else the first one standing
    std::optional<size_t> npcAt(yh::Cell cell) const;
    void walkToTalk(size_t npc);
    void startTalk(size_t npc);
    void chooseReply(size_t index, size_t hero);
    void drawDialogue(yh::Renderer& renderer);

    // Story flags: set by dialogue and won fights; the quest journal and chapter completion read them.
    void setFlags(const std::vector<std::string>& flags);
    void flagsChanged(const std::set<std::string>& before);
    void drawJournal(yh::Renderer& renderer);

    // Rests come from the ruleset (short, long...), each with its own healing and limit.
    void rest(const yh::RestDefinition& rest);
    int restsLeft(const yh::RestDefinition& rest) const;

    // One autosave slot, written after victories and rests (never mid-fight).
    std::string savePath() const;
    void saveAdventure();
    bool loadAdventure();
    // The adventure between fights as JSON: the save file, and what a joining player receives.
    std::string stateJson() const;
    bool restoreState(std::string_view text, std::string* error = nullptr);
    yh::Random nextRandom(uint64_t salt); // fresh dice for the next roll, the same on every machine

    // Co-op (Coop.cpp). Everything that changes the shared game goes through act(): alone it
    // applies at once, hosting it goes through the session's rules, joined it goes to the host.
    // Each player moves their own heroes while exploring and their positions are shared.
    bool inSession() const { return host_ || client_; }
    void act(std::string_view type, const std::string& data = "{}");
    std::optional<std::string> validate(yh::PlayerId player, std::string_view type, std::string_view data, std::string& reason);
    void apply(const yh::NetCommand& command);
    bool mine(size_t creature) const; // this machine plays it
    bool mayAct(yh::PlayerId player, size_t creature) const;
    void selectOwnHero();
    void hostSession();
    void joinSession(const std::string& address);
    void endSession(const std::string& reason);
    void updateSession(double deltaSeconds);
    void assignSeats();
    void shareWalking(double deltaSeconds);
    std::string snapshot() const;
    uint64_t checksum() const;
    std::string seatName(size_t hero) const;
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

    // The end cutscene, then back to the title (the finished adventure's save is removed).
    void playEnding();
    void finishAdventure();

    // Cells the current creature can reach with its movement left, and what each costs.
    // `extra` squares on top (what a dash would add).
    void computeReach(size_t mover, int extra = 0);
    bool occupied(yh::Cell cell, size_t except) const;
    bool walkable(yh::Cell cell) const;
    std::optional<size_t> orderIndex(size_t creature) const;
    std::optional<size_t> currentCreature() const;
    yh::Cell cellOf(size_t creature) const;
    bool adjacent(size_t a, size_t b) const;
    std::optional<size_t> hoveredCreature() const;

    void drawWorld(yh::Renderer& renderer);
    void drawHud(yh::Renderer& renderer);
    void drawParty(yh::Renderer& renderer);
    void drawInitiative(yh::Renderer& renderer);
    void drawCombatBar(yh::Renderer& renderer);
    void drawBars(yh::Renderer& renderer);
    bool overUi(yh::Vec2 screen) const;

    // The adventure being played. Null if it failed to load (the title shows chapterError_).
    std::unique_ptr<Chapter> chapter_;
    std::string chapterError_;
    std::string themePath_;
    std::vector<ContentLibrary::Adventure> adventures_;
    std::vector<ContentLibrary::Package> packages_; // installed .yore files
    yh::Compendium compendium_; // built-in plus installed definitions, for making characters and adventures
    size_t adventure_ = 0; // index into adventures_
    size_t adventurePage_ = 0;
    std::string notice_; // result of the last added file, shown on the title menus
    bool noticeBad_ = false;
    GameMap& map() { return chapter_->map; }
    const GameMap& map() const { return chapter_->map; }
    yh::Grid grid_{yh::GridType::Square, GameMap::cellSize};
    yh::Camera camera_;
    yh::CameraControls controls_;
    yh::ControlScheme scheme_;
    yh::Input input_;
    yh::Input noInput_; // fed to the token controller while the mouse is over the UI
    yh::TokenController tokens_;
    yh::FogOfWar fog_{1, 1, GameMap::cellSize}; // resized to the map in newAdventure()
    yh::Lighting lighting_;
    yh::LightLevels lightLevels_{1, 1, GameMap::cellSize}; // rebuilt in newAdventure()
    yh::Ui ui_;
    yh::FileSystem files_;
    std::unique_ptr<yh::Assets> assets_;
    yh::Font* title_ = nullptr;

    yh::Ruleset rules_ = yh::Ruleset::modern(); // the chapter's, copied at load
    std::vector<Creature> creatures_; // fixed size after newAdventure(): the encounter points into it
    std::unique_ptr<yh::Encounter> encounter_;
    size_t encounterLogShown_ = 0;
    uint64_t seed_ = 0;
    int fights_ = 0;

    std::unordered_map<yh::Cell, float, yh::CellHash> reach_;
    yh::Cell standing_; // where the current creature stood when reach_ was computed
    std::optional<size_t> pendingAttack_; // walk next to this creature, then hit it
    EnemyStep enemyStep_ = EnemyStep::Think;
    double enemyTimer_ = 0;
    std::optional<size_t> enemyTarget_;
    int sideAtStart_[2] = {0, 0};      // how many each side brought to this fight, and whether a leader was among them
    bool hadLeader_[2] = {false, false};
    bool aiNotes_ = false; // F8: each AI decision is explained in the log

    std::vector<std::string> log_;
    std::vector<Floater> floaters_;
    std::vector<yh::Rect> uiRects_; // last frame's panels, so clicks on them don't walk the party
    std::string banner_;
    double bannerTime_ = 0;
    double time_ = 0;
    bool cameraPlaced_ = false;
    std::map<std::string, int> restsUsed_; // by rest id
    std::set<std::string> flags_;
    std::optional<yh::QuestJournal> journal_; // the chapter's, if it has one
    bool journalOpen_ = false;
    std::unique_ptr<yh::DialogueSession> talk_; // the conversation on screen, if any
    size_t talkNpc_ = 0;
    std::optional<size_t> pendingTalk_; // walking over to this NPC
    uint64_t rolls_ = 0; // rests, recoveries and dialogue checks so far; seeds each one's dice

    // Co-op. The host is player 0 and owns the enemies; seats_ says who plays each hero.
    std::unique_ptr<yh::SessionHost> host_;
    std::unique_ptr<yh::SessionClient> client_;
    yh::PlayerId self_ = 0;
    std::vector<int> seats_; // empty = every hero is player 0's
    std::map<int, std::string> playerNames_;
    std::string sessionEnded_; // set by the client's disconnect handler, handled after its update
    bool reseat_ = false; // someone joined or left: deal the heroes out again after the host's update
    std::string netStatus_;
    Online online_;
    std::string onlineStatus_; // the last one printed
    double syncTimer_ = 0;
    std::string lastSync_;
    std::optional<yh::Cell> pendingStep_; // pendingAttack_ swings once the hero stands here
    static constexpr int enemyOwner = 1000;
    static constexpr int npcOwner = 1001;
    yh::Random restRandom_{1};
    yh::Cutscene cutscene_;
    bool cutsceneDone_ = false; // set by the cutscene's "finished" event, handled after its update
    Menu menu_ = Menu::Main;
    Menu settingsBack_ = Menu::Main; // where Settings' Back button goes
    Settings settings_;
    bool settingsDirty_ = false;
    bool onTitle() const { return menu_ != Menu::None && menu_ != Menu::Pause; }
    bool hasSave_ = false; // checked once, for the title's Continue button
    bool testRun_ = false; // YOREHOLD_SEED set: fixed seed, no autosaves
    bool autoPlay_ = false; // F9: heroes use the goblin AI (for testing whole runs)
    int autoExploreStuck_ = 0;
};
