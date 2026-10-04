#pragma once

#include <yorehold/framework/map/Positioning.h>

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
        yh::CharacterChoices choices;      // heroes: what the sheet is rebuilt from on every load
        std::string library;               // heroes: their file in the character library ("" = made for this adventure)
        std::vector<std::string> aiLayers; // the chapter's AI changes for it (JSON; see aiFor)
        bool fleeing = false; // its morale broke this fight: it runs until it gets away or is cornered
        bool fled = false;    // it got away: out of the adventure, and no body is left behind
        std::string breakAs;  // how it reacts now its morale has broken this fight (AiProfile::onBreak); empty = it hasn't
        bool surrendered = false; // gave up: out of the fight, stays where it is and can be talked to
        bool dropped = false;     // dead, and what it had now lies in a pile
        std::string surrender;    // the dialogue for that (see Chapter::surrender)
        std::string readiedAction; // waiting for a reaction, until the next turn or the fight ends
        yh::Concentration concentration; // the spell it is holding in place, if any
        bool mayPrepare = true;          // a prepared caster may choose its spells: after a rest that allows it, until a fight
        float facing = 0;      // radians: where an enemy looks until it notices the party
        // A hero moving quietly (the Hidden condition): slower, lights covered, only noticed inside a vision cone.
        bool sneaking() const { return sheet.hasCondition(hiddenCondition); }
    };

    // The ruleset's conditions the game itself puts on creatures (rulesets/yorehold/conditions/).
    // A ruleset without one of them still plays: the state is tracked, with nothing attached to it.
    static constexpr const char* hiddenCondition = "hidden"; // sneaking
    static constexpr const char* downedCondition = "downed"; // a hero at 0 HP
    static constexpr const char* deadCondition = "dead";     // anyone else at 0 HP

    // The ruleset's actions the game itself reaches for (rulesets/yorehold/actions/): clicking an
    // enemy strikes, Space ends the turn, and the creatures the game plays strike and stride.
    // Every ruleset has them, from its own files or the framework's basic three.
    static constexpr const char* strikeAction = "strike";
    static constexpr const char* strideAction = "stride";
    static constexpr const char* endTurnAction = "end-turn";
    // Changing what is worn or held in a fight costs what this action costs (1 without it).
    static constexpr const char* interactAction = "interact";

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

    // A character the player brought for one of the chapter's seats, in place of its ready-made
    // hero. newAdventure uses them until they are set again; seats without one keep the chapter's.
    struct PartyPick
    {
        yh::CharacterChoices choices;
        std::vector<yh::Item> inventory;
        std::string library; // its file in the character library
        int coins = 0;
    };
    void setParty(std::vector<std::optional<PartyPick>> picks) { partyPicks_ = std::move(picks); }
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
        bool reactionPrompts = false;
    };
    void setOptions(const Options& options);

    bool partyDown() const;
    bool wiping() const { return pendingWipe_; }
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
    bool canChooseTurn(size_t creature) const; // an unfinished member of the active block, while no action is moving
    yh::Cell cellOf(size_t creature) const;
    bool adjacent(size_t a, size_t b) const;
    bool isFlanked(size_t creature) const;
    yh::Cover coverFrom(size_t from, size_t target) const;
    int positionalArmorClass(size_t target) const; // flanking, without an attacker's cover
    int attackArmorClass(size_t from, size_t target, bool ranged) const;

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

    // Loads the chapter in `folder` of the files (not started yet: see newAdventure). False, with
    // the reason, if it can't be loaded.
    bool loadChapter(const std::string& folder, std::string* error = nullptr);

    // What the screens draw from. They change the world through act(); the exceptions are this
    // machine's own walking and selection, which the token controller turns from clicks into
    // paths (shared with the others as "walk"), and the map, fog and tokens caching what they draw.
    const Chapter* chapter() const { return chapter_.get(); }
    GameMap& map() { return chapter_->map; }
    const GameMap& map() const { return chapter_->map; }
    const yh::Ruleset& rules() const { return rules_; }
    const yh::Grid& grid() const { return grid_; }
    yh::TokenController& tokens() { return tokens_; }
    const yh::TokenController& tokens() const { return tokens_; }
    yh::FogOfWar& fog() { return fog_; }
    const yh::FogOfWar& fog() const { return fog_; }
    const std::vector<Creature>& creatures() const { return creatures_; }
    size_t heroCount() const { return heroCount_; }

    // Things lying on the map for the party to take (WorldLoot.cpp): the chapter's containers, and
    // what the enemies of a won fight left where they fell. A hero standing on or beside one can
    // take from it ("loot"); between fights heroes can hand items and coins to each other ("give").
    struct Pile
    {
        std::string name;
        yh::Cell at;
        int coins = 0;
        std::vector<yh::Item> items;
        int container = -1; // index into the chapter's containers; -1 = left by a creature
        bool empty() const { return coins == 0 && items.empty(); }
    };
    const std::vector<Pile>& piles() const { return piles_; }
    // The nearest pile with something in it that `hero` can reach from where they stand.
    std::optional<size_t> pileNear(size_t hero) const;
    // Coins as the game counts them: 1234 -> "12 gp 3 sp 4 cp".
    static std::string coinText(int copper);
    const yh::Merchant* merchant(size_t npc) const;
    bool canTrade(size_t hero, size_t npc) const;
    std::optional<size_t> merchantNear(size_t hero) const;
    bool canConsume(size_t hero, size_t item, size_t target, std::string* why = nullptr) const;
    const yh::Encounter* encounter() const { return encounter_.get(); }
    bool fighting() const { return encounter_ && !encounter_->finished(); }
    const yh::DialogueSession* talk() const { return talk_.get(); }
    size_t talkWith() const { return talkWith_; }
    const yh::QuestJournal* journal() const { return journal_ ? &*journal_ : nullptr; }
    const std::set<std::string>& flags() const { return flags_; }
    // Where the current hero can still move this turn, and the square they stood on to work it out.
    const std::unordered_map<yh::Cell, float, yh::CellHash>& reach() const { return reach_; }
    yh::Cell standing() const { return standing_; }
    bool autoPlay() const { return autoPlay_; }

    // Exploring (WorldExplore.cpp).
    void walk(double deltaSeconds); // everyone on the move takes their next steps
    void arrive();                  // the leader reached someone they walked over to talk to
    // Whoever's turn it is acts. `heroesListen`: this machine's heroes may act (no menu over them);
    // `heroInput` reads the player's input on their own hero's turn.
    void takeTurns(double deltaSeconds, bool heroesListen, const std::function<void()>& heroInput);
    size_t leaderIndex() const; // the selected hero, else the first one standing
    // Walks the leader next to `creature`; the conversation opens on arrival.
    void walkToTalk(size_t creature);
    // Uses an action on `target` (a Strike unless another is named), walking into range first if
    // the current creature's movement reaches.
    void tryAttack(size_t target, std::string_view with = strikeAction);

    // Actions (WorldActions.cpp): what the ruleset's files let a creature do on its turn.
    const yh::ActionDefinition* findAction(std::string_view id) const;
    // The ones this creature has, in the order the action bar shows them.
    std::vector<const yh::ActionDefinition*> actionsOf(size_t creature) const;
    int actionCost(size_t creature, const yh::ActionDefinition& action) const;
    // Actions it costs a creature to change what it wears or holds during a fight ("equip").
    int equipCost(size_t creature) const;
    // It is this creature's turn, and it has the action, the actions left and whatever else it asks for.
    bool canUse(size_t creature, const yh::ActionDefinition& action, std::string* why = nullptr) const;
    bool canUse(size_t creature, std::string_view action) const;
    bool inRange(size_t creature, const yh::ActionDefinition& action, size_t target) const;
    // `target` is someone the action may be aimed at from where `creature` stands.
    bool validTarget(size_t creature, const yh::ActionDefinition& action, size_t target) const;
    // Sends the "use" intent for the creature whose turn it is. `at` aims an action with a point target.
    void use(std::string_view action, std::optional<size_t> target = std::nullopt, std::optional<yh::Cell> at = std::nullopt);

    // Spells (WorldSpells.cpp): the ruleset's spells/ files. A creature has the ones on its sheet,
    // listed with its actions; in a fight they are used like any action. Between fights "cast"
    // uses the ones that help, on the party.
    const yh::SpellDefinition* findSpell(std::string_view id) const;
    const yh::SpellRules& spellRules() const { return chapter_->spellcasting; }
    // Where an action's area lies when aimed at `aim` from where `creature` stands.
    yh::AreaTemplate areaOf(size_t creature, const yh::ActionDefinition& action, yh::Cell aim) const;
    // Who an action with an area lands on when aimed there: those of its side, inside it, with a
    // clear line from where it starts.
    std::vector<size_t> creaturesIn(size_t creature, const yh::ActionDefinition& action, yh::Cell aim) const;
    // `at` is a square an action with a point target may be aimed at from where `creature` stands.
    bool validAim(size_t creature, const yh::ActionDefinition& action, yh::Cell at, std::string* why = nullptr) const;
    // Between fights: `hero` may cast `spell` on `target` (itself for a self target).
    bool canCast(size_t hero, std::string_view spell, size_t target, std::string* why = nullptr) const;
    // Between fights, after a rest the spellcasting file names: `hero` may make `spells` its
    // prepared ones ("prepare" intent).
    bool canPrepare(size_t hero, const std::vector<std::string>& spells, std::string* why = nullptr) const;

    struct ReactionPrompt
    {
        size_t creature = 0;
        size_t target = 0;
        std::string name;
        double secondsLeft = 0;
        uint64_t id = 0;
    };
    const std::optional<ReactionPrompt>& reactionPrompt() const { return reactionPrompt_; }
    void react(bool take);

    // What the party sees, and sneaking (WorldStealth.cpp).
    GameMap::LightingMode lightingMode() const;
    GameMap::Time timeOfDay() const;
    int viewTeam() const; // fog view on screen: 0 = the party, 1 + i = hero i alone
    // Enemies that haven't noticed the party watch in a cone; a sneaking hero inside one rolls
    // Stealth against their passive Perception (see yh::StealthTracker).
    std::vector<yh::Watcher> watchers() const; // one per creature after the heroes; range below 0 = not watching
    bool sneakingMine() const; // one of this machine's heroes is sneaking

protected:
    enum class EnemyStep { Think, Walk, Strike, Wait };

    std::string dialogueFor(size_t creature) const;
    void startTalk(size_t creature);
    void chooseReply(size_t index, size_t hero);
    void dialogueActions(); // carries out the conversation's "do" actions
    // Rests come from the ruleset (short, long...), each with its own healing and limit.
    void rest(const yh::RestDefinition& rest);
    // A hero walking to a square between fights; the others follow their links.
    bool canGo(size_t hero, yh::Cell to) const;
    void go(size_t hero, yh::Cell to);
    void autoExplore();

    void revealWalls(int team);
    void updateVisibility();
    yh::LightLevel lightAt(yh::Vec2 point) const;
    void updateStealth();

    // Fights (WorldCombat.cpp).
    // Wakes `group` (or only `only` of it) and starts a fight with the party.
    // `surprise`: the party struck from hiding, so the enemies lose their first turn.
    void startCombat(int group, std::optional<size_t> only = std::nullopt, bool surprise = false);
    void endCombat();
    void beginTurn();
    void endTurn();
    void syncLog(); // the encounter's new lines into the adventure log
    // The current creature does `action`: pays for it, runs its effects and shows what happened.
    void perform(const yh::ActionDefinition& action, std::optional<size_t> target, std::optional<yh::Cell> at = std::nullopt, int slot = 0);
    void consume(size_t hero, size_t item, size_t target);
    // Runs an action's effect on its target, or on everyone in its area. `slot` is the spell slot
    // it was cast from, for steps that scale by it.
    yh::EffectResult runActionEffect(size_t creature, const yh::ActionDefinition& action, std::optional<size_t> target,
        std::optional<yh::Cell> at = std::nullopt, int slot = 0);
    // A casting: spends the slot, ends what the caster was concentrating on if this spell needs
    // concentration, runs the effect and starts concentrating on what it left.
    void castSpell(size_t caster, const yh::SpellDefinition& spell, std::optional<size_t> target, std::optional<yh::Cell> at, int slot);
    void endConcentration(size_t creature, std::string_view why);
    // Damage makes a concentrating creature check, as the ruleset's spellcasting says.
    void concentrationChecks(const yh::EffectResult& result, yh::Random& random);
    // Concentration with nothing left to hold, or held by someone who fell, is over.
    void tidyConcentration();
    void endAllConcentration();
    void startMovement(size_t creature, std::vector<yh::Cell> path, bool prompts);
    void continueMovement();
    void resolveReaction(bool take);
    void reactionTime(double seconds);
    void narrate(const yh::EffectResult& result); // an effect's events as log lines and floating numbers
    class EffectsHost;                            // what effects ask of the world (WorldActions.cpp)
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

    // Conditions the game keeps in step with what happened (WorldStealth.cpp, WorldCombat.cpp).
    void setSneaking(size_t hero, bool on);
    void raise(std::string_view event); // tells everyone's conditions: "fightStart", "fightEnd", "rest"
    void fallenConditions();            // Downed or Dead for whoever is at 0 HP, and off again once they are up
    // A hero whose XP has passed its level: the new levels go into its latest class (until the
    // level-up screen lets the player choose) and the sheet is rebuilt, gaining the extra HP.
    void gainLevels(size_t hero);
    void returnFromWipe(const std::string& checkpoint);

    void emit(Event event) { events_.push_back(std::move(event)); }
    void flagsChanged(const std::set<std::string>& before);
    yh::Random nextRandom(uint64_t salt); // fresh dice for the next roll, the same on every machine

    const yh::FileSystem& chapterFiles_;
    std::vector<Event> events_;
    Options options_;

    // The adventure being played. Null if it failed to load.
    std::unique_ptr<Chapter> chapter_;
    yh::Ruleset rules_ = yh::Ruleset::modern(); // the chapter's, copied at load
    yh::Grid grid_{yh::GridType::Square, GameMap::cellSize};
    // Where everyone stands and walks. The controller also turns a local player's clicks into
    // selections and walks while exploring, which is why the screen hands it the pointer.
    yh::TokenController tokens_;
    yh::FogOfWar fog_{1, 1, GameMap::cellSize}; // resized to the map in newAdventure()
    yh::LightLevels lightLevels_{1, 1, GameMap::cellSize};

    std::vector<Creature> creatures_; // fixed size after newAdventure(): the encounter points into it
    size_t heroCount_ = 0; // the chapter's party; creatures_ lists heroes first
    std::vector<std::optional<PartyPick>> partyPicks_;
    std::vector<Pile> piles_;
    std::vector<std::optional<yh::Merchant>> merchants_; // one entry per chapter NPC
    nlohmann::json merchantsJson() const;
    std::vector<std::optional<yh::Merchant>> merchantsFrom(const nlohmann::json& saved) const;
    void fillContainers(); // newAdventure: each of the chapter's containers becomes a pile
    void dropLoot();       // after a win: the dead enemies' gear and loot, where they fell
    nlohmann::json pilesJson() const;
    std::vector<Pile> pilesFrom(const nlohmann::json& saved) const; // throws for loot that doesn't fit the chapter
    static void addTo(yh::Character& sheet, yh::Item item); // into an inventory, stacking with the same unworn item
    std::string magicLimitText(size_t hero) const; // why this hero can't take another magic item
    size_t npcStart_ = 0;
    std::unique_ptr<yh::Encounter> encounter_;
    size_t encounterLogShown_ = 0;
    uint64_t turnBlockShown_ = 0;
    uint64_t seed_ = 0;
    int fights_ = 0;

    std::unordered_map<yh::Cell, float, yh::CellHash> reach_;
    yh::Cell standing_; // where the current creature stood when reach_ was computed
    std::optional<size_t> pendingAttack_; // walk next to this creature, then hit it
    std::string pendingAction_;           // with this action
    std::optional<yh::Cell> pendingStep_; // pendingAttack_ swings once the hero stands here
    struct PendingMovement
    {
        size_t creature = 0;
        std::vector<yh::Cell> path;
        size_t edge = 1;
        size_t nextCreature = 0;
        int phase = 0; // leaving reach before entering another creature's reach
        size_t animateFrom = 0;
        bool prompts = false;
    };
    struct PendingReaction
    {
        size_t creature = 0;
        size_t target = 0;
        std::string action;
        std::string name;
        bool readied = false;
    };
    std::optional<PendingMovement> pendingMovement_;
    std::optional<PendingReaction> pendingReaction_;
    std::optional<ReactionPrompt> reactionPrompt_;
    uint64_t reactionSequence_ = 0;
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
    bool pendingWipe_ = false;
    bool wipeRequested_ = false;
    std::string checkpoint_;
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
