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

        // Validate that all chapters can load
        for (const auto& chapterFolder : adventure.chapterFolders)
        {
            auto chapter = Chapter::load(files, chapterFolder, error);
            if (!chapter)
            {
                if (error)
                    *error = chapterFolder + ": " + (*error ? *error : "failed to load");
                return {};
            }
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

std::optional<const std::string&> Adventure::nextChapter(std::string_view currentChapter,
    std::string_view marker, const std::vector<std::string>& setFlags) const
{
    for (const auto& trans : transitions)
    {
        if (trans.fromChapter == currentChapter && trans.exitMarker == marker)
        {
            // Check if all required flags are set
            bool allowed = true;
            for (const auto& flag : trans.when)
            {
                if (std::find(setFlags.begin(), setFlags.end(), flag) == setFlags.end())
                {
                    allowed = false;
                    break;
                }
            }

            if (allowed)
            {
                return trans.toChapter;
            }
        }
    }

    return {};
}
