// What a World writes down between fights, and how it is read back.

#include "Save.h"
#include "World.h"

#include <nlohmann/json.hpp>

#include <algorithm>
#include <cmath>
#include <stdexcept>

const yh::SaveFormat& Save::format()
{
    static const yh::SaveFormat format = [] {
        yh::SaveFormat f("yorehold.adventure", 3);
        // v1 counted short rests left (2 per adventure); v2 counts uses per rest id.
        f.migrate(1, [](nlohmann::json& data) {
            const int left = data.at("restsLeft").get<int>();
            data.erase("restsLeft");
            data["restsUsed"] = {{"short", std::max(0, 2 - left)}};
        });
        // Earlier saves always belonged to the original keep, whose placements haven't changed.
        f.migrate(2, [](nlohmann::json& data) {
            data["chapterId"] = "goblin-keep";
            data["chapterFolder"] = "chapters/goblin-keep";
        });
        return f;
    }();
    return format;
}

std::string World::stateJson() const
{
    nlohmann::json data;
    data["chapterId"] = chapter_->id;
    data["chapterFolder"] = chapter_->folder;
    data["chapterSignature"] = chapter_->signature;
    data["seed"] = seed_;
    data["fights"] = fights_;
    data["restsUsed"] = restsUsed_;
    data["flags"] = flags_;
    data["rolls"] = rolls_;
    data["fog"] = nlohmann::json::parse(fog_.toJson());
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        const yh::Token& token = tokens_.tokens[i];
        if (i < heroCount_)
        {
            yh::CharacterChoices choices = creatures_[i].choices;
            choices.xp = creatures_[i].sheet.xp;
            data["choices"].push_back(nlohmann::json::parse(choices.toJson()));
        }
        data["creatures"].push_back({
            {"sheet", nlohmann::json::parse(creatures_[i].sheet.toJson())},
            {"awake", creatures_[i].awake},
            {"fled", creatures_[i].fled},
            {"surrendered", creatures_[i].surrendered},
            {"sneaking", creatures_[i].sneaking()},
            {"team", creatures_[i].team},
            {"x", token.path.empty() ? token.position.x : token.path.back().x},
            {"y", token.path.empty() ? token.position.y : token.path.back().y},
        });
    }
    return data.dump();
}

std::string World::snapshot() const
{
    nlohmann::json j = nlohmann::json::parse(stateJson());
    j["seats"] = seats_;
    j["checkpoint"] = checkpoint_;
    for (const auto& [id, name] : playerNames_)
        j["names"][std::to_string(id)] = name;
    return j.dump();
}

bool World::restoreState(std::string_view text, std::string* problem)
{
    if (!chapter_)
        return false;
    std::string error;
    try
    {
        const nlohmann::json data = nlohmann::json::parse(text);
        const std::string checkpoint = data.value("checkpoint", std::string{});
        if (!checkpoint.empty())
        {
            const auto savedCheckpoint = nlohmann::json::parse(checkpoint);
            if (!savedCheckpoint.is_object() || savedCheckpoint.at("chapterId") != chapter_->id
                || savedCheckpoint.at("chapterFolder") != chapter_->folder
                || savedCheckpoint.at("chapterSignature") != chapter_->signature)
                throw std::runtime_error("Snapshot checkpoint belongs to different content");
        }
        if (data.at("chapterId") != chapter_->id || data.at("chapterFolder") != chapter_->folder)
            throw std::runtime_error("this save belongs to another chapter");
        if (data.contains("chapterSignature") && data.at("chapterSignature") != chapter_->signature)
            throw std::runtime_error("the chapter's content has changed since this save");
        const nlohmann::json& saved = data.at("creatures");
        const auto& fogData = data.at("fog");
        if (fogData.at("width") != map().width() || fogData.at("height") != map().height()
            || fogData.at("cellSize") != GameMap::cellSize)
            throw std::runtime_error("the map has changed since this save");
        std::optional<yh::FogOfWar> fog = yh::FogOfWar::fromJson(data.at("fog").dump(), &error);
        if (!fog || !saved.is_array() || saved.size() != creatures_.size())
            throw std::runtime_error(fog ? "the map has changed since this save" : error);
        // Check all state before applying any of it, including positions and recovery counters.
        const auto seed = data.at("seed").get<uint64_t>();
        const int fights = data.at("fights").get<int>();
        auto rests = data.at("restsUsed").get<std::map<std::string, int>>();
        if (fights < 0 || std::any_of(rests.begin(), rests.end(), [](const auto& entry) { return entry.second < 0; }))
            throw std::runtime_error("invalid adventure counters");
        auto flags = data.value("flags", std::set<std::string>{});
        const auto rolls = data.value("rolls", uint64_t{0});
        // Only in a co-op snapshot: who plays which hero.
        auto seats = data.value("seats", seats_);
        if (!seats.empty() && seats.size() != chapter_->party.size())
            throw std::runtime_error("seats don't match the party");
        // The heroes' choices; saves from before they were kept read them off the saved sheets.
        const nlohmann::json savedChoices = data.value("choices", nlohmann::json::array());
        if (!savedChoices.is_array() || (!savedChoices.empty() && savedChoices.size() != heroCount_))
            throw std::runtime_error("saved characters don't match the party");
        std::vector<yh::CharacterChoices> choices;
        std::vector<yh::Character> sheets;
        std::vector<yh::Vec2> positions;
        std::vector<bool> awake, fled, surrendered, sneaking;
        std::vector<int> teams;
        for (const nlohmann::json& c : saved)
        {
            std::optional<yh::Character> sheet = yh::Character::fromJson(c.at("sheet").dump(), &error);
            if (!sheet || !sheet->checkProficiencyRanks(rules_, &error))
                throw std::runtime_error(error);
            if (!c.at("sheet").contains("death")) sheet->death.saves = creatures_[sheets.size()].sheet.death.saves;
            if (rules_.death.enabled && (sheet->death.failures > rules_.death.failures || sheet->death.successes > rules_.death.successes
                || (!sheet->death.dead && sheet->death.failures == rules_.death.failures)
                || (!sheet->death.stable && !sheet->death.dead && sheet->death.successes == rules_.death.successes)))
                throw std::runtime_error("Saved death counters do not match the ruleset");
            if (sheets.size() < heroCount_)
            {
                std::optional<yh::CharacterChoices> made = savedChoices.empty()
                    ? std::optional(yh::choicesFromSheet(rules_, *sheet, chapter_->party[sheets.size()].classId))
                    : yh::CharacterChoices::fromJson(savedChoices.at(sheets.size()).dump(), &error);
                const std::optional<yh::Character> built = made ? chapter_->compendium.build(rules_, *made, &error) : std::nullopt;
                if (!built)
                    throw std::runtime_error("saved character " + sheet->name + ": " + error);
                sheet->adoptBuild(*built);
                choices.push_back(std::move(*made));
            }
            sheets.push_back(std::move(*sheet));
            const yh::Vec2 position{c.at("x").get<float>(), c.at("y").get<float>()};
            if (!std::isfinite(position.x) || !std::isfinite(position.y) || position.x < 0 || position.y < 0
                || position.x >= map().width() * GameMap::cellSize || position.y >= map().height() * GameMap::cellSize)
                throw std::runtime_error("saved token is outside the map");
            positions.push_back(position);
            awake.push_back(c.at("awake").get<bool>());
            fled.push_back(c.value("fled", false) && sheets.back().down());
            sneaking.push_back(c.value("sneaking", false) && sneaking.size() < heroCount_ && !sheets.back().down()); // older saves have none
            // Only an NPC's side can change (a peaceful one the party attacked), or an enemy's that gave up.
            const int team = c.value("team", creatures_[teams.size()].team);
            surrendered.push_back(c.value("surrendered", false) && team == 2 && teams.size() >= heroCount_);
            if (team != creatures_[teams.size()].team && !(creatures_[teams.size()].npc >= 0 && (team == 1 || team == 2)) && !surrendered.back())
                throw std::runtime_error("saved creature is on the wrong side");
            teams.push_back(team);
        }
        seats_ = std::move(seats);
        newAdventure(seed);
        fog_ = std::move(*fog);
        fights_ = fights;
        restsUsed_ = std::move(rests);
        flags_ = std::move(flags);
        rolls_ = rolls;
        for (size_t i = 0; i < creatures_.size(); i++)
        {
            creatures_[i].sheet = std::move(sheets[i]);
            if (i < heroCount_)
                creatures_[i].choices = std::move(choices[i]);
            creatures_[i].awake = awake[i];
            creatures_[i].fled = fled[i];
            creatures_[i].surrendered = surrendered[i];
            setSneaking(i, sneaking[i]); // also for saves from before sneaking was a condition on the sheet
            creatures_[i].team = teams[i];
            yh::Token& token = tokens_.tokens[i];
            if (creatures_[i].npc >= 0)
                token.owner = teams[i] == 1 ? enemyOwner : npcOwner;
            token.position = positions[i];
            token.path.clear();
            if (creatures_[i].sheet.down())
                token.floor = dead;
        }
        fallenConditions();
        emit({Event::Kind::Resumed});
        checkpoint_ = checkpoint.empty() ? stateJson() : checkpoint;
        return true;
    }
    catch (const std::exception& e)
    {
        if (problem)
            *problem = e.what();
        return false;
    }
}

void World::gainLevels(size_t hero)
{
    Creature& c = creatures_[hero];
    if (c.choices.levels.empty() || c.sheet.level <= c.choices.level())
        return;
    yh::CharacterChoices choices = c.choices;
    choices.xp = c.sheet.xp;
    choices.levels.resize(static_cast<size_t>(c.sheet.level), yh::LevelChoice{choices.levels.back().classId, {}});
    std::string error;
    const std::optional<yh::Character> built = chapter_->compendium.build(rules_, choices, &error);
    if (!built)
    {
        say(c.sheet.name + " can't level up: " + error);
        return;
    }
    const int before = c.sheet.maxHp();
    c.sheet.adoptBuild(*built);
    if (!c.sheet.down())
        c.sheet.hp += std::max(0, c.sheet.maxHp() - before);
    c.choices = std::move(choices);
    say(c.sheet.name + " reaches level " + std::to_string(c.sheet.level) + ".");
}

void World::returnFromWipe(const std::string& checkpoint)
{
    std::string error;
    if (checkpoint.empty() || !restoreState(checkpoint, &error))
    {
        const uint64_t seed = seed_;
        newAdventure(seed);
        if (!error.empty()) say("Couldn't return to the autosave: " + error);
    }
    bool occupiedDestination = false;
    for (const yh::Cell destination : chapter_->wipeDestination)
        for (size_t i = heroCount_; i < creatures_.size(); i++)
            if (!creatures_[i].sheet.down() && cellOf(i) == destination) occupiedDestination = true;
    if (!occupiedDestination)
        for (size_t i = 0; i < chapter_->wipeDestination.size(); i++)
            tokens_.tokens[i].position = grid_.center(chapter_->wipeDestination[i]);
    else
        say("The wipe destination is occupied; using the autosave positions.");
    selectOwnHero();
    say("The party returns to the autosave.");
    emit({Event::Kind::Banner, chapter_->resumeText, {}, FloatKind::Miss, 2});
    requestSave();
}
