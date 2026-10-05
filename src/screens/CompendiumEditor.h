#pragma once

#include <yorehold/framework/editor/Form.h>
#include <yorehold/framework/editor/History.h>
#include <yorehold/framework/graphics/Renderer.h>
#include <yorehold/framework/graphics/Types.h>
#include <yorehold/framework/input/Input.h>
#include <yorehold/framework/ui/Ui.h>

#include <map>
#include <optional>
#include <set>
#include <string>
#include <string_view>
#include <vector>

// Compendium mode of the Create screen, in two parts like the other modes: CompendiumEditor holds
// a package's definition files (items, creatures, classes, AI profiles, kits and the ruleset's
// spells, races, backgrounds and feats) and the commands that change them, with no drawing.
// CompendiumPanel is the desktop layout.
//
// What each kind has is data: a list of yh::FormSchema read from create/compendium.json, so the
// forms are built from it and a new field is a line in that file. Fields a form doesn't list are
// written back as they were. Every command goes on the history it was given, which the whole
// Create screen shares.
class CompendiumEditor
{
public:
    struct Kind
    {
        yh::FormSchema form;
        std::string reader;   // which of the game's readers checks a file ("item", "spell"...); empty = none
        bool ruleset = false; // lives in a ruleset folder, not at the package root
    };

    struct Entry
    {
        std::string kind;
        std::string id;
        std::string path; // in the package: "items/rope.json"
        yh::FormJson value;
    };

    struct Problem
    {
        std::string text;
        bool error = true; // false = worth a look, but the file still saves
    };

    explicit CompendiumEditor(yh::History& history) : history_(history) {}
    CompendiumEditor(const CompendiumEditor&) = delete;
    CompendiumEditor& operator=(const CompendiumEditor&) = delete;

    // The forms file (create/compendium.json). Clears the entries.
    bool setKinds(std::string_view json, std::string* error = nullptr);
    const std::vector<Kind>& kinds() const { return kinds_; }
    const Kind* kind(std::string_view id) const;
    // Named lists the forms offer (abilities, skills, the game's own ids). The entries here are
    // added to the list named after their kind's folder.
    void setOptions(yh::FormOptions options) { options_ = std::move(options); }
    yh::FormOptions options() const;

    // A file read from the package. False (and why) when it isn't a JSON object; it is then
    // left out, and the package's own checks say more.
    bool addFile(const std::string& kind, const std::string& path, std::string_view text, std::string* error = nullptr);
    const std::vector<Entry>& entries() const { return state_; }
    // Entries of a kind, by id.
    std::vector<size_t> of(std::string_view kind) const;
    std::optional<size_t> find(std::string_view path) const;

    // A new entry in `folder` (the kind's folder in the package, or a ruleset's), from the form's
    // defaults. An empty id gets "new-<kind>" with a number if that is taken. Ids are lowercase
    // letters, digits, - and _, and unique in the folder.
    std::optional<size_t> add(const std::string& kind, const std::string& folder, std::string id = {});
    // A copy of an entry beside it, under a new id.
    std::optional<size_t> copy(size_t entry, std::string id = {});
    // Typed text for a field; false (and why) if it can't be that field's value. Typing in one
    // box is one undo step until endTyping().
    bool setField(size_t entry, std::string_view key, std::string_view text, std::string* error = nullptr);
    bool setValue(size_t entry, std::string_view key, const yh::FormJson& value);
    void endTyping() { history_.breakMerge(); }

    // The file as it is written.
    std::string toJson(size_t entry) const;
    // Errors are what the game would refuse; warnings are names the lists don't know.
    std::vector<Problem> problems(size_t entry) const;
    // What the next save writes: entries whose text differs from the file on disk.
    std::vector<size_t> changed() const;
    void markSaved(size_t entry);

    static bool validId(std::string_view id);

private:
    template <typename Change> void edit(std::string_view label, Change change, std::string_view mergeKey = {});
    std::string freeId(const std::string& folder, const std::string& base) const;

    yh::History& history_;
    std::vector<Kind> kinds_;
    yh::FormOptions options_;
    std::vector<Entry> state_;                   // what an undo step puts back
    std::map<std::string, std::string> saved_;   // path -> text on disk, in the editor's form
};

// The desktop layout over a CompendiumEditor: kinds on the left, that kind's entries next to them,
// the picked entry's form on the right.
class CompendiumPanel
{
public:
    // `folders` says where a new entry of each kind goes; a kind without one can't be added to.
    void draw(CompendiumEditor& editor, const std::map<std::string, std::string>& folders, yh::Ui& ui, const yh::Input& input,
        yh::Renderer& renderer, const yh::Rect& area);
    // A text box of this mode is being typed in, so shortcuts are its own.
    bool typing(const yh::Ui& ui) const;
    void reset() { *this = CompendiumPanel(); }

private:
    void drawEntries(CompendiumEditor& editor, const std::map<std::string, std::string>& folders, yh::Ui& ui, const yh::Rect& column);
    void drawForm(CompendiumEditor& editor, yh::Ui& ui, yh::Renderer& renderer, const yh::Rect& column);

    std::string kind_;
    std::string path_; // the picked entry
    std::string newId_;
    float entryScroll_ = 0, formScroll_ = 0;
    bool wasTyping_ = false;
    std::string hint_;
    std::map<std::string, std::string> typed_; // what the boxes show while they are typed in, by box id
    std::set<std::string> boxes_;              // box ids drawn last frame
};
