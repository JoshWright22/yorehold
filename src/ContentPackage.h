#pragma once

#include <optional>
#include <string>
#include <vector>

namespace yh
{
class FileSystem;
}

// A complete, transferable content folder, optionally zipped as .yore. The manifest declares
// playable chapters; classes, items, maps, art and design documents travel in the same folder.
struct ContentPackage
{
    std::string defaultChapter;
    std::string theme;
    std::vector<std::string> chapters;
    std::vector<std::string> dialogues;
    std::vector<std::string> cutscenes;

    static bool mount(yh::FileSystem& files, const std::string& source, const std::string& name);
    static std::optional<ContentPackage> load(const yh::FileSystem& files, std::string* error = nullptr);
    bool validate(const yh::FileSystem& files, std::string* error = nullptr) const;
};
