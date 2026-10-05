#pragma once

#include "content/GameMap.h"

#include <yorehold/framework/editor/History.h>
#include <yorehold/framework/graphics/Renderer.h>
#include <yorehold/framework/graphics/Types.h>
#include <yorehold/framework/input/Input.h>
#include <yorehold/framework/map/Grid.h>
#include <yorehold/framework/rpg/Compendium.h>
#include <yorehold/framework/rpg/Loot.h>
#include <yorehold/framework/ui/Ui.h>

#include <functional>
#include <map>
#include <optional>
#include <set>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

// Encounters mode of the Create screen, in two parts like map mode: EncountersEditor is the
// chapter's groups and the commands that change them (no drawing, so tests and other layouts can
// use it), EncountersPanel is the desktop layout.
//
// It edits the `encounters` and `xpPerVictory` of a chapter.json and writes the rest of the file
// back as it was. Every command goes on the history it was given, which the whole Create screen
// shares.
class EncountersEditor
{
public:
    // What a chapter can name: the creatures, AI profiles and items its package can see.
    struct Catalog
    {
        struct Creature
        {
            std::string name;
            int level = 1;
            yh::Color color{150, 150, 160, 255};
            float size = 0.4f; // token radius in cells
        };
        std::map<std::string, Creature, std::less<>> creatures;
        std::set<std::string, std::less<>> ai;
        std::map<std::string, std::string, std::less<>> items; // id to name
        // What the editor proposes for a fight: this much for each level of each creature in it.
        int xpPerLevel = 25;

        static Catalog from(const yh::Compendium& compendium);
    };

    struct Placement
    {
        std::string creature;
        std::string name;            // empty = the creature's own
        yh::Cell at;
        std::optional<float> facing; // degrees, 0 = east, 90 = south; empty = toward the party's start
        std::string ai;              // JSON: a profile's name or an object of changes; empty = none
        std::string extra;           // fields the editor has no tool for, as a JSON object; empty = none
    };

    // Creatures that wake up and fight together.
    struct Group
    {
        std::string id;
        std::string text;             // the line shown when the fight starts
        std::vector<Placement> creatures;
        std::vector<std::string> set; // story flags set when the party wins
        std::string ai;               // JSON, for everyone in it; empty = none
        std::optional<int> xp;        // empty = the chapter's xpPerVictory
        yh::LootTable loot;           // left with the last of them to fall
        std::string extra;
    };

    // Who else stands on the map: heroes, NPCs and chests. Shown and kept clear of, not edited here.
    struct Fixed
    {
        enum class Kind { Hero, Npc, Chest };
        Kind kind = Kind::Hero;
        std::string name;
        yh::Cell at;
        yh::Color color{200, 200, 210, 255};
    };

    struct Problem
    {
        std::string text;
        bool error = true; // false = worth a look, but the chapter still saves and plays
    };

    using Walkable = std::function<bool(yh::Cell)>;

    explicit EncountersEditor(yh::History& history) : history_(history) {}
    EncountersEditor(const EncountersEditor&) = delete;
    EncountersEditor& operator=(const EncountersEditor&) = delete;

    // `walkable` says where someone can stand on the chapter's map as it is now; without it any
    // cell from 0, 0 will do.
    bool load(std::string_view chapterJson, std::string* error = nullptr, Catalog catalog = {}, Walkable walkable = {});
    bool loaded() const { return loaded_; }
    // The chapter.json to save. A group with nobody in it is left out: the game refuses one.
    std::string toJson() const;

    const std::vector<Group>& groups() const { return state_.groups; }
    const std::vector<Fixed>& fixed() const { return fixed_; }
    const Catalog& catalog() const { return catalog_; }
    int chapterXp() const { return state_.chapterXp; }
    // What winning the fight a group starts gives each hero.
    int xpOf(size_t group) const;
    // From the levels of the creatures in it.
    int proposedXp(size_t group) const;
    // Where a placed creature looks until it notices the party, in degrees.
    float facingOf(const Placement& placement) const;
    // The name of the profile an "ai" entry gives, "" for none and "custom" for an object of changes.
    static std::string profileOf(const std::string& ai);
    // The group and place in it of the creature on a cell.
    std::optional<std::pair<size_t, size_t>> creatureAt(yh::Cell cell) const;
    // Someone could be put there: it can be stood on and nobody is on it.
    bool free(yh::Cell cell) const;

    // Groups. An empty id gets the next free "encounter-N".
    std::optional<size_t> addGroup(std::string id = {});
    // Story changes to its creatures' AI (`aiChanges` naming it) go with it.
    bool removeGroup(size_t group);
    // Ids are unique, 1 to 64 characters. `aiChanges` that name the group follow the new id.
    bool setGroupId(size_t group, const std::string& id);
    bool setGroupText(size_t group, const std::string& text);
    bool setGroupFlags(size_t group, const std::vector<std::string>& flags);
    // A profile from the catalog, or "" for none.
    bool setGroupAi(size_t group, std::string_view profile);
    bool setGroupXp(size_t group, std::optional<int> xp);
    bool setGroupLoot(size_t group, const yh::LootTable& loot);
    bool setChapterXp(int xp);

    // Creatures.
    std::optional<size_t> addCreature(size_t group, std::string_view creature, yh::Cell cell);
    bool moveCreature(size_t group, size_t index, yh::Cell cell);
    bool removeCreature(size_t group, size_t index);
    bool setCreatureName(size_t group, size_t index, const std::string& name);
    // Degrees from -360 to 360, or nothing to look toward the party's start.
    bool setFacing(size_t group, size_t index, std::optional<float> degrees);
    bool setCreatureAi(size_t group, size_t index, std::string_view profile);
    // Its place at the end of the other group.
    std::optional<size_t> moveToGroup(size_t group, size_t index, size_t toGroup);

    // Typing in a box is one undo step until this is called.
    void endTyping() { history_.breakMerge(); }

    std::vector<Problem> problems() const;

private:
    // What an undo step puts back.
    struct State
    {
        std::vector<Group> groups;
        int chapterXp = 0;
        std::string aiChanges; // the chapter's aiChanges as JSON; empty = none
    };
    // Runs `change` and records it with what was there before and after.
    template <typename Change> void edit(std::string_view label, Change change, std::string_view mergeKey = {});
    Placement* placement(size_t group, size_t index);

    yh::History& history_;
    bool loaded_ = false;
    std::string document_; // the chapter.json as read, for everything this mode doesn't edit
    State state_;
    Catalog catalog_;
    Walkable walkable_;
    std::vector<Fixed> fixed_;
};

// The desktop layout over an EncountersEditor: tools and the groups on the left, the chapter's map
// in the middle, the picked group or creature on the right. Left button selects, drags and places,
// right button removes, middle drags the view and the wheel zooms.
class EncountersPanel
{
public:
    enum class Tool { Select, Place };

    void draw(EncountersEditor& editor, GameMap& map, yh::Ui& ui, const yh::Input& input, yh::Renderer& renderer, const yh::Rect& area);
    // A text box of this mode is being typed in, so shortcuts and the Delete key are its own.
    static bool typing(const yh::Ui& ui);

private:
    void drawMap(EncountersEditor& editor, GameMap& map, yh::Ui& ui, yh::Renderer& renderer, const yh::Rect& view);
    void useTool(EncountersEditor& editor, GameMap& map, yh::Ui& ui, const yh::Input& input, const yh::Rect& view);
    void drawTools(EncountersEditor& editor, yh::Ui& ui, const yh::Rect& column);
    void drawGroup(EncountersEditor& editor, yh::Ui& ui, const yh::Rect& column);
    void drawCreature(EncountersEditor& editor, yh::Ui& ui, const yh::Rect& column);
    void drawPalette(EncountersEditor& editor, yh::Ui& ui, const yh::Rect& column);
    yh::Vec2 toWorld(yh::Vec2 screen, const yh::Rect& view) const;

    Tool tool_ = Tool::Select;
    size_t group_ = 0;
    std::optional<size_t> creature_; // selected, in group_
    std::string kind_;               // the creature the Place tool puts down
    std::string lootItem_;           // the item the loot list would add
    float zoom_ = 0;                 // screen pixels per world unit; 0 = fit the map on the first draw
    yh::Vec2 pan_;                   // world position at the view's top-left
    bool grid_ = true;
    std::optional<yh::Cell> hover_;
    bool dragging_ = false;          // the selected creature is being moved
    bool wasTyping_ = false;
    float scroll_ = 0;
    std::string hint_;
    // What the text boxes show while they are typed in.
    std::string idText_, lineText_, flagsText_, xpText_, chapterXpText_, coinsText_, nameText_;
};
