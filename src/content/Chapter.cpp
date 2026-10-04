#include "Chapter.h"

#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/animation/Cutscene.h>
#include <yorehold/framework/rpg/Dialogue.h>
#include <yorehold/framework/rpg/QuestJournal.h>

#include <nlohmann/json.hpp>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <numbers>
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

// An optional list of story flag names.
std::vector<std::string> flagsFrom(const nlohmann::json& j, const char* key)
{
    const auto flags = j.value(key, std::vector<std::string>{});
    if (std::any_of(flags.begin(), flags.end(), [](const std::string& f) { return f.empty() || f.size() > 64; }))
        throw std::invalid_argument(std::string(key) + ": story flags are 1 to 64 characters");
    return flags;
}

// An optional "ai" entry, kept as JSON text: a profile name or an object of changes.
std::string aiFrom(const nlohmann::json& j, const yh::Compendium& compendium)
{
    if (!j.contains("ai"))
        return {};
    const nlohmann::json& ai = j.at("ai");
    if (!ai.is_string() && !ai.is_object()) throw std::invalid_argument("ai is a profile name or an object");
    std::string problem;
    if (!yh::AiProfile::fromJson(ai.dump(), &problem, compendium.aiLookup(), yh::AiProfile::preset("cunning")))
        throw std::invalid_argument(problem);
    return ai.dump();
}

// The chapter file without anything that only says how creatures think, for the save signature.
nlohmann::json withoutAi(nlohmann::json j)
{
    if (j.is_object())
    {
        j.erase("ai");
        j.erase("aiChanges");
    }
    for (auto& child : j)
        if (child.is_structured())
            child = withoutAi(child);
    return j;
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

        // Rules: the game's own unless the chapter names a built-in set, a file, or a folder with
        // ruleset.json in it. Content checked away from the game has no such folder and gets "modern".
        const std::string ruleset = j.value("ruleset", files.exists(std::string(defaultRuleset) + "/ruleset.json") ? defaultRuleset : "modern");
        if (ruleset == "modern")
            c.rules = yh::Ruleset::modern();
        else if (ruleset == "classic")
            c.rules = yh::Ruleset::classic();
        else
        {
            where = resolve(files, folder, ruleset + "/ruleset.json");
            if (files.exists(where))
                c.rulesFolder = where.substr(0, where.size() - std::string_view("/ruleset.json").size());
            else
                where = resolve(files, folder, ruleset);
            std::string problem;
            std::optional<yh::Ruleset> rules = yh::Ruleset::fromJson(readOrThrow(files, where), &problem);
            if (!rules) throw std::invalid_argument(problem);
            c.rules = std::move(*rules);
            // A ruleset folder keeps its conditions one to a file.
            where = c.rulesFolder + "/conditions";
            if (!c.rulesFolder.empty() && !c.rules.loadConditions(files, where, &problem))
            {
                where = "conditions";
                throw std::invalid_argument(problem);
            }
        }

        // How sneaking works: the ruleset folder's file, then the content's own from before
        // rulesets were folders, which still wins so older packages play as they did.
        for (const std::string& path : {c.rulesFolder.empty() ? std::string() : c.rulesFolder + "/stealth.json", std::string("rules/stealth.json")})
        {
            if (path.empty() || !files.exists(path))
                continue;
            where = path;
            std::string problem;
            const std::optional<yh::StealthRules> stealth = yh::StealthRules::fromJson(readOrThrow(files, where), &problem);
            if (!stealth) throw std::invalid_argument(problem);
            c.stealth = *stealth;
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
        // How creatures think is left out everywhere: it can change (a patch, the server, a writer's
        // tweak) without making old saves or a co-op partner's copy count as a different chapter.
        include(withoutAi(j).dump());
        include(nlohmann::json::parse(readOrThrow(files, where)).dump());
        include(c.rules.toJson());
        include(c.stealth.toJson());
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
            yh::CreatureDefinition sheet = definition;
            sheet.ai = yh::CreatureDefinition{}.ai;
            include(yh::Compendium::creatureToJson(sheet));
        }
        if (!c.clearedCutscene.empty())
        {
            where = c.clearedCutscene;
            const auto endingText = readOrThrow(files, where);
            if (!yh::Cutscene::fromJson(endingText, &problem)) throw std::invalid_argument(problem);
            include(nlohmann::json::parse(endingText).dump());
        }
        where = folder + "/chapter.json";

        // Surrender conversations: checked once each, and part of the signature like NPC dialogue.
        std::set<std::string> dialogues;
        auto dialogueFrom = [&](const nlohmann::json& entry, const std::string& fallback) {
            if (!entry.contains("surrender"))
                return fallback;
            const std::string path = resolve(files, folder, entry.at("surrender").get<std::string>());
            if (dialogues.insert(path).second)
            {
                where = path;
                const std::string text = readOrThrow(files, where);
                if (!yh::Dialogue::fromJson(text, &problem)) throw std::invalid_argument(problem);
                include(nlohmann::json::parse(text).dump());
                where = folder + "/chapter.json";
            }
            return path;
        };
        c.surrender = j.contains("surrender") ? dialogueFrom(j, "")
            : files.exists("dialogue/surrender.json") ? dialogueFrom(nlohmann::json{{"surrender", "dialogue/surrender.json"}}, "") : "";

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
                Placement placement{p.at("creature").get<std::string>(), p.value("name", ""), cellFrom(p.at("at")), {}};
                if (!c.compendium.creature(placement.creatureId))
                    throw std::invalid_argument("unknown creature \"" + placement.creatureId + "\" in " + encounter.id);
                placement.ai = aiFrom(p, c.compendium);
                placement.surrender = dialogueFrom(p, "");
                if (p.contains("facing"))
                {
                    placement.facing = p.at("facing").get<float>();
                    if (!std::isfinite(*placement.facing) || std::fabs(*placement.facing) > 360)
                        throw std::invalid_argument("facing is in degrees, -360 to 360, in " + encounter.id);
                }
                place(placement.at, placement.name.empty() ? placement.creatureId : placement.name);
                encounter.creatures.push_back(std::move(placement));
            }
            if (encounter.creatures.empty()) throw std::invalid_argument(encounter.id + " has no creatures");
            encounter.set = flagsFrom(e, "set");
            encounter.ai = aiFrom(e, c.compendium);
            encounter.surrender = dialogueFrom(e, "");
            c.encounters.push_back(std::move(encounter));
        }

        std::set<std::string> npcIds;
        const auto npcs = j.value("npcs", nlohmann::json::array());
        if (!npcs.is_array()) throw std::invalid_argument("npcs must be an array");
        for (const auto& n : npcs)
        {
            Npc npc{n.at("id").get<std::string>(), n.at("name").get<std::string>(),
                n.contains("color") ? colorFrom(n.at("color")) : yh::Color{200, 180, 140, 255}, cellFrom(n.at("at")),
                resolve(files, folder, n.at("dialogue").get<std::string>())};
            npc.creature = n.value("creature", npc.creature);
            npc.attacked = flagsFrom(n, "attacked");
            npc.killed = flagsFrom(n, "killed");
            npc.ai = aiFrom(n, c.compendium);
            if (!validId(npc.id) || !npcIds.insert(npc.id).second) throw std::invalid_argument("npc ids must be unique and use a-z, 0-9, - and _");
            if (!c.compendium.creature(npc.creature))
                throw std::invalid_argument("unknown creature \"" + npc.creature + "\" for " + npc.name);
            place(npc.at, npc.name);
            where = npc.dialogue;
            const std::string text = readOrThrow(files, where);
            if (!yh::Dialogue::fromJson(text, &problem)) throw std::invalid_argument(problem);
            include(nlohmann::json::parse(text).dump());
            where = folder + "/chapter.json";
            c.npcs.push_back(std::move(npc));
        }

        if (const std::string quests = j.value("quests", ""); !quests.empty())
        {
            where = c.quests = resolve(files, folder, quests);
            const std::string text = readOrThrow(files, where);
            if (!yh::QuestJournal::fromJson(text, &problem)) throw std::invalid_argument(problem);
            include(nlohmann::json::parse(text).dump());
            where = folder + "/chapter.json";
        }
        const auto changes = j.value("aiChanges", nlohmann::json::array());
        if (!changes.is_array()) throw std::invalid_argument("aiChanges must be an array");
        for (const auto& change : changes)
        {
            AiChange entry{flagsFrom(change, "when"), change.value("creature", ""), change.value("encounter", ""), change.value("name", ""),
                aiFrom(change, c.compendium)};
            if (entry.ai.empty()) throw std::invalid_argument("each aiChanges entry needs an ai");
            if (!entry.creature.empty() && !c.compendium.creature(entry.creature))
                throw std::invalid_argument("aiChanges: unknown creature \"" + entry.creature + "\"");
            if (!entry.encounter.empty() && !encounterIds.contains(entry.encounter))
                throw std::invalid_argument("aiChanges: unknown encounter \"" + entry.encounter + "\"");
            c.aiChanges.push_back(std::move(entry));
        }
        c.completeWhen = flagsFrom(j, "completeWhen");
        c.signature = std::to_string(signature);
        return c;
    }
    catch (const std::exception& e)
    {
        if (error) *error = where + ": " + e.what();
        return std::nullopt;
    }
}

yh::StealthRules Chapter::stealthOnMap() const
{
    yh::StealthRules onMap = stealth;
    const float metresPerSquare = static_cast<float>(std::max(1, rules.feetPerSquare)) * 0.3048f;
    onMap.checkEvery = stealth.checkEvery / metresPerSquare * GameMap::cellSize;
    return onMap;
}

float Chapter::facingOf(const Placement& placement) const
{
    if (placement.facing)
        return *placement.facing * std::numbers::pi_v<float> / 180;
    if (party.empty() || party.front().at == placement.at)
        return 0;
    // Nobody told it where to look: it watches the way the party comes from.
    return std::atan2(static_cast<float>(party.front().at.y - placement.at.y), static_cast<float>(party.front().at.x - placement.at.x));
}
