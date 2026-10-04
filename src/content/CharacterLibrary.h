#pragma once

#include <yorehold/framework/rpg/CharacterChoices.h>

#include <optional>
#include <string>
#include <vector>

namespace yh
{
class SaveFormat;
}

// The player's characters: one file each in `characters/` beside the save. They belong to the
// player, not to an adventure. Starting one copies a character into the adventure's save; at the
// end of each chapter and on leaving, the copy is written back here (choices, XP, what they carry).
// `characters/graveyard/` keeps characters that can no longer be played (dead, or built on a retired
// ruleset): they stay viewable, and nothing writes to them again.
struct CharacterLibrary
{
    static constexpr const char* graveyardFolder = "graveyard";

    struct Entry
    {
        std::string path; // the file; empty until written
        yh::CharacterChoices choices;
        std::vector<yh::Item> inventory; // carried from one adventure to the next
        int coins = 0;
        // The adventure save the character is playing in. A character takes part in one unfinished
        // adventure at a time and shows as "away" until it ends.
        std::string away;
        bool retired = false; // read from the graveyard

        std::string fileName() const;
    };

    static const yh::SaveFormat& format();

    static std::optional<Entry> read(const std::string& path, std::string* error = nullptr);
    // Every character in `folder`, by name, then the graveyard's. Broken files are skipped and
    // listed in `problems`. A missing folder is just empty.
    static std::vector<Entry> list(const std::string& folder, std::vector<std::string>* problems = nullptr);
    // Writes `entry` to its file, or for a new one to a plain file name made from its name ("Ser
    // Ada" -> ser-ada.json, then ser-ada-2.json...), and sets `entry.path`. Graveyard files are
    // never written.
    static bool write(const std::string& folder, Entry& entry, std::string* error = nullptr);
    // Moves the file into the graveyard (a later character with the same name gets a new file).
    static bool retire(const std::string& folder, Entry& entry, std::string* error = nullptr);
    // The library file `fileName` (as the adventure save names it), if it is in `folder`.
    static std::optional<Entry> find(const std::string& folder, const std::string& fileName, std::string* error = nullptr);
};
