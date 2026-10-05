#pragma once

#include "Chapter.h"

#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace yh
{
class FileSystem;
}

// An adventure as a collection of chapters with transitions between them, adventure-wide flags
// and level range. An adventure.json file lists chapters and how they connect; a chapter folder
// is chapters/<id>/chapter.json.
struct Adventure
{
    struct Transition
    {
        std::string fromChapter;   // chapter id
        std::string exitMarker;    // name of the exit marker on the from chapter's map
        std::string toChapter;     // chapter id
        std::string entryMarker;   // name of the entry marker on the to chapter's map
        std::vector<std::string> when; // optional: adventure flags required for this transition
    };

    std::string id;
    std::string title;
    std::string description; // optional
    int minLevel = 1;
    int maxLevel = 20;
    int recommendedPartySize = 4; // preferred party size at start

    // List of chapter folders (paths like "chapters/my-chapter")
    std::vector<std::string> chapterFolders;
    // The id in each of those folders' chapter.json, in the same order.
    std::vector<std::string> chapterIds;

    // How to move between chapters
    std::vector<Transition> transitions;

    // Story flags that are tracked across the entire adventure, set by dialogue or encounters
    std::vector<std::string> flags; // all possible flags, pre-declared

    // The chapter folder the party makes camp in; empty = the shared chapters/camp.
    std::string camp;

    // Loads adventure.json from the root. The file must list chapter folders; Chapter::load
    // is called for each to verify it exists and is valid, and every transition must name chapters
    // in the list and markers on their maps. All chapters seat the same number of heroes and are
    // written for a level inside the range. Returns an empty optional and sets error if not.
    static std::optional<Adventure> load(const yh::FileSystem& files, const std::string& folder,
        std::string* error = nullptr);
    // Only the chapter folders the file lists, without loading anything: a quick look for whether
    // a chapter belongs to it. Empty if there is no readable adventure.json.
    static std::vector<std::string> listedChapters(const yh::FileSystem& files, const std::string& folder);

    // Returns the chapter that comes after `currentChapter` when exiting at `marker`, or empty
    // if there is no transition. If `when` flags are not met, this returns empty (the transition
    // is not available yet).
    std::optional<std::string> nextChapter(std::string_view currentChapter, std::string_view marker,
        const std::vector<std::string>& setFlags) const;
    // The same, as the whole transition. Null if none is open.
    const Transition* transition(std::string_view currentChapter, std::string_view marker,
        const std::vector<std::string>& setFlags) const;
    // The folder of a chapter id, or empty.
    std::string folderOf(std::string_view chapterId) const;
    bool hasFolder(std::string_view folder) const;
};
