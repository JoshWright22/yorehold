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

    // How to move between chapters
    std::vector<Transition> transitions;

    // Story flags that are tracked across the entire adventure, set by dialogue or encounters
    std::vector<std::string> flags; // all possible flags, pre-declared

    // Loads adventure.json from the root. The file must list chapter folders; Chapter::load
    // is called for each to verify it exists and is valid. Returns an empty optional and sets
    // error if the file is missing, malformed, or a chapter can't load.
    static std::optional<Adventure> load(const yh::FileSystem& files, const std::string& folder,
        std::string* error = nullptr);

    // Returns the chapter that comes after `currentChapter` when exiting at `marker`, or empty
    // if there is no transition. If `when` flags are not met, this returns empty (the transition
    // is not available yet).
    std::optional<std::string> nextChapter(std::string_view currentChapter, std::string_view marker,
        const std::vector<std::string>& setFlags) const;
};
