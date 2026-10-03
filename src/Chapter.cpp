#include "Chapter.h"

#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/animation/Cutscene.h>

#include <nlohmann/json.hpp>

#include <algorithm>
#include <cstdint>
#include <set>
#include <stdexcept>

namespace
{

yh::Cell cellFrom(const nlohmann::json& j)
{
    const auto v = j.get<std::vector<int>>();
    if (v.size() != 2) throw std::invalid_argument("cells are [x, y]");
    return {v[0], v[1]};
}

yh::Color colorFrom(const nlohmann::json& j)
{
    const auto v = j.get<std::vector<int>>();
    if ((v.size() != 3 && v.size() != 4) || std::any_of(v.begin(), v.end(), [](int c) { return c < 0 || c > 255; }))
        throw std::invalid_argument("colours are [r,g,b] or [r,g,b,a] in 0..255");
    return {static_cast<uint8_t>(v[0]), static_cast<uint8_t>(v[1]), static_cast<uint8_t>(v[2]), static_cast<uint8_t>(v.size() == 4 ? v[3] : 255)};
}

// Paths in a chapter are relative to its folder; anything not found there is looked up from the root.
std::string resolve(const yh::FileSystem& files, const std::string& folder, const std::string& path)
{
    if (path.empty() || yh::FileSystem::normalize(path) != path)
        throw std::invalid_argument("expected a relative content path: " + path);
    const std::string local = folder + "/" + path;
    return files.exists(local) ? local : path;
}

bool validId(std::string_view id)
{
    return !id.empty() && id.size() <= 64 && std::all_of(id.begin(), id.end(), [](char c) {
        return (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
    });
}

std::string readOrThrow(const yh::FileSystem& files, const std::string& path)
{
    std::optional<std::string> text = files.readText(path);
    if (!text) throw std::invalid_argument("missing file " + path);
    return std::move(*text);
}

}

std::optional<Chapter> Chapter::load(const yh::FileSystem& files, std::string_view folderView, std::string* error)
{
    if (error) error->clear();
    const std::string folder(folderView);
    std::string where = folder + "/chapter.json";
    try
    {
        if (!folder.empty() && yh::FileSystem::normalize(folder) != folder)
            throw std::invalid_argument("expected a relative chapter folder");
        const auto j = nlohmann::json::parse(readOrThrow(files, where));
        Chapter c;
        c.folder = folder;
        c.id = j.at("id").get<std::string>();
        if (!validId(c.id)) throw std::invalid_argument("chapter ids use a-z, 0-9, - and _");
        c.title = j.value("title", c.id);
        c.intro = j.value("intro", std::vector<std::string>{});
        c.xpPerVictory = j.value("xpPerVictory", 0);
        c.victoryText = j.value("victoryText", c.victoryText);
        c.defeatText = j.value("defeatText", c.defeatText);
        c.resumeText = j.value("resumeText", c.resumeText);
        c.clearedText = j.value("clearedText", c.clearedText);
        if (const auto endings = j.value("endings", nlohmann::json::object()); endings.contains("cleared"))
        {
            const auto path = endings.at("cleared").get<std::string>();
            if (!path.empty()) c.clearedCutscene = resolve(files, folder, path);
        }
        if (c.xpPerVictory < 0) throw std::invalid_argument("xpPerVictory can't be negative");

        // Rules: a built-in name or a file.
        const std::string ruleset = j.value("ruleset", "modern");
        if (ruleset == "modern")
            c.rules = yh::Ruleset::modern();
        else if (ruleset == "classic")
            c.rules = yh::Ruleset::classic();
        else
        {
            where = resolve(files, folder, ruleset);
            std::string problem;
            std::optional<yh::Ruleset> rules = yh::Ruleset::fromJson(readOrThrow(files, where), &problem);
            if (!rules) throw std::invalid_argument(problem);
            c.rules = std::move(*rules);
        }

        // Shared content first, then the chapter's own (which can replace shared entries).
        std::string problem;
        if (!c.compendium.load(files, "", &problem) || !c.compendium.load(files, folder, &problem))
        {
            where = "content";
            throw std::invalid_argument(problem);
        }

        where = resolve(files, folder, j.value("map", "map.json"));
        std::optional<GameMap> map = GameMap::fromJson(readOrThrow(files, where), &problem);
        if (!map) throw std::invalid_argument(problem);
        c.map = std::move(*map);
        // Hash canonical data rather than whitespace. Shared definitions and the rules are included,
        // so a save cannot silently apply old sheets to a newly edited chapter.
        uint64_t signature = 14695981039346656037ull;
        auto include = [&](std::string_view text) {
            for (const unsigned char byte : text)
            {
                signature ^= byte;
                signature *= 1099511628211ull;
            }
            signature ^= 0xff;
            signature *= 1099511628211ull;
        };
        include(j.dump());
        include(nlohmann::json::parse(readOrThrow(files, where)).dump());
        include(c.rules.toJson());
        for (const auto& [id, item] : c.compendium.items) include(yh::Compendium::itemToJson(item));
        for (const auto& [id, definition] : c.compendium.classes) include(yh::Compendium::classToJson(definition));
        for (auto& [id, definition] : c.compendium.creatures)
        {
            if (!definition.token.image.empty())
            {
                where = resolve(files, folder, definition.token.image);
                if (!files.exists(where)) throw std::invalid_argument("missing token image");
                definition.token.image = where;
            }
            include(yh::Compendium::creatureToJson(definition));
        }
        if (!c.clearedCutscene.empty())
        {
            where = c.clearedCutscene;
            const auto endingText = readOrThrow(files, where);
            if (!yh::Cutscene::fromJson(endingText, &problem)) throw std::invalid_argument(problem);
            include(nlohmann::json::parse(endingText).dump());
        }
        c.signature = std::to_string(signature);
        where = folder + "/chapter.json";

        std::set<std::pair<int, int>> taken;
        auto place = [&](yh::Cell at, const std::string& who) {
            if (!c.map.walkable(at)) throw std::invalid_argument(who + " starts on a cell you can't stand on");
            if (!taken.insert({at.x, at.y}).second) throw std::invalid_argument(who + " starts on an occupied cell");
        };
        if (!j.at("party").is_array()) throw std::invalid_argument("party must be an array");
        for (const auto& p : j.at("party"))
        {
            PartyMember member{p.at("name").get<std::string>(), p.at("class").get<std::string>(),
                p.contains("color") ? colorFrom(p.at("color")) : yh::Color{200, 200, 210, 255}, cellFrom(p.at("at"))};
            if (!c.compendium.characterClass(member.classId)) throw std::invalid_argument("unknown class \"" + member.classId + "\" for " + member.name);
            place(member.at, member.name);
            c.party.push_back(std::move(member));
        }
        if (c.party.empty()) throw std::invalid_argument("the party needs at least one member");

        std::set<std::string> encounterIds;
        const auto encounters = j.value("encounters", nlohmann::json::array());
        if (!encounters.is_array()) throw std::invalid_argument("encounters must be an array");
        for (const auto& e : encounters)
        {
            Encounter encounter{e.value("id", "encounter " + std::to_string(c.encounters.size() + 1)), e.value("text", ""), {}};
            if (!encounterIds.insert(encounter.id).second) throw std::invalid_argument("duplicate encounter " + encounter.id);
            if (!e.at("creatures").is_array()) throw std::invalid_argument("encounter creatures must be an array");
            for (const auto& p : e.at("creatures"))
            {
                Placement placement{p.at("creature").get<std::string>(), p.value("name", ""), cellFrom(p.at("at"))};
                if (!c.compendium.creature(placement.creatureId))
                    throw std::invalid_argument("unknown creature \"" + placement.creatureId + "\" in " + encounter.id);
                place(placement.at, placement.name.empty() ? placement.creatureId : placement.name);
                encounter.creatures.push_back(std::move(placement));
            }
            if (encounter.creatures.empty()) throw std::invalid_argument(encounter.id + " has no creatures");
            c.encounters.push_back(std::move(encounter));
        }
        return c;
    }
    catch (const std::exception& e)
    {
        if (error) *error = where + ": " + e.what();
        return std::nullopt;
    }
}
