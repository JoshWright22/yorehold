#pragma once

#include "content/Chapter.h"

#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/map/FogOfWar.h>
#include <yorehold/framework/map/Tokens.h>
#include <yorehold/framework/net/Session.h>
#include <yorehold/framework/rpg/Combat.h>
#include <yorehold/framework/rpg/Dialogue.h>
#include <yorehold/framework/rpg/QuestJournal.h>
#include <yorehold/framework/rpg/Random.h>
#include <yorehold/framework/rpg/Stealth.h>
#include <yorehold/framework/rpg/Tactics.h>

#include <nlohmann/json.hpp>

#include <functional>
#include <map>
#include <memory>
#include <optional>
#include <set>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

// The game itself: everything that is true about an adventure in progress, and the rules that
// change it. It draws nothing, reads no keyboard or mouse and has no clock: time arrives as the
// seconds passed to it, what players want arrives as intents, and what the screen should show or
// play leaves as events.
class World
{
public:
    // Everything on the map that has a sheet. Same order as the tokens; heroes come first.
    struct Creature
    {
        yh::Character sheet;
        int team = 0;  // 0 = party, 1 = enemies, 2 = neutral NPCs (who turn into enemies if attacked)
        int group = -1; // index into the chapter's encounters (enemies that wake up together); NPCs fight alone
        bool awake = false;
        int npc = -1; // index into the chapter's NPCs
        std::string creatureId;            // its definition in the compendium ("" for heroes)
        std::vector<std::string> aiLayers; // the chapter's AI changes for it (JSON; see aiFor)
        bool fleeing = false; // its morale broke this fight: it runs until it gets away or is cornered
        bool fled = false;    // it got away: out of the adventure, and no body is left behind
        std::string breakAs;  // how it reacts now its morale has broken this fight (AiProfile::onBreak); empty = it hasn't
        bool surrendered = false; // gave up: out of the fight, stays where it is and can be talked to
        std::string surrender;    // the dialogue for that (see Chapter::surrender)
        float facing = 0;      // radians: where an enemy looks until it notices the party
        bool sneaking = false; // a hero moving quietly: slower, lights covered, only noticed inside a vision cone
    };

    // What a number or word floating up from a token is about; the screen picks the colour.
    enum class FloatKind { Miss, Hit, Critical, Heal, Unseen };

    // Something for the screen to show. The screen takes them all once it has finished a call into the world.
    struct Event
    {
        enum class Kind
        {
            Reset,   // a new adventure began: drop the old one's log, floating text, cutscene and camera place
            Resumed, // a saved or shared game was put over it: clear the log and the banner again
            Log,     // `text` is a line for the adventure log
            Floater, // `text` floats up from `at`
            Banner,  // `text` across the screen for `seconds`
            Camera,  // look at `at`
            Follow,  // go back to following the party
            Ending,  // `text` is the ending cutscene (JSON) to play; call endCutscene() when it is over
            Save,    // `text` is the adventure between fights, for the autosave
            Talk,    // a conversation began
        };
        Kind kind = Kind::Log;
        std::string text;
        yh::Vec2 at;
        FloatKind floater = FloatKind::Miss;
        double seconds = 0;
    };
    std::vector<Event> takeEvents();

    // `files` is where chapters keep their dialogue, quests and cutscenes; it must outlive the world.
    explicit World(const yh::FileSystem& files);
    virtual ~World();

    static constexpr int dead = -1;   // token floor for fallen creatures (the controller ignores them)
    static constexpr int hidden = 1;  // token floor for enemies the party can't see
    static constexpr int enemyOwner = 1000; // token owners nobody at the table plays
    static constexpr int npcOwner = 1001;

    // Starts the chapter again from its files. Every roll that follows comes from `seed`.
    void newAdventure(uint64_t seed);
    void say(std::string line);

    // One step of time: walking, conversations opening on arrival, the turns of whatever the game
    // plays, and what the party can see (which can start a fight). The play screen runs the same
    // parts with its input in between (walk, arrive, takeTurns).
    void update(double deltaSeconds);

    // Player choices that change what the rules see: lighting (0 = as the map says, else 1 +
    // GameMap::LightingMode), time of day (0 = as the map says, else 1 + GameMap::Time) and whether
    // the fog shows the whole party's view or only the selected hero's.
    struct Options
    {
        int lighting = 0;
        int timeOfDay = 0;
        bool sharedFog = true;
    };
    void setOptions(const Options& options);

    bool partyDown() const;
    bool chapterCleared() const;
    int restsLeft(const yh::RestDefinition& rest) const;

    // The adventure between fights as JSON: the save file, and what a joining player receives.
    std::string stateJson() const;
    bool restoreState(std::string_view text, std::string* error = nullptr);
    std::string snapshot() const; // stateJson plus who sits where
    // Not in a fight, a cutscene or a finished game, and not a guest in someone else's.
    bool canSave() const;
    void requestSave(); // an autosave point: the state goes out as an event

    // Who plays what. The host is player 0 and owns the enemies.
    bool mine(size_t creature) const; // this machine plays it
    bool mayAct(yh::PlayerId player, size_t creature) const;
    void selectOwnHero();
    std::string seatName(size_t hero) const;

    // Talking to (or attacking) the chapter's NPCs, who come last, and enemies who surrendered.
    size_t npcToken(size_t npc) const { return npcStart_ + npc; }
    bool talkable(size_t creature) const; // standing, not fighting the party, and has something to say
    std::optional<size_t> talkerAt(yh::Cell cell) const;

    // Story flags: set by dialogue and won fights; the quest journal and chapter completion read them.
    void setFlags(const std::vector<std::string>& flags);

    bool occupied(yh::Cell cell, size_t except) const;
    bool walkable(yh::Cell cell) const;
    std::optional<size_t> orderIndex(size_t creature) const;
    std::optional<size_t> currentCreature() const;
    yh::Cell cellOf(size_t creature) const;
    bool adjacent(size_t a, size_t b) const;

    // The ending cutscene the world asked for (Event::Ending) has finished or was skipped.
    void endCutscene();

    // Everything that changes the game is an intent: a type and JSON data (WorldNet.cpp lists
    // them). validate() runs on the host, checks it and returns the command data; apply() runs
    // that command on every copy of the game in the same order (dice come from the shared seed).
    // act() is where intents go in. Alone it validates and applies at once as player 0; the game
    // overrides it to send them through a co-op session.
    virtual void act(std::string_view type, const std::string& data = "{}");
    std::optional<std::string> validate(yh::PlayerId player, std::string_view type, std::string_view data, std::string& reason);
    void apply(const yh::NetCommand& command);
    uint64_t checksum() const; // everything a desync between copies would show up in

protected:
    enum class EnemyStep { Think, Walk, Strike, Wait };


    // Exploring (WorldExplore.cpp).
    void walk(double deltaSeconds); // everyone on the move takes their next steps
    void arrive();                  // the leader reached someone they walked over to talk to
    // Whoever's turn it is acts. `heroesListen`: this machine's heroes may act (no menu over them);
    // `heroInput` reads the player's input on their own hero's turn.
    void takeTurns(double deltaSeconds, bool heroesListen, const std::function<void()>& heroInput);
    size_t leaderIndex() const; // the selected hero, else the first one standing
    std::string dialogueFor(size_t creature) const;
    void walkToTalk(size_t creature);
    void startTalk(size_t creature);
    void chooseReply(size_t index, size_t hero);
    void dialogueActions(); // carries out the conversation's "do" actions
    // Rests come from the ruleset (short, long...), each with its own healing and limit.
    void rest(const yh::RestDefinition& rest);
    // A hero walking to a square between fights; the others follow their links.
    bool canGo(size_t hero, yh::Cell to) const;
    void go(size_t hero, yh::Cell to);
    void autoExplore();

    // What the party sees, and sneaking (WorldStealth.cpp).
    GameMap::LightingMode lightingMode() const;
    GameMap::Time timeOfDay() const;
    int viewTeam() const; // fog view on screen: 0 = the party, 1 + i = hero i alone
    void revealWalls(int team);
    void updateVisibility();
    // Enemies that haven't noticed the party watch in a cone; a sneaking hero inside one rolls
    // Stealth against their passive Perception (see yh::StealthTracker).
    std::vector<yh::Watcher> watchers() const; // one per creature after the heroes; range below 0 = not watching
    yh::LightLevel lightAt(yh::Vec2 point) const;
    void updateStealth();
    bool sneakingMine() const; // one of this machine's heroes is sneaking

    // Fights (WorldCombat.cpp).
    // Wakes `group` (or only `only` of it) and starts a fight with the party.
    // `surprise`: the party struck from hiding, so the enemies lose their first turn.
    void startCombat(int group, std::optional<size_t> only = std::nullopt, bool surprise = false);
    void endCombat();
    void beginTurn();
    void endTurn();
    void syncLog(); // the encounter's new lines into the adventure log
    void attack(size_t target);
    // Strikes `target`, walking next to it first if the current creature's movement reaches.
    void tryAttack(size_t target);
    bool swingReady() const; // tryAttack's walk has landed
    void swingIfReady();
    void updateEnemyTurn(double deltaSeconds);
    void turnHostile(size_t creature); // an NPC or a creature that surrendered attacks the party
    // The chapter is cleared: its ending cutscene goes out as an event, or a banner if it has none.
    void playEnding();
    // Cells the current creature can reach with its movement left, and what each costs.
    // `extra` squares on top (what a dash would add).
    void computeReach(size_t mover, int extra = 0);

    // How the creatures the game plays think (WorldAi.cpp).
    // What a creature's AI sees on its turn (see yh::decide). `who` maps the view's units back to creatures_.
    yh::TacticalView tacticalView(size_t me, std::vector<size_t>& who);
    yh::CellCosts distanceToFoes(int team) const;
    yh::CellCosts distanceFrom(const std::vector<yh::Cell>& cells) const; // walking distance to the nearest of them
    std::optional<int> sleepingGroupNear(size_t creature, float squares) const; // allies not yet fighting, within reach
    // How a creature thinks right now: its file, the chapter, the story so far and the server, in that order.
    yh::AiProfile aiFor(size_t creature) const;
    void applyServerAi(const nlohmann::json& config);
    void reloadAi(const nlohmann::json& serverConfig);

    void emit(Event event) { events_.push_back(std::move(event)); }
    void flagsChanged(const std::set<std::string>& before);
    yh::Random nextRandom(uint64_t salt); // fresh dice for the next roll, the same on every machine

    const yh::FileSystem& chapterFiles_;
    std::vector<Event> events_;
    Options options_;

    // The adventure being played. Null if it failed to load.
    std::unique_ptr<Chapter> chapter_;
    GameMap& map() { return chapter_->map; }
    const GameMap& map() const { return chapter_->map; }
    yh::Ruleset rules_ = yh::Ruleset::modern(); // the chapter's, copied at load
    yh::Grid grid_{yh::GridType::Square, GameMap::cellSize};
    // Where everyone stands and walks. The controller also turns a local player's clicks into
    // selections and walks while exploring, which is why the screen hands it the pointer.
    yh::TokenController tokens_;
    yh::FogOfWar fog_{1, 1, GameMap::cellSize}; // resized to the map in newAdventure()
    yh::LightLevels lightLevels_{1, 1, GameMap::cellSize};

    std::vector<Creature> creatures_; // fixed size after newAdventure(): the encounter points into it
    size_t heroCount_ = 0; // the chapter's party; creatures_ lists heroes first
    size_t npcStart_ = 0;
    std::unique_ptr<yh::Encounter> encounter_;
    size_t encounterLogShown_ = 0;
    uint64_t seed_ = 0;
    int fights_ = 0;

    std::unordered_map<yh::Cell, float, yh::CellHash> reach_;
    yh::Cell standing_; // where the current creature stood when reach_ was computed
    std::optional<size_t> pendingAttack_; // walk next to this creature, then hit it
    std::optional<yh::Cell> pendingStep_; // pendingAttack_ swings once the hero stands here
    EnemyStep enemyStep_ = EnemyStep::Think;
    double enemyTimer_ = 0;
    std::optional<size_t> enemyTarget_;
    int sideAtStart_[2] = {0, 0}; // how many each side brought to this fight, and whether a leader was among them
    bool hadLeader_[2] = {false, false};
    bool aiNotes_ = false;  // each AI decision is explained in the log
    bool autoPlay_ = false; // heroes use the AI too (for testing whole runs)
    int autoExploreStuck_ = 0;
    std::map<std::string, yh::AiProfile, std::less<>> serverProfiles_;
    std::map<std::string, std::string> serverCreatureAi_; // creature id -> JSON layer

    std::map<std::string, int> restsUsed_; // by rest id
    std::set<std::string> flags_;
    std::optional<yh::QuestJournal> journal_; // the chapter's, if it has one
    std::unique_ptr<yh::DialogueSession> talk_; // the conversation going on, if any
    size_t talkWith_ = 0; // creature index
    std::optional<size_t> pendingTalk_; // walking over to talk to this creature
    uint64_t rolls_ = 0; // rests, recoveries and dialogue checks so far; seeds each one's dice
    yh::Random restRandom_{1};
    bool inCutscene_ = false; // the ending is playing: nothing else happens until endCutscene()
    bool saves_ = true;       // autosave points send their state out (off for test runs)

    std::vector<yh::StealthTracker> sneak_; // per hero
    std::vector<yh::Vec2> lastAt_;          // per hero: where they stood last frame
    yh::Random stealthRandom_{1};

    // Co-op: seats_ says who plays each hero (empty = every hero is player 0's).
    yh::PlayerId self_ = 0;
    bool remote_ = false; // this copy follows a host, who makes the rolls and plays the enemies
    std::vector<int> seats_;
    std::map<int, std::string> playerNames_;
};
