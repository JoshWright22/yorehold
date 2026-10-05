#include "Adventure.h"

#include <yorehold/framework/assets/FileSystem.h>
#include <nlohmann/json.hpp>

#include <algorithm>

using json = nlohmann::json;

std::optional<Adventure> Adventure::load(const yh::FileSystem& files, const std::string& folder,
    std::string* error)
{
    auto json_str = files.readText(folder.empty() ? "adventure.json" : folder + "/adventure.json");
    if (!json_str)
    {
        if (error)
            *error = (folder.empty() ? "" : folder + "/") + "adventure.json: file not found";
        return {};
    }

    try
    {
        auto doc = json::parse(*json_str);

        Adventure adventure;
        adventure.id = doc.value("id", "");
        adventure.title = doc.value("title", "");
        adventure.description = doc.value("description", "");
        adventure.minLevel = doc.value("minLevel", 1);
        adventure.maxLevel = doc.value("maxLevel", 20);
        adventure.recommendedPartySize = doc.value("recommendedPartySize", 4);

        // Load chapter folders
        if (doc.contains("chapters"))
        {
            for (const auto& ch : doc["chapters"])
            {
                if (ch.is_string())
                {
                    adventure.chapterFolders.push_back(ch.get<std::string>());
                }
                else if (ch.is_object() && ch.contains("folder"))
                {
                    adventure.chapterFolders.push_back(ch["folder"].get<std::string>());
                }
            }
        }

        // Load transitions
        if (doc.contains("transitions"))
        {
            for (const auto& trans : doc["transitions"])
            {
                Transition t;
                t.fromChapter = trans.value("from", "");
                t.toChapter = trans.value("to", "");

                // Support nested object format
                if (trans.contains("from") && trans["from"].is_object())
                {
                    t.fromChapter = trans["from"].value("chapter", "");
                    t.exitMarker = trans["from"].value("marker", "");
                }
                else
                {
                    t.exitMarker = trans.value("exitMarker", "");
                }

                if (trans.contains("to") && trans["to"].is_object())
                {
                    t.toChapter = trans["to"].value("chapter", "");
                    t.entryMarker = trans["to"].value("marker", "");
                }
                else
                {
                    t.entryMarker = trans.value("entryMarker", "");
                }

                // Optional condition flags
                if (trans.contains("when") && trans["when"].is_array())
                {
                    for (const auto& flag : trans["when"])
                    {
                        t.when.push_back(flag.get<std::string>());
                    }
                }

                adventure.transitions.push_back(t);
            }
        }

        // Load pre-declared flags
        if (doc.contains("flags") && doc["flags"].is_array())
        {
            for (const auto& flag : doc["flags"])
            {
                adventure.flags.push_back(flag.get<std::string>());
            }
        }

        auto fail = [&](const std::string& why) -> std::optional<Adventure> {
            if (error)
                *error = (folder.empty() ? "" : folder + "/") + "adventure.json: " + why;
            return {};
        };
        if (adventure.minLevel < 1 || adventure.maxLevel > 20 || adventure.minLevel > adventure.maxLevel)
            return fail("minLevel and maxLevel are 1 to 20, lowest first");
        if (adventure.recommendedPartySize < 1 || adventure.recommendedPartySize > 4)
            return fail("recommendedPartySize is 1 to 4");
        if (adventure.chapterFolders.empty())
            return fail("an adventure needs at least one chapter");
        for (const std::string& flag : adventure.flags)
            if (flag.empty() || flag.size() > 64)
                return fail("story flags are 1 to 64 characters");
        adventure.camp = doc.value("camp", std::string{});
        if (!adventure.camp.empty())
        {
            std::string problem;
            if (adventure.hasFolder(adventure.camp))
                return fail("the camp is its own chapter, not one of the adventure's");
            if (!Chapter::load(files, adventure.camp, &problem))
                return fail("camp " + adventure.camp + ": " + problem);
        }

        // Every chapter must load; their maps say which markers exist.
        std::vector<Chapter> chapters;
        for (const auto& chapterFolder : adventure.chapterFolders)
        {
            std::string problem;
            auto chapter = Chapter::load(files, chapterFolder, &problem);
            if (!chapter)
            {
                if (error)
                    *error = chapterFolder + ": " + (problem.empty() ? std::string("failed to load") : problem);
                return {};
            }
            if (std::find(adventure.chapterIds.begin(), adventure.chapterIds.end(), chapter->id) != adventure.chapterIds.end())
                return fail("two chapters have the id " + chapter->id);
            if (chapter->level < adventure.minLevel || chapter->level > adventure.maxLevel)
                return fail(chapter->id + " is written for level " + std::to_string(chapter->level) + ", outside the adventure's range");
            // The party travels as one: every chapter seats the same heroes.
            if (!chapters.empty() && chapter->party.size() != chapters.front().party.size())
                return fail(chapter->id + " seats " + std::to_string(chapter->party.size()) + " heroes, the first chapter "
                    + std::to_string(chapters.front().party.size()));
            adventure.chapterIds.push_back(chapter->id);
            chapters.push_back(std::move(*chapter));
        }
        auto chapterOf = [&](const std::string& id) -> const Chapter* {
            for (const Chapter& c : chapters)
                if (c.id == id)
                    return &c;
            return nullptr;
        };
        for (const Transition& t : adventure.transitions)
        {
            const Chapter* from = chapterOf(t.fromChapter);
            const Chapter* to = chapterOf(t.toChapter);
            if (!from || !to)
                return fail("a transition names a chapter that isn't in the list: " + (from ? t.toChapter : t.fromChapter));
            if (!from->map.marker(t.exitMarker))
                return fail(t.fromChapter + " has no marker \"" + t.exitMarker + "\"");
            if (!to->map.marker(t.entryMarker))
                return fail(t.toChapter + " has no marker \"" + t.entryMarker + "\"");
            if (!to->map.walkable(*to->map.marker(t.entryMarker)))
                return fail(t.toChapter + "'s marker \"" + t.entryMarker + "\" is on a cell nobody can stand on");
        }

        return adventure;
    }
    catch (const std::exception& e)
    {
        if (error)
            *error = folder + "/adventure.json: " + std::string(e.what());
        return {};
    }
}

std::optional<std::string> Adventure::nextChapter(std::string_view currentChapter,
    std::string_view marker, const std::vector<std::string>& setFlags) const
{
    const Transition* open = transition(currentChapter, marker, setFlags);
    return open ? std::optional(open->toChapter) : std::nullopt;
}

const Adventure::Transition* Adventure::transition(std::string_view currentChapter, std::string_view marker,
    const std::vector<std::string>& setFlags) const
{
    for (const auto& trans : transitions)
        if (trans.fromChapter == currentChapter && trans.exitMarker == marker
            && std::all_of(trans.when.begin(), trans.when.end(),
                [&](const std::string& flag) { return std::find(setFlags.begin(), setFlags.end(), flag) != setFlags.end(); }))
            return &trans;
    return nullptr;
}

std::string Adventure::folderOf(std::string_view chapterId) const
{
    for (size_t i = 0; i < chapterIds.size() && i < chapterFolders.size(); i++)
        if (chapterIds[i] == chapterId)
            return chapterFolders[i];
    return {};
}

bool Adventure::hasFolder(std::string_view folder) const
{
    return std::find(chapterFolders.begin(), chapterFolders.end(), folder) != chapterFolders.end();
}

std::vector<std::string> Adventure::listedChapters(const yh::FileSystem& files, const std::string& folder)
{
    std::vector<std::string> listed;
    const auto text = files.readText(folder.empty() ? "adventure.json" : folder + "/adventure.json");
    if (!text)
        return listed;
    try
    {
        const auto doc = json::parse(*text);
        for (const auto& ch : doc.value("chapters", json::array()))
        {
            if (ch.is_string())
                listed.push_back(ch.get<std::string>());
            else if (ch.is_object() && ch.contains("folder"))
                listed.push_back(ch.at("folder").get<std::string>());
        }
    }
    catch (const std::exception&)
    {
        listed.clear(); // a broken file belongs to nothing; Adventure::load says what is wrong with it
    }
    return listed;
}
