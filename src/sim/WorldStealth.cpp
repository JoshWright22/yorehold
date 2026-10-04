// What the party sees, and sneaking past what sees them.

#include "World.h"

#include <cmath>
#include <functional>
#include <span>

namespace
{

constexpr float cell = GameMap::cellSize;

float distance(yh::Vec2 a, yh::Vec2 b)
{
    const yh::Vec2 d = a - b;
    return std::sqrt(d.x * d.x + d.y * d.y);
}

}

void World::setOptions(const Options& options)
{
    options_ = options;
}

GameMap::LightingMode World::lightingMode() const
{
    if (options_.lighting >= 1 && options_.lighting <= 3)
        return static_cast<GameMap::LightingMode>(options_.lighting - 1);
    return map().lighting().mode;
}

GameMap::Time World::timeOfDay() const
{
    // There's no sky to change underground.
    if (map().lighting().time != GameMap::Time::Underground && options_.timeOfDay >= 1 && options_.timeOfDay <= 3)
        return static_cast<GameMap::Time>(options_.timeOfDay - 1);
    return map().lighting().time;
}

int World::viewTeam() const
{
    if (options_.sharedFog)
        return 0;
    // Whoever's turn it is in a fight, otherwise the selected (leading) hero.
    if (const std::optional<size_t> current = currentCreature(); current && *current < heroCount_)
        return static_cast<int>(*current) + 1;
    for (size_t i = 0; i < heroCount_; i++)
        if (tokens_.tokens[i].selected)
            return static_cast<int>(i) + 1;
    return 1;
}

// Wall cells are never in line of sight (their centre is behind the wall edge), so show the ones
// bordering what a view sees.
void World::revealWalls(int team)
{
    for (int y = 0; y < map().height(); y++)
    {
        for (int x = 0; x < map().width(); x++)
        {
            if (map().blocksSight({x, y}) || fog_.state(team, 0, {x, y}) != yh::FogState::Visible)
                continue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (map().inside({x + dx, y + dy}) && map().blocksSight({x + dx, y + dy}))
                        fog_.reveal(team, 0, {x + dx, y + dy});
        }
    }
}

void World::updateVisibility()
{
    const GameMap::Lighting& lighting = map().lighting();
    const bool rules = lightingMode() == GameMap::LightingMode::Rules;
    const GameMap::Sky sky = map().sky(timeOfDay());
    std::vector<yh::Vision> eyes(heroCount_);
    std::vector<yh::Light> carried;
    for (size_t i = 0; i < heroCount_; i++)
    {
        if (tokens_.tokens[i].floor == dead)
            continue;
        const float darkvision = creatures_[i].sheet.stats.value("darkvision") / std::max(1, rules_.feetPerSquare) * cell;
        eyes[i] = {tokens_.tokens[i].position, sky.sight * cell, darkvision};
        if (lighting.carried > 0 && !creatures_[i].sneaking())
            carried.push_back({tokens_.tokens[i].position, lighting.carried * cell});
    }
    // In rules mode a cell is only seen if some light reaches it (or it's within darkvision).
    // By day the outdoors is lit by the sky and seen from far off; under a roof it's the usual
    // sight distance and the map's own lights.
    std::function<bool(yh::Cell)> lit;
    if (rules || sky.differs)
    {
        lit = [&, rules](yh::Cell c) {
            if (sky.differs && !map().indoors(c))
                return !rules || sky.level != yh::LightLevel::Dark || lightLevels_.lit(c, carried, map().walls());
            if (sky.differs)
            {
                const yh::Vec2 at = grid_.center(c);
                const float reach = lighting.sight * cell;
                bool near = false;
                for (size_t i = 0; i < heroCount_ && !near; i++)
                    near = tokens_.tokens[i].floor != dead && distance(at, tokens_.tokens[i].position) <= reach;
                if (!near)
                    return false;
            }
            return !rules || lightLevels_.lit(c, carried, map().walls());
        };
    }

    // Team 0 is everyone's view together; it decides when enemies are spotted. With shared fog
    // off, each hero also keeps a view of their own (team 1 + index) for the screen.
    std::vector<yh::Vision> all;
    for (size_t i = 0; i < heroCount_; i++)
        if (tokens_.tokens[i].floor != dead)
            all.push_back(eyes[i]);
    fog_.update(0, 0, all, map().walls(), lit);
    revealWalls(0);
    if (!options_.sharedFog)
    {
        for (size_t i = 0; i < heroCount_; i++)
        {
            const std::span<const yh::Vision> mine = tokens_.tokens[i].floor == dead ? std::span<const yh::Vision>() : std::span(&eyes[i], 1);
            fog_.update(static_cast<int>(i) + 1, 0, mine, map().walls(), lit);
            revealWalls(static_cast<int>(i) + 1);
        }
    }
    const int view = viewTeam();

    const bool fighting = encounter_ && !encounter_->finished();
    for (size_t i = heroCount_; i < creatures_.size(); i++)
    {
        yh::Token& token = tokens_.tokens[i];
        if (token.floor == dead)
            continue;
        token.floor = fog_.state(view, 0, cellOf(i)) == yh::FogState::Visible ? 0 : hidden;
    }
    // The host decides when a fight starts.
    if (!fighting && !partyDown() && !remote_ && !talk_)
        updateStealth();
}

void World::setSneaking(size_t hero, bool on)
{
    yh::Character& sheet = creatures_[hero].sheet;
    if (!on)
        sheet.removeCondition(hiddenCondition);
    else if (!sheet.hasCondition(hiddenCondition))
        sheet.addCondition(rules_, hiddenCondition);
}

bool World::sneakingMine() const
{
    for (size_t i = 0; i < heroCount_; i++)
        if (creatures_[i].sneaking() && mine(i) && !creatures_[i].sheet.down())
            return true;
    return false;
}

yh::LightLevel World::lightAt(yh::Vec2 point) const
{
    if (lightingMode() != GameMap::LightingMode::Rules)
        return yh::LightLevel::Bright;
    const yh::Cell c = grid_.cellAt(point);
    const GameMap::Sky sky = map().sky(timeOfDay());
    if (sky.differs && !map().indoors(c) && sky.level != yh::LightLevel::Dark)
        return sky.level;
    std::vector<yh::Light> carried;
    for (size_t i = 0; i < heroCount_; i++)
        if (tokens_.tokens[i].floor != dead && map().lighting().carried > 0 && !creatures_[i].sneaking())
            carried.push_back({tokens_.tokens[i].position, map().lighting().carried * cell});
    return lightLevels_.level(c, carried, map().walls());
}

std::vector<yh::Watcher> World::watchers() const
{
    // They see as far as the heroes do: the usual distance under a roof, further under an open sky.
    const GameMap::Sky sky = map().sky(timeOfDay());
    const char* perception = rules_.skill("perception") ? "perception" : "wis";
    std::vector<yh::Watcher> watching(creatures_.size() - heroCount_);
    for (size_t i = heroCount_; i < creatures_.size(); i++)
    {
        const Creature& c = creatures_[i];
        yh::Watcher& watcher = watching[i - heroCount_];
        watcher.position = tokens_.tokens[i].position;
        watcher.facing = c.facing;
        watcher.passivePerception = rules_.passiveBase + c.sheet.checkModifier(rules_, perception);
        watcher.darkRange = c.sheet.stats.value("darkvision") / std::max(1, rules_.feetPerSquare) * cell;
        // Only enemies that haven't noticed anything yet keep watch.
        const float sight = (sky.differs && map().indoors(cellOf(i)) ? map().lighting().sight : sky.sight) * cell;
        watcher.range = c.team == 1 && !c.awake && !c.sheet.down() ? sight : -1;
    }
    return watching;
}

void World::updateStealth()
{
    const std::vector<yh::Watcher> watching = watchers();
    const std::function<yh::LightLevel(yh::Vec2)> light = [this](yh::Vec2 point) { return lightAt(point); };
    const char* stealth = rules_.skill("stealth") ? "stealth" : "dex";
    std::optional<size_t> noticed; // the enemy that saw someone
    std::string note;              // how, for the log
    for (size_t h = 0; h < heroCount_; h++)
    {
        const yh::Vec2 at = tokens_.tokens[h].position;
        yh::Vec2 from = lastAt_[h];
        lastAt_[h] = at;
        if (noticed || tokens_.tokens[h].floor == dead)
            continue;
        if (distance(from, at) > 2 * cell) // put somewhere else (a load, the end of a fight), not walked
        {
            from = at;
            sneak_[h].reset();
        }
        if (!creatures_[h].sneaking())
        {
            // Walking openly: the fight starts as soon as they and an enemy can see each other.
            for (size_t w = 0; w < watching.size() && !noticed; w++)
                if (watching[w].range > 0 && fog_.state(0, 0, cellOf(heroCount_ + w)) == yh::FogState::Visible
                    && distance(at, watching[w].position) <= watching[w].range + cell / 2 && yh::lineOfSight(at, watching[w].position, map().walls()))
                    noticed = heroCount_ + w;
            continue;
        }
        const int bonus = creatures_[h].sheet.checkModifier(rules_, stealth);
        for (const yh::StealthCheck& check : sneak_[h].move(from, at, true, bonus, watching, map().walls(), stealthRandom_, light))
        {
            if (!check.spotted)
            {
                act("unseen", nlohmann::json{{"hero", h}}.dump());
                continue;
            }
            noticed = heroCount_ + check.watcher;
            note = creatures_[*noticed].sheet.name + " spots " + creatures_[h].sheet.name + "! (Stealth " + std::to_string(check.total) + " against "
                + std::to_string(check.dc) + ")";
        }
    }
    if (!noticed)
        return;
    // Everyone's positions go with it, so the fight starts the same on every machine.
    nlohmann::json at = nlohmann::json::array();
    for (size_t c = 0; c < creatures_.size(); c++)
        at.push_back({tokens_.tokens[c].position.x, tokens_.tokens[c].position.y});
    act("fight", nlohmann::json{{"group", creatures_[*noticed].group}, {"at", at}, {"note", note}}.dump());
}
