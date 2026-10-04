#pragma once

#include "GameMap.h"

#include <yorehold/framework/rpg/Action.h>
#include <yorehold/framework/rpg/Reaction.h>
#include <yorehold/framework/rpg/Compendium.h>
#include <yorehold/framework/rpg/Ruleset.h>
#include <yorehold/framework/rpg/Stealth.h>
#include <yorehold/framework/rpg/PositioningRules.h>

#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace yh
{
class FileSystem;
}

// An adventure as files: chapters/<id>/chapter.json plus its map, cutscenes and any classes,
// items or creatures of its own. Everything the players read (intro, encounter lines, endings) and
// everything placed on the map comes from here; the game code only runs it.
struct Chapter
{
    struct PartyMember
    {
        std::string name;
        std::string classId;
        yh::Color color;
        yh::Cell at;
    };

    struct Placement
    {
        std::string creatureId;
        std::string name; // empty = the creature's own name
        yh::Cell at;
        std::string ai;   // JSON (a profile name or changes) on top of the creature's and the encounter's; empty = none
        std::string surrender; // dialogue when it gives up; empty = the encounter's
        std::optional<float> facing; // where it looks until it notices the party, in degrees (0 = east, 90 = south); empty = toward the party's start
    };

    // The story changing how creatures fight: once every flag in `when` is set, the creatures it
    // matches (any of creature id, encounter id or placed name; empty matches all) get `ai` on
    // top of what they had. Later entries apply after earlier ones.
    struct AiChange
    {
        std::vector<std::string> when;
        std::string creature, encounter, name;
        std::string ai; // JSON, as above
    };

    // Creatures that wake up and fight together once any of them is seen.
    struct Encounter
    {
        std::string id;
        std::string text; // shown when the fight starts
        std::vector<Placement> creatures;
        std::vector<std::string> set; // story flags set when the party wins this fight
        std::string ai; // JSON for everyone in it, on top of each creature's own; empty = none
        std::string surrender; // dialogue for any of them that gives up; empty = the chapter's
    };

    // Someone on the map the party can talk to. They only fight if the party attacks them.
    struct Npc
    {
        std::string id;
        std::string name;
        yh::Color color;
        yh::Cell at;
        std::string dialogue; // virtual path to a yh::Dialogue file
        std::string creature = "commoner"; // their sheet, from the compendium
        std::vector<std::string> attacked; // story flags set when the party attacks them
        std::vector<std::string> killed;   // and when they die
        std::string ai; // JSON on top of their creature's, for when they're attacked; empty = none
    };

    std::string id;
    std::string title;
    std::string folder; // virtual path, "chapters/goblin-keep"
    std::string signature; // content identity used to reject saves from an edited chapter
    std::vector<std::string> intro;
    int xpPerVictory = 0;
    std::string victoryText = "Victory!"; // "{xp}" becomes xpPerVictory
    std::string defeatText = "The party has fallen.";
    std::string resumeText = "Adventure resumed.";
    std::string clearedText = "Chapter complete";
    std::string clearedCutscene; // virtual path, played when every encounter is beaten; empty = none
    std::string wipeCutscene; // played before returning to the checkpoint
    std::vector<yh::Cell> wipeDestination; // one cell per hero; empty = checkpoint positions

    // One seat per hero. Each holds the chapter's own ready-made character until the player brings
    // or makes one; ready-made ones start at `level`, the level the chapter is written for.
    std::vector<PartyMember> party;
    int level = 1;
    std::vector<Encounter> encounters;
    std::vector<Npc> npcs;
    std::vector<AiChange> aiChanges;
    std::string quests; // virtual path to a yh::QuestJournal file; empty = no journal
    // Talking to a creature that surrendered. "do" actions in it: "release" (it leaves), "kill",
    // "fight" (it takes up arms again), "follow"... see dialogue/README.md. Empty = none.
    std::string surrender;
    // The chapter is complete once all of these story flags are set. Empty = once every encounter is won.
    std::vector<std::string> completeWhen;

    // The game's own rules, used by every chapter that doesn't name another set.
    static constexpr const char* defaultRuleset = "rulesets/yorehold";

    yh::Ruleset rules;
    std::string rulesFolder;  // virtual path of the ruleset's folder; empty for a built-in set or a single file
    yh::StealthRules stealth; // the ruleset folder's stealth.json, if it has one; checkEvery is in metres
    // What creatures can do on their turns, in the order the action bar lists them: the ruleset
    // folder's actions/, over the framework's basic three.
    std::vector<yh::ActionDefinition> actions;
    std::vector<yh::ReactionDefinition> reactions;
    yh::PositioningRules positioning;
    yh::Compendium compendium;
    GameMap map;

    // The stealth rules with distances in the map's world units, as yh::StealthTracker wants them.
    yh::StealthRules stealthOnMap() const;
    // Where a placed creature looks until it notices the party, in radians (0 = east, clockwise on screen).
    float facingOf(const Placement& placement) const;

    // Loads the shared compendium (items/, classes/, creatures/ at the root), then the chapter's own
    // additions, the ruleset ("modern"/"classic", a .json path or a folder) and the map, and checks that every
    // class, creature and placement makes sense.
    static std::optional<Chapter> load(const yh::FileSystem& files, std::string_view folder, std::string* error = nullptr);
};
