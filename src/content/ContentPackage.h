#pragma once

#include <optional>
#include <string>
#include <vector>

namespace yh
{
class FileSystem;
class Compendium;
struct Ruleset;
}

// A complete, transferable content folder, optionally zipped as .yore. The manifest declares
// playable chapters; classes, items, maps, art and design documents travel in the same folder.
struct ContentPackage
{
    std::string name; // shown in the library; empty = the file's name
    std::string kind; // "adventure", "ruleset", "compendium", "character_class", "race", "feat"; empty = inferred from structure
    std::string id; // unique identifier across versions; empty for built-in or unnamed packages
    int revision = 0; // version number for updates; 0 = not versioned
    std::string ruleset; // for adventures: which ruleset version this requires; empty = game's own ruleset
    std::vector<std::string> needs; // package ids/revisions this depends on
    std::string defaultChapter; // empty when the package has no chapters (classes, items... only)
    std::string theme;
    std::vector<std::string> chapters;
    std::vector<std::string> dialogues;
    std::vector<std::string> cutscenes;

    static bool mount(yh::FileSystem& files, const std::string& source, const std::string& name);
    static std::optional<ContentPackage> load(const yh::FileSystem& files, std::string* error = nullptr);
    bool validate(const yh::FileSystem& files, std::string* error = nullptr) const;
};

// The player's installed .yore files: one folder in the state directory. Opening a .yore with the
// game (double-click, drag onto the window, or a command-line argument) copies it here.
struct ContentLibrary
{
    // One playable chapter, from the built-in content (package empty) or an installed file.
    struct Adventure
    {
        std::string package; // path of the .yore or folder; empty = built in
        std::string packageName;
        std::string folder; // chapter folder inside it
        std::string title;
    };

    // An installed file and what it holds.
    struct Package
    {
        std::string path;
        std::string name;
        std::vector<Adventure> adventures;
        size_t items = 0, classes = 0, creatures = 0;
    };

    // Reads a package by itself (it has to carry everything it uses) and checks all of it.
    static std::optional<Package> inspect(const std::string& source, std::string* error = nullptr);
    // Checks `source`, then copies it into `folder` (replacing an older copy with the same name).
    static std::optional<Package> install(const std::string& source, const std::string& folder, std::string* error = nullptr);
    // Every valid package in `folder`, by file name. Broken files are skipped and listed in `problems`.
    static std::vector<Package> installed(const std::string& folder, std::vector<std::string>* problems = nullptr);
    static bool remove(const Package& package);
    // Everything there is to build with when making characters and adventures: the definitions in
    // `builtIn` plus those of every package, in order (a later id replaces an earlier one).
    // Adventures never read this; they play with what their own file carries.
    static yh::Compendium compendium(const std::string& builtIn, const std::vector<Package>& packages);
    // What making a character needs: the game's own ruleset from `builtIn`, and compendium() with
    // that ruleset's races, backgrounds and feats added. False, with the reason, if they don't load.
    static bool creation(const std::string& builtIn, const std::vector<Package>& packages, yh::Ruleset& rules,
        yh::Compendium& compendium, std::string* error = nullptr);
};
