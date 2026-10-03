#pragma once

#include "GameMap.h"

#include <yorehold/framework/rpg/Compendium.h>
#include <yorehold/framework/rpg/Ruleset.h>

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
    };

    // Creatures that wake up and fight together once any of them is seen.
    struct Encounter
    {
        std::string id;
        std::string text; // shown when the fight starts
        std::vector<Placement> creatures;
        std::vector<std::string> set; // story flags set when the party wins this fight
    };

    // Someone on the map the party can talk to. They don't fight.
    struct Npc
    {
        std::string id;
        std::string name;
        yh::Color color;
        yh::Cell at;
        std::string dialogue; // virtual path to a yh::Dialogue file
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

    std::vector<PartyMember> party;
    std::vector<Encounter> encounters;
    std::vector<Npc> npcs;
    std::string quests; // virtual path to a yh::QuestJournal file; empty = no journal
    // The chapter is complete once all of these story flags are set. Empty = once every encounter is won.
    std::vector<std::string> completeWhen;

    yh::Ruleset rules;
    yh::Compendium compendium;
    GameMap map;

    // Loads the shared compendium (items/, classes/, creatures/ at the root), then the chapter's own
    // additions, the ruleset ("modern"/"classic" or a .json path) and the map, and checks that every
    // class, creature and placement makes sense.
    static std::optional<Chapter> load(const yh::FileSystem& files, std::string_view folder, std::string* error = nullptr);
};
