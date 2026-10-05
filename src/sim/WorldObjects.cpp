// Doors, levers, locks, chests and traps: the map's objects (GameMap::objects), used through Interact.

#include "World.h"

#include <yorehold/framework/rpg/Effect.h>

#include <algorithm>
#include <cmath>
#include <cstdlib>

namespace
{

// The skill or ability a check uses when the object names none, or names one this ruleset lacks.
std::string checkWith(const yh::Ruleset& rules, const std::string& wanted, const char* fallback)
{
    if (!wanted.empty() && (rules.skill(wanted) || rules.ability(wanted)))
        return wanted;
    return rules.skill(fallback) || rules.ability(fallback) ? fallback : (rules.abilities.empty() ? "" : rules.abilities.front().id);
}

std::string called(const yh::MapObject& object, const char* otherwise)
{
    return object.name.empty() ? otherwise : object.name;
}

}

bool World::pileLocked(size_t pile) const
{
    if (pile >= piles_.size() || piles_[pile].object == 0)
        return false;
    const yh::MapObject* object = map().objects().get(piles_[pile].object);
    return object && object->locked();
}

bool World::canInteract(size_t hero, yh::ObjectId id, std::string* why) const
{
    if (why)
        why->clear();
    const yh::MapObject* object = chapter_ ? map().objects().get(id) : nullptr;
    if (!object || object->destroyed || hero >= heroCount_ || creatures_[hero].sheet.down())
        return false;
    const bool hiddenTrap = object->trap && !object->trap->found;
    // A chest that isn't locked is opened by looting it, not through here.
    const bool usable = (object->door && (!object->has("container") || object->door->locked)) || object->has("lever") || object->has("interactable") || (object->armedTrap() && !hiddenTrap);
    if (!usable)
        return false;
    const yh::Cell at = cellOf(hero);
    const std::vector<yh::Cell> cells = map().cellsOf(*object);
    if (std::none_of(cells.begin(), cells.end(), [&](yh::Cell c) { return std::abs(c.x - at.x) <= 1 && std::abs(c.y - at.y) <= 1; }))
    {
        if (why)
            *why = creatures_[hero].sheet.name + " is too far from " + called(*object, "it") + ".";
        return false;
    }
    // A door can't swing shut on someone standing in it.
    if (object->door && object->door->open)
        for (size_t i = 0; i < creatures_.size(); i++)
            if (!creatures_[i].sheet.down() && std::find(cells.begin(), cells.end(), cellOf(i)) != cells.end())
            {
                if (why)
                    *why = "Something is in the way.";
                return false;
            }
    return true;
}

std::optional<yh::ObjectId> World::objectNear(size_t hero) const
{
    if (!chapter_)
        return std::nullopt;
    for (const auto& [id, object] : map().objects().all())
        if (canInteract(hero, id))
            return id;
    return std::nullopt;
}

void World::interact(size_t hero, yh::ObjectId object)
{
    act("interact", nlohmann::json{{"hero", hero}, {"object", object}}.dump());
}

void World::useObject(size_t hero, yh::ObjectId id)
{
    yh::Objects& objects = map().objects();
    yh::MapObject* object = objects.get(id);
    if (!object)
        return;
    if (fighting())
        encounter_->spendActions(equipCost(hero));
    yh::Character& sheet = creatures_[hero].sheet;
    const std::string name = called(*object, object->door ? "the door" : object->has("lever") ? "the lever" : "it");
    bool used = false;

    auto check = [&](const std::string& with, int dc, const char* what) {
        yh::Random dice = nextRandom(0x0b1ec7ull);
        const yh::RollResult roll = sheet.rollCheck(rules_, with, yh::Advantage::None, dice);
        say(sheet.name + " tries to " + what + " " + name + " (" + with + " DC " + std::to_string(dc) + "): " + roll.describe());
        return roll.total;
    };

    if (object->armedTrap() && object->trap->found)
    {
        const int dc = object->trap->disarmDc;
        const int total = check(checkWith(rules_, object->trap->disarmSkill, "dex"), dc, "disarm");
        if (total >= dc)
        {
            objects.disarm(id);
            say(sheet.name + " disarms " + name + ".");
            used = true;
        }
        else if (total <= dc - 5)
            springTrap(id, hero); // fumbled it
        else
            say(sheet.name + " can't work out how to disarm " + name + ".");
    }
    else
    {
        std::vector<std::string> keys;
        for (const yh::Item& item : sheet.inventory)
            keys.push_back("key:" + item.id);
        const bool wasLocked = object->locked();
        yh::Interaction result = objects.interact(id, keys);
        object = objects.get(id);
        if (result == yh::Interaction::Locked && object->lock && object->lock->dc > 0)
        {
            if (check(checkWith(rules_, object->lock->skill, "dex"), object->lock->dc, "pick the lock of") >= object->lock->dc)
            {
                objects.unlock(id);
                result = objects.interact(id, keys);
            }
            else
                say(name + " stays locked.");
        }
        else if (result == yh::Interaction::Locked)
            say(name + " is locked. It needs a key.");
        if (wasLocked && !object->locked())
            say(sheet.name + " unlocks " + name + ".");
        if (result == yh::Interaction::Opened || result == yh::Interaction::Closed)
            say(sheet.name + (result == yh::Interaction::Opened ? " opens " : " closes ") + name + ".");
        else if (result == yh::Interaction::Activated)
            say(sheet.name + (object->has("lever") ? " pulls " : " uses ") + name + ".");
        used = result == yh::Interaction::Opened || result == yh::Interaction::Closed || result == yh::Interaction::Activated;
    }
    // Story flags named on the object ("flag:gate-open") are set the first time it is used.
    if (used && (object = objects.get(id)))
    {
        std::vector<std::string> flags;
        for (const std::string& tag : object->tags)
            if (tag.starts_with("flag:") && tag.size() > 5)
                flags.push_back(tag.substr(5));
        if (!flags.empty())
            setFlags(flags);
    }
    objectsChanged();
    syncLog();
    if (fighting() && currentCreature() == hero)
        computeReach(hero);
}

void World::watchTraps()
{
    if (!chapter_ || remote_ || inCutscene_)
        return;
    const yh::Objects& objects = map().objects();
    for (size_t h = 0; h < heroCount_; h++)
    {
        if (creatures_[h].sheet.down() || tokens_.tokens[h].floor == dead)
            continue;
        const yh::Cell at = cellOf(h);
        const yh::Rect standing{at.x * GameMap::cellSize + 1, at.y * GameMap::cellSize + 1, GameMap::cellSize - 2, GameMap::cellSize - 2};
        if (const std::vector<yh::ObjectId> under = objects.trapsIn(standing, 0); !under.empty())
        {
            act("trap", nlohmann::json{{"object", under.front()}, {"hero", h}}.dump());
            return; // one at a time: the next frame sees what is left
        }
        // A hidden trap close by is found if the hero's passive score beats it. Each pair is only
        // looked at once, since nothing about the score changes by standing there longer.
        for (const auto& [id, object] : objects.all())
        {
            if (!object.armedTrap() || object.trap->found || trapsLookedAt_.contains({h, id}))
                continue;
            const yh::Cell trapAt = map().cellOf(object);
            if (std::max(std::abs(trapAt.x - at.x), std::abs(trapAt.y - at.y)) > map().trapSpotRange()
                || !yh::lineOfSight(grid_.center(at), grid_.center(trapAt), map().walls()))
                continue;
            trapsLookedAt_.insert({h, id});
            const std::string skill = checkWith(rules_, object.trap->detectSkill, "perception");
            if (rules_.passiveBase + creatures_[h].sheet.checkModifier(rules_, skill) >= object.trap->detectDc)
            {
                act("trap", nlohmann::json{{"object", id}, {"hero", h}, {"spot", true}}.dump());
                return;
            }
        }
    }
}

void World::objectsChanged()
{
    map().refreshWalls();
    std::vector<yh::Light> fixed;
    for (const GameMap::Light& l : map().lights())
        fixed.push_back({l.position, l.radius, l.color});
    lightLevels_.setFixed(fixed, map().walls());
}
