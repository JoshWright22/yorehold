#pragma once

#include <yorehold/framework/editor/History.h>
#include <yorehold/framework/graphics/Renderer.h>
#include <yorehold/framework/graphics/Types.h>
#include <yorehold/framework/input/Input.h>
#include <yorehold/framework/ui/Ui.h>

#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

// Story mode of the Create screen, in two parts like the other modes: StoryEditor is the
// adventure's story graph and the commands that change it (no drawing, so tests and other layouts
// can use it), StoryPanel is the desktop layout.
//
// The graph is `story.json` at the package root: scene, encounter, dialogue, quest and ending
// nodes with links and the writer's notes. The game doesn't play it; it is where an adventure is
// planned, and it points at the chapters, fights, conversations, quests and endings that do play.
// Suggestions come from those files and from the graph itself, and the writer takes or turns down
// each one. Fields the editor has no tool for are written back as they were. Every command goes on
// the history it was given, which the whole Create screen shares.
class StoryEditor
{
public:
    enum class Kind { Scene, Encounter, Dialogue, Quest, Ending };

    struct Node
    {
        std::string id;
        Kind kind = Kind::Scene;
        std::string title;
        std::string text;   // the writer's notes
        yh::Vec2 at;        // where it sits on the graph
        std::string chapter; // chapter folder in the package; empty = not made yet
        // What it stands for in that chapter: the encounter group, the dialogue file, the quest id
        // or the ending cutscene. Scenes have none: their chapter is what they stand for.
        std::string ref;
        std::optional<int> xp;          // encounter and quest
        std::vector<std::string> steps; // quest
        int mapWidth = 0, mapHeight = 0; // scene with no chapter yet: the map it should get; 0 = none
        std::string extra;               // fields the editor has no tool for, as a JSON object; empty = none
    };

    struct Link
    {
        std::string from, to;
        std::string text;              // what takes the story along it
        std::vector<std::string> when; // flags it needs
        std::string extra;
    };

    // What the package has for the graph to point at, read by the Create screen.
    struct Catalog
    {
        struct Group
        {
            std::string id;
            int xp = 0;       // what Encounters mode proposes: from the levels of who is in it
            int creatures = 0;
        };
        struct Quest
        {
            std::string id, title;
        };
        struct Chapter
        {
            std::string folder, id, title;
            std::vector<Group> groups;
            std::vector<std::string> dialogues; // package paths
            std::vector<Quest> quests;
            std::string ending;                 // the cutscene played when it is cleared; package path
        };
        std::vector<Chapter> chapters;
        bool adventure = false;                                   // there is an adventure.json
        std::vector<std::pair<std::string, std::string>> travel; // chapter ids, from and to

        const Chapter* chapter(std::string_view folder) const;
    };

    struct Suggestion
    {
        std::string key;  // stays the same while it applies, so it can be turned down for good
        std::string text;
    };

    struct Problem
    {
        std::string text;
        bool error = true; // false = worth a look, but the file still saves
    };

    explicit StoryEditor(yh::History& history) : history_(history) {}
    StoryEditor(const StoryEditor&) = delete;
    StoryEditor& operator=(const StoryEditor&) = delete;

    bool load(std::string_view json, std::string* error = nullptr);
    // An empty graph, for a package that has no story.json yet.
    void create();
    bool loaded() const { return loaded_; }
    std::string toJson() const;

    // Not undone: it is what the files say, and it changes as the other modes save.
    void setCatalog(Catalog catalog) { catalog_ = std::move(catalog); }
    const Catalog& catalog() const { return catalog_; }

    const std::vector<Node>& nodes() const { return state_.nodes; }
    const std::vector<Link>& links() const { return state_.links; }
    const std::vector<std::string>& dismissed() const { return state_.dismissed; }
    std::optional<size_t> find(std::string_view node) const;
    std::optional<size_t> findLink(std::string_view from, std::string_view to) const;

    // Ids are a-z, 0-9, - and _, up to 64, unique in the graph. An empty id gets "<kind>-N".
    std::optional<size_t> addNode(Kind kind, yh::Vec2 at, std::string id = {});
    // Its links go with it.
    bool removeNode(size_t node);
    // Links follow the new id.
    bool renameNode(size_t node, const std::string& id);
    // A drag is one undo step until endTyping().
    bool moveNode(size_t node, yh::Vec2 at);
    // Changing the kind clears what it pointed at, since a group isn't a quest.
    bool setKind(size_t node, Kind kind);
    bool setTitle(size_t node, const std::string& title);
    bool setText(size_t node, const std::string& text);
    // A chapter folder the catalog has, or "". A new chapter clears `ref`.
    bool setChapter(size_t node, const std::string& chapter);
    bool setRef(size_t node, const std::string& ref);
    bool setXp(size_t node, std::optional<int> xp);
    bool setSteps(size_t node, const std::vector<std::string>& steps);
    // 0 by 0 for none; otherwise 8 to 200 each way.
    bool setMapSize(size_t node, int width, int height);

    // Not to itself, and only one link each way between two nodes.
    std::optional<size_t> addLink(size_t from, size_t to);
    bool removeLink(size_t link);
    bool setLinkText(size_t link, const std::string& text);
    bool setLinkWhen(size_t link, const std::vector<std::string>& when);

    void endTyping() { history_.breakMerge(); }

    // What the files and the graph suggest, without the ones turned down.
    std::vector<Suggestion> suggestions() const;
    bool accept(std::string_view key);
    // Every suggestion shown now, as one undo step. How many were taken.
    int acceptAll();
    // Turned down for good: saved in the file, so it doesn't come back.
    bool dismiss(std::string_view key);
    // Brings back every one turned down.
    bool restoreDismissed();

    std::vector<Problem> problems() const;

    static std::string_view kindName(Kind kind);
    // The field `ref` is written as: "group", "dialogue", "quest", "cutscene"; empty for a scene.
    static std::string_view refField(Kind kind);
    // The options `ref` has for a node, from the catalog and its chapter.
    std::vector<std::string> refOptions(size_t node) const;

    static constexpr float nodeWidth = 190, nodeHeight = 52;
    // A scene's proposed map: this size, and this much more for each fight linked from it.
    static constexpr std::pair<int, int> mapBase{24, 16}, mapPerFight{8, 4};

private:
    // What an undo step puts back.
    struct State
    {
        std::vector<Node> nodes;
        std::vector<Link> links;
        std::vector<std::string> dismissed;
        std::string extra;
    };
    template <typename Change> void edit(std::string_view label, Change change, std::string_view mergeKey = {});
    std::string freeId(std::string_view base) const;
    // Where a node added for `chapter` goes: under that chapter's scene, or in a new column.
    yh::Vec2 placeFor(const std::string& chapter) const;
    // The node standing for this chapter's scene.
    std::optional<size_t> sceneOf(std::string_view chapter) const;
    // Applies one suggestion to state_, with no history.
    bool apply(std::string_view key);
    // apply() as one undo step; nothing is recorded if it can't be done.
    bool take(const Suggestion& suggestion);
    // The map a scene with no chapter is offered: width and height in cells.
    std::pair<int, int> mapFor(size_t node) const;

    yh::History& history_;
    bool loaded_ = false;
    State state_;
    Catalog catalog_;
};

// The desktop layout over a StoryEditor: the nodes and suggestions on the left, the graph in the
// middle, the picked node or link on the right.
class StoryPanel
{
public:
    void draw(StoryEditor& editor, yh::Ui& ui, const yh::Input& input, yh::Renderer& renderer, const yh::Rect& area);
    // A text box of this mode is being typed in, so shortcuts and the Delete key are its own.
    static bool typing(const yh::Ui& ui);
    void reset() { *this = StoryPanel(); }

private:
    void drawList(StoryEditor& editor, yh::Ui& ui, yh::Renderer& renderer, const yh::Rect& column);
    void drawGraph(StoryEditor& editor, yh::Ui& ui, const yh::Input& input, yh::Renderer& renderer, const yh::Rect& box);
    void drawNode(StoryEditor& editor, yh::Ui& ui, const yh::Rect& column);
    void drawLink(StoryEditor& editor, yh::Ui& ui, const yh::Rect& column);
    void pickNode(std::optional<size_t> node);

    std::optional<size_t> node_;
    std::optional<size_t> link_;
    bool linking_ = false; // the next node clicked gets a link from the picked one
    yh::Vec2 pan_{20, 20};
    bool panning_ = false;
    std::optional<size_t> dragging_;
    yh::Vec2 grab_; // where on the node it was picked up
    float listScroll_ = 0, suggestionScroll_ = 0;
    bool wasTyping_ = false;
    std::string hint_;
    // What the text boxes show while they are typed in.
    std::string idText_, titleText_, notesText_, xpText_, stepsText_, widthText_, heightText_, linkText_, whenText_;
};
