#pragma once

#include <yorehold/framework/editor/History.h>
#include <yorehold/framework/graphics/Renderer.h>
#include <yorehold/framework/graphics/Types.h>
#include <yorehold/framework/input/Input.h>
#include <yorehold/framework/rpg/Dialogue.h>
#include <yorehold/framework/ui/Ui.h>

#include <functional>
#include <optional>
#include <set>
#include <string>
#include <string_view>
#include <vector>

// Dialogue mode of the Create screen, in two parts like the other modes: DialogueEditor is one
// conversation file and the commands that change it (no drawing, so tests and other layouts can
// use it), DialoguePanel is the desktop layout.
//
// It reads and writes the framework's dialogue format (DIALOGUE.md). Fields it has no tool for
// are written back as they were. Every command goes on the history it was given, which the whole
// Create screen shares.
class DialogueEditor
{
public:
    // What a conversation can name, from the chapter and package it is in.
    struct Catalog
    {
        std::vector<std::string> skills;               // what a check can roll: skills, then abilities
        std::set<std::string, std::less<>> companions; // ids of the chapter's NPCs who can join
        bool companion = false;                        // the NPC who uses this file can join the party
    };

    struct Choice
    {
        std::string id;
        std::string text;
        std::string next; // node id; empty ends the conversation. Unused with a check
        std::vector<std::string> require;
        std::vector<std::string> forbid;
        yh::DialogueFlags flags;
        std::optional<yh::DialogueCheck> check;
        std::string extra; // fields the editor has no tool for, as a JSON object; empty = none
    };

    struct Node
    {
        std::string id;
        std::string speaker;
        std::string text;
        yh::DialogueFlags flags; // on entering it
        std::vector<Choice> choices;
        std::string extra;
    };

    struct Problem
    {
        std::string text;
        bool error = true; // false = worth a look, but the file still saves and plays
    };

    explicit DialogueEditor(yh::History& history) : history_(history) {}
    DialogueEditor(const DialogueEditor&) = delete;
    DialogueEditor& operator=(const DialogueEditor&) = delete;

    // Only a file the game would load: what it refuses is refused here too, with its reason.
    bool load(std::string_view json, std::string* error = nullptr, Catalog catalog = {});
    // A new conversation with one empty node, "start".
    void create(const std::string& id, Catalog catalog = {});
    bool loaded() const { return loaded_; }
    std::string toJson() const;
    // As the game reads it; nullopt (and why) while something in it would be refused.
    std::optional<yh::Dialogue> dialogue(std::string* error = nullptr) const;

    const std::string& id() const { return state_.id; }
    const std::string& start() const { return state_.start; }
    const std::vector<Node>& nodes() const { return state_.nodes; }
    const Catalog& catalog() const { return catalog_; }
    std::optional<size_t> find(std::string_view node) const;
    // How many choices lead to a node, from anywhere.
    int linksTo(std::string_view node) const;

    // Ids are 1 to 64 characters with no spaces; node ids are unique in the file, choice ids in their node.
    bool setId(const std::string& id);
    bool setStart(size_t node);

    // Nodes. An empty id gets the next free "node-N".
    std::optional<size_t> addNode(std::string id = {});
    // Choices that led to it end the conversation instead. The last node can't go.
    bool removeNode(size_t node);
    // Choices, checks and the start that named it follow the new id.
    bool renameNode(size_t node, const std::string& id);
    bool setSpeaker(size_t node, const std::string& speaker);
    bool setText(size_t node, const std::string& text);
    bool setNodeFlags(size_t node, const yh::DialogueFlags& flags);

    // Choices (replies). An empty text gets "..." so the file stays valid.
    std::optional<size_t> addChoice(size_t node, std::string text = {});
    bool removeChoice(size_t node, size_t choice);
    // One place up (-1) or down (1).
    bool moveChoice(size_t node, size_t choice, int by);
    bool setChoiceId(size_t node, size_t choice, const std::string& id);
    bool setChoiceText(size_t node, size_t choice, const std::string& text);
    // A node id, or "" to end the conversation. Not for a choice with a check.
    bool setNext(size_t node, size_t choice, const std::string& next);
    // A new node that the choice leads to (or the check's success), as one undo step.
    std::optional<size_t> branch(size_t node, size_t choice);
    bool setConditions(size_t node, size_t choice, const std::vector<std::string>& require, const std::vector<std::string>& forbid);
    bool setChoiceFlags(size_t node, size_t choice, const yh::DialogueFlags& flags);
    // Adding a check moves `next` to its success; taking it off moves the success back.
    bool setCheck(size_t node, size_t choice, const std::optional<yh::DialogueCheck>& check);

    // Typing in a box is one undo step until this is called.
    void endTyping() { history_.breakMerge(); }

    std::vector<Problem> problems() const;

    // The "do" actions Yorehold carries out (World::dialogueActions), and what is wrong with one:
    // "" when nothing is, else why. `error` says if the game would do nothing with it.
    static std::vector<std::string_view> actions();
    static std::string actionProblem(std::string_view action, const Catalog& catalog, bool* error = nullptr);

private:
    // What an undo step puts back.
    struct State
    {
        std::string id;
        std::string start;
        std::vector<Node> nodes;
        std::string extra;
    };
    template <typename Change> void edit(std::string_view label, Change change, std::string_view mergeKey = {});
    Choice* choice(size_t node, size_t choice);
    std::string freeNodeId(std::string_view base) const;

    yh::History& history_;
    bool loaded_ = false;
    State state_;
    Catalog catalog_;
};

// The desktop layout over a DialogueEditor: the nodes on the left, the picked node and its replies
// in the middle, the picked reply (or the node's own flags and actions) on the right.
class DialoguePanel
{
public:
    void draw(DialogueEditor& editor, yh::Ui& ui, const yh::Input& input, yh::Renderer& renderer, const yh::Rect& area);
    // A text box of this mode is being typed in, so shortcuts and the Delete key are its own.
    static bool typing(const yh::Ui& ui);
    // Forget the picked node and reply, for another file.
    void reset() { *this = DialoguePanel(); }

private:
    void drawNodes(DialogueEditor& editor, yh::Ui& ui, const yh::Rect& column);
    void drawNode(DialogueEditor& editor, yh::Ui& ui, yh::Renderer& renderer, const yh::Rect& column);
    void drawChoice(DialogueEditor& editor, yh::Ui& ui, const yh::Rect& column);
    void drawNodeFlags(DialogueEditor& editor, yh::Ui& ui, const yh::Rect& column);
    // Set, clear and do boxes with the companion buttons under them; true if `flags` was changed.
    bool flagFields(yh::Ui& ui, const DialogueEditor::Catalog& catalog, float x, float& y, float w, const yh::DialogueFlags& flags, yh::DialogueFlags& changed);

    size_t node_ = 0;
    std::optional<size_t> choice_;
    float nodeScroll_ = 0;
    bool wasTyping_ = false;
    std::string hint_;
    // What the text boxes show while they are typed in.
    std::string idText_, speakerText_, lineText_, setText_, clearText_, doText_;
    std::string choiceIdText_, choiceText_, requireText_, forbidText_, difficultyText_;
};
