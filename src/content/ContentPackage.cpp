#include "ContentPackage.h"
#include "Chapter.h"

#include <yorehold/framework/animation/Cutscene.h>
#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/rpg/Dialogue.h>
#include <yorehold/framework/ui/Ui.h>

#include <nlohmann/json.hpp>

#include <algorithm>
#include <filesystem>
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
        p.name = j.value("name", "");
        p.chapters = j.value("chapters", std::vector<std::string>{});
        // Packages of definitions only (classes, items, creatures, rulesets) have no chapters.
        p.defaultChapter = p.chapters.empty() ? j.value("defaultChapter", "") : j.at("defaultChapter").get<std::string>();
        p.theme = j.value("theme", "");
        p.dialogues = j.value("dialogues", std::vector<std::string>{});
        p.cutscenes = j.value("cutscenes", std::vector<std::string>{});
        if (p.name.size() > 80) throw std::invalid_argument("name is longer than 80 characters");
        if (!p.chapters.empty() || !p.defaultChapter.empty()) checkPath(p.defaultChapter);
        std::set<std::string> seen;
        for (const auto& path : p.chapters)
        {
            checkPath(path);
            if (!seen.insert(path).second) throw std::invalid_argument("duplicate chapter " + path);
        }
        if ((!p.chapters.empty() || !p.defaultChapter.empty()) && !seen.contains(p.defaultChapter)) throw std::invalid_argument("defaultChapter must be listed in chapters");
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
    if (chapters.empty())
    {
        // Nothing to play: the shared definitions still have to load.
        yh::Compendium compendium;
        if (!compendium.load(files, "", error)) return false;
    }
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

namespace
{

namespace fs = std::filesystem;

// "My Keep (2).yore" -> "my-keep-2": installed files get plain names, whatever they were called.
std::string libraryName(const fs::path& source)
{
    std::string name;
    for (const char c : source.stem().string())
    {
        const char lower = c >= 'A' && c <= 'Z' ? static_cast<char>(c - 'A' + 'a') : c;
        if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9') || lower == '_')
            name += lower;
        else if (!name.empty() && name.back() != '-')
            name += '-';
    }
    while (!name.empty() && name.back() == '-') name.pop_back();
    return name.empty() ? "content" : name.substr(0, 60);
}

}

std::optional<ContentLibrary::Package> ContentLibrary::inspect(const std::string& source, std::string* error)
{
    if (error) error->clear();
    yh::FileSystem files;
    if (!ContentPackage::mount(files, source, "package"))
    {
        if (error) *error = "couldn't open " + source;
        return std::nullopt;
    }
    const auto content = ContentPackage::load(files, error);
    if (!content || !content->validate(files, error)) return std::nullopt;

    Package package;
    package.path = source;
    package.name = content->name.empty() ? fs::path(source).stem().string() : content->name;
    // The default chapter leads, so opening a package starts where its author meant.
    std::vector<std::string> folders{content->chapters};
    std::stable_partition(folders.begin(), folders.end(), [&](const std::string& f) { return f == content->defaultChapter; });
    for (const std::string& folder : folders)
    {
        const auto chapter = Chapter::load(files, folder, error);
        if (!chapter) return std::nullopt;
        package.adventures.push_back({source, package.name, folder, chapter->title.empty() ? chapter->id : chapter->title});
    }
    yh::Compendium compendium;
    if (compendium.load(files, "", nullptr))
    {
        package.items = compendium.items.size();
        package.classes = compendium.classes.size();
        package.creatures = compendium.creatures.size();
    }
    return package;
}

std::optional<ContentLibrary::Package> ContentLibrary::install(const std::string& source, const std::string& folder, std::string* error)
{
    if (!inspect(source, error)) return std::nullopt;
    std::error_code problem;
    if (!fs::is_regular_file(source, problem))
    {
        if (error) *error = "only .yore files can be added (pack the folder first)";
        return std::nullopt;
    }
    fs::create_directories(folder, problem);
    const fs::path target = fs::path(folder) / (libraryName(source) + ".yore");
    if (!fs::equivalent(source, target, problem))
    {
        problem.clear();
        // Copy beside the target first, so a failed copy never leaves half a package installed.
        const fs::path partial = target.string() + ".part";
        fs::copy_file(source, partial, fs::copy_options::overwrite_existing, problem);
        if (!problem) fs::rename(partial, target, problem);
        if (problem)
        {
            if (error) *error = "couldn't copy into " + folder + ": " + problem.message();
            fs::remove(partial, problem);
            return std::nullopt;
        }
    }
    return inspect(target.string(), error);
}

std::vector<ContentLibrary::Package> ContentLibrary::installed(const std::string& folder, std::vector<std::string>* problems)
{
    std::vector<std::string> paths;
    std::error_code problem;
    for (fs::directory_iterator it(folder, problem), end; !problem && it != end; it.increment(problem))
        if (it->is_regular_file(problem) && it->path().extension() == ".yore")
            paths.push_back(it->path().string());
    std::sort(paths.begin(), paths.end());

    std::vector<Package> packages;
    for (const std::string& path : paths)
    {
        std::string error;
        if (auto package = inspect(path, &error))
            packages.push_back(std::move(*package));
        else if (problems)
            problems->push_back(fs::path(path).filename().string() + ": " + error);
    }
    return packages;
}

bool ContentLibrary::remove(const Package& package)
{
    std::error_code problem;
    return fs::remove(package.path, problem) && !problem;
}

yh::Compendium ContentLibrary::compendium(const std::string& builtIn, const std::vector<Package>& packages)
{
    yh::Compendium all;
    auto add = [&all](const std::string& source) {
        yh::FileSystem files;
        if (ContentPackage::mount(files, source, "package"))
            all.load(files, "", nullptr); // all-or-nothing per source: a broken one adds nothing
    };
    add(builtIn);
    for (const Package& package : packages)
        add(package.path);
    return all;
}
