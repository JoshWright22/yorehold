#include "ContentPackage.h"
#include "Chapter.h"

#include <yorehold/framework/animation/Cutscene.h>
#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/rpg/Dialogue.h>
#include <yorehold/framework/ui/Ui.h>

#include <nlohmann/json.hpp>

#include <algorithm>
#include <set>
#include <stdexcept>

namespace
{

void checkPath(const std::string& path)
{
    if (path.empty() || yh::FileSystem::normalize(path) != path)
        throw std::invalid_argument("expected a relative content path: " + path);
}

}

bool ContentPackage::mount(yh::FileSystem& files, const std::string& source, const std::string& name)
{
    return files.mountFolder(source, name) || files.mountZip(source, name);
}

std::optional<ContentPackage> ContentPackage::load(const yh::FileSystem& files, std::string* error)
{
    if (error) error->clear();
    try
    {
        const auto text = files.readText("content.json");
        if (!text) throw std::invalid_argument("missing manifest");
        const auto j = nlohmann::json::parse(*text);
        if (j.at("format") != "yorehold.content" || j.at("version") != 1)
            throw std::invalid_argument("unsupported format or version");
        ContentPackage p;
        p.defaultChapter = j.at("defaultChapter").get<std::string>();
        p.chapters = j.at("chapters").get<std::vector<std::string>>();
        p.theme = j.value("theme", "");
        p.dialogues = j.value("dialogues", std::vector<std::string>{});
        p.cutscenes = j.value("cutscenes", std::vector<std::string>{});
        checkPath(p.defaultChapter);
        std::set<std::string> seen;
        for (const auto& path : p.chapters)
        {
            checkPath(path);
            if (!seen.insert(path).second) throw std::invalid_argument("duplicate chapter " + path);
        }
        if (!seen.contains(p.defaultChapter)) throw std::invalid_argument("defaultChapter must be listed in chapters");
        if (!p.theme.empty()) checkPath(p.theme);
        for (const auto& path : p.dialogues) checkPath(path);
        for (const auto& path : p.cutscenes) checkPath(path);
        return p;
    }
    catch (const std::exception& e)
    {
        if (error) *error = "content.json: " + std::string(e.what());
        return std::nullopt;
    }
}

bool ContentPackage::validate(const yh::FileSystem& files, std::string* error) const
{
    if (error) error->clear();
    std::set<std::string> ids;
    for (const auto& path : chapters)
    {
        auto chapter = Chapter::load(files, path, error);
        if (!chapter) return false;
        if (!ids.insert(chapter->id).second)
        {
            if (error) *error = path + ": duplicate chapter id " + chapter->id;
            return false;
        }
    }
    auto validateFile = [&](const std::string& path, auto parse) {
        const auto text = files.readText(path);
        std::string problem;
        const bool ok = text && parse(*text, &problem).has_value();
        if (!ok && error) *error = path + ": " + (text ? problem : "missing file");
        return ok;
    };
    if (!theme.empty() && !validateFile(theme, yh::UiTheme::fromJson)) return false;
    for (const auto& path : dialogues)
        if (!validateFile(path, yh::Dialogue::fromJson)) return false;
    for (const auto& path : cutscenes)
        if (!validateFile(path, yh::Cutscene::fromJson)) return false;
    return true;
}
