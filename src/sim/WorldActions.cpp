// Actions: what the ruleset's files let a creature do on its turn, and carrying one out.

#include "World.h"

#include <algorithm>
#include <yorehold/framework/graphics/Lighting.h>

// What effects ask of the world. Sheets are the creatures' own; movement left this turn is a
// resource of the fight, and story flags are the chapter's.
class World::EffectsHost : public yh::EffectHost
{
public:
    explicit EffectsHost(World& world) : world_(world) {}

    yh::Character* sheet(yh::EffectActor who) override
    {
        return who >= 0 && static_cast<size_t>(who) < world_.creatures_.size() ? &world_.creatures_[static_cast<size_t>(who)].sheet : nullptr;
    }

    // A positional condition applies for this evaluation and never remains on a saved sheet.
    int armorClass(yh::EffectActor who, const yh::EffectContext& context) override
    {
        if (!sheet(who) || !sheet(context.self)) return yh::EffectHost::armorClass(who, context);
        const auto* action = world_.findAction(context.source);
        return world_.attackArmorClass(static_cast<size_t>(context.self), static_cast<size_t>(who), action && action->range > 1);
    }

    bool hasFlag(yh::EffectActor who, std::string_view flag, const yh::EffectContext& context) override
    {
        if (yh::EffectHost::hasFlag(who, flag, context)) return true;
        const auto* condition = world_.rules_.condition(world_.chapter_->positioning.flankingCondition);
        return sheet(who) && condition && condition->hasFlag(flag) && world_.isFlanked(static_cast<size_t>(who));
    }

    // Allies and enemies are whoever still stands in the fight, on the doer's side or another.
    // Areas come with spells.
    std::vector<yh::EffectActor> group(std::string_view which, const yh::EffectContext& context) override
    {
        std::vector<yh::EffectActor> out;
        if (which == "area" || !world_.encounter_ || !sheet(context.self))
            return out;
        const int team = world_.creatures_[static_cast<size_t>(context.self)].team;
        for (size_t i = 0; i < world_.creatures_.size(); i++)
        {
            const std::optional<size_t> index = world_.orderIndex(i);
            if (index && world_.encounter_->order()[*index].standing() && (world_.creatures_[i].team == team) == (which == "allies"))
                out.push_back(static_cast<yh::EffectActor>(i));
        }
        return out;
    }

    bool resource(yh::EffectActor who, std::string_view id, int change, const yh::EffectContext& context) override
    {
        if (id != "movement")
            return yh::EffectHost::resource(who, id, change, context);
        // Squares the creature whose turn it is may still move.
        const std::optional<size_t> current = world_.currentCreature();
        if (!current || static_cast<size_t>(who) != *current)
            return false;
        int& left = world_.encounter_->current().budget.movementLeft;
        left = std::max(0, left + change);
        return true;
    }

    bool flag(std::string_view name, bool set, const yh::EffectContext&) override
    {
        if (set)
            world_.setFlags({std::string(name)});
        else if (const std::set<std::string> before = world_.flags_; world_.flags_.erase(std::string(name)) > 0)
            world_.flagsChanged(before);
        return true;
    }

    bool move(yh::EffectActor who, std::string_view how, int squares, const yh::EffectContext& context) override
    {
        if (!sheet(who) || !sheet(context.self) || (how != "push" && how != "pull") || who == context.self)
            return false;
        const size_t creature = static_cast<size_t>(who);
        const yh::Cell from = world_.cellOf(static_cast<size_t>(context.self));
        yh::Cell at = world_.cellOf(creature);
        auto sign = [](int n) { return (n > 0) - (n < 0); };
        const int direction = how == "push" ? 1 : -1;
        const yh::Cell delta{sign(at.x - from.x) * direction, sign(at.y - from.y) * direction};
        bool moved = false;
        for (int i = 0; i < squares; i++)
        {
            const yh::Cell to{at.x + delta.x, at.y + delta.y};
            if (to == from || !world_.walkable(to) || world_.occupied(to, creature)
                || !yh::lineOfSight(world_.grid_.center(at), world_.grid_.center(to), world_.map().walls()))
                break;
            at = to;
            moved = true;
        }
        if (moved)
        {
            yh::Token& token = world_.tokens_.tokens[creature];
            token.position = world_.grid_.center(at);
            token.path.clear();
            sheet(who)->conditionEvent(world_.rules_, "move");
        }
        return moved;
    }

private:
    World& world_;
};

const yh::ActionDefinition* World::findAction(std::string_view id) const
{
    return chapter_ ? yh::findAction(chapter_->actions, id) : nullptr;
}

std::vector<const yh::ActionDefinition*> World::actionsOf(size_t creature) const
{
    std::vector<const yh::ActionDefinition*> has;
    if (!chapter_ || creature >= creatures_.size())
        return has;
    // The ruleset's general actions. Classes, feats and items will add their own to this list.
    for (const yh::ActionDefinition& a : chapter_->actions)
        if (a.general)
            has.push_back(&a);
    return has;
}

int World::actionCost(size_t creature, const yh::ActionDefinition& action) const
{
    return action.costFor(creatures_[creature].sheet, rules_);
}

bool World::canUse(size_t creature, const yh::ActionDefinition& action, std::string* why) const
{
    if (why)
        why->clear();
    if (pendingMovement_ || currentCreature() != creature)
        return false;
    const std::vector<const yh::ActionDefinition*> has = actionsOf(creature);
    if (std::find(has.begin(), has.end(), &action) == has.end())
        return false;
    if (!encounter_->canAct(actionCost(creature, action)))
    {
        if (why)
            *why = "not enough actions left";
        return false;
    }
    return action.meets(creatures_[creature].sheet, rules_, why);
}

bool World::canUse(size_t creature, std::string_view id) const
{
    const yh::ActionDefinition* found = findAction(id);
    return found && canUse(creature, *found);
}

bool World::validTarget(size_t creature, const yh::ActionDefinition& action, size_t target) const
{
    if (action.target != yh::ActionDefinition::Target::Creature || creature >= creatures_.size() || target >= creatures_.size())
        return false;
    const std::optional<size_t> index = orderIndex(target);
    if (!index || encounter_->order()[*index].out || creatures_[target].sheet.hasFlag(rules_, "dead")
        || (creatures_[target].sheet.down() && !action.allowsDowned))
        return false;
    const bool sameSide = creatures_[target].team == creatures_[creature].team;
    if ((action.side == yh::ActionDefinition::Side::Enemy && sameSide) || (action.side == yh::ActionDefinition::Side::Ally && !sameSide))
        return false;
    if (!inRange(creature, action, target)) return false;
    if (chapter_->positioning.enabled)
        return (action.range <= 1 && !chapter_->positioning.coverAgainstMelee) || coverFrom(creature, target) != yh::Cover::Full;
    return action.range <= 1 || yh::lineOfSight(grid_.center(cellOf(creature)), grid_.center(cellOf(target)), map().walls());
}

bool World::inRange(size_t creature, const yh::ActionDefinition& action, size_t target) const
{
    return action.range <= 1 ? adjacent(creature, target)
        : grid_.distance(cellOf(creature), cellOf(target)) <= static_cast<float>(action.range) + 0.01f;
}

void World::use(std::string_view id, std::optional<size_t> target)
{
    nlohmann::json data{{"action", id}};
    if (target)
        data["target"] = *target;
    act("use", data.dump());
}

void World::perform(const yh::ActionDefinition& action, std::optional<size_t> target)
{
    const std::optional<size_t> current = currentCreature();
    if (!current)
        return;
    const size_t me = *current;
    creatures_[me].readiedAction = action.readies;
    encounter_->spendActions(actionCost(me, action));
    syncLog();
    if (!action.log.empty())
    {
        std::string line = action.log;
        for (size_t at; (at = line.find("{name}")) != std::string::npos;)
            line.replace(at, 6, creatures_[me].sheet.name);
        say(line);
    }

    runActionEffect(me, action, target);
    if (action.endsTurn && !encounter_->finished())
        endTurn();
    else if (encounter_->finished())
        endCombat();
    else if (currentCreature() == me)
        computeReach(me);
}

void World::runActionEffect(size_t me, const yh::ActionDefinition& action, std::optional<size_t> target)
{
    yh::EffectResult result;
    if (!action.effect.empty())
    {
        EffectsHost host(*this);
        yh::EffectContext context;
        context.rules = &rules_;
        context.random = &encounter_->random(); // the fight's own dice, the same on every machine
        context.self = static_cast<yh::EffectActor>(me);
        context.targets = {static_cast<yh::EffectActor>(target.value_or(me))};
        context.source = action.id;
        result = action.effect.run(host, context);
        narrate(result);
    }
    fallenConditions();
    // Healing can bring an ally back into the same encounter.
    for (size_t i = 0; i < creatures_.size(); i++)
        if (!creatures_[i].sheet.down() && tokens_.tokens[i].floor == dead)
            tokens_.tokens[i].floor = 0;
    for (const yh::EffectEvent& event : result.events)
    {
        // Whoever it dropped lies where they fell.
        const size_t who = static_cast<size_t>(event.who);
        if (event.kind != yh::EffectEvent::Kind::Damage || !event.dropped || !creatures_[who].sheet.down())
            continue;
        yh::Token& token = tokens_.tokens[who];
        token.floor = dead;
        token.selected = false;
        token.path.clear();
        if (creatures_[who].npc >= 0)
            setFlags(chapter_->npcs[creatures_[who].npc].killed);
    }

}

void World::narrate(const yh::EffectResult& result)
{
    using Kind = yh::EffectEvent::Kind;
    auto name = [&](yh::EffectActor who) {
        return who >= 0 && static_cast<size_t>(who) < creatures_.size() ? creatures_[static_cast<size_t>(who)].sheet.name : std::string("Someone");
    };
    auto conditionName = [&](const std::string& id) {
        const yh::ConditionDefinition* def = rules_.condition(id);
        return def ? def->name : id;
    };
    auto at = [&](yh::EffectActor who) { return tokens_.tokens[static_cast<size_t>(who)].position; };

    // An attack and the damage it does read as one line; what ended because of them follows it.
    std::string attackLine;
    yh::EffectActor attacked = -1;
    bool hitPending = false;
    std::vector<std::string> after;
    auto flush = [&] {
        if (!attackLine.empty())
            say(attackLine + (hitPending ? " - hit" : ""));
        attackLine.clear();
        hitPending = false;
        for (std::string& line : after)
            say(std::move(line));
        after.clear();
    };

    for (const yh::EffectEvent& e : result.events)
    {
        if (e.who < 0 || static_cast<size_t>(e.who) >= creatures_.size())
            continue;
        switch (e.kind)
        {
        case Kind::Attack:
            flush();
            attackLine = name(e.by) + " attacks " + name(e.who) + " (AC " + std::to_string(e.dc) + "): " + e.roll.describe();
            attacked = e.who;
            hitPending = e.success;
            if (!e.success)
            {
                attackLine += " - miss";
                emit({Event::Kind::Floater, "Miss", at(e.who), FloatKind::Miss});
            }
            break;
        case Kind::Damage:
        {
            const std::string fell = e.dropped ? ". " + name(e.who) + " goes down!" : "";
            if (hitPending && attacked == e.who)
            {
                attackLine += (e.critical ? " - CRITICAL HIT, " : " - hit, ") + e.roll.describe() + " damage" + fell;
                hitPending = false;
            }
            else
                after.push_back(name(e.who) + " takes " + std::to_string(e.amount) + (e.id.empty() || e.id == "untyped" ? "" : " " + e.id)
                    + " damage (" + e.roll.describe() + ")" + fell);
            emit({Event::Kind::Floater, (e.critical ? "Critical! " : "") + std::to_string(e.amount), at(e.who),
                e.critical ? FloatKind::Critical : FloatKind::Hit});
            break;
        }
        case Kind::Heal:
            after.push_back(name(e.who) + " recovers " + std::to_string(e.amount) + " HP.");
            emit({Event::Kind::Floater, "+" + std::to_string(e.amount), at(e.who), FloatKind::Heal});
            break;
        case Kind::TempHp:
            after.push_back(name(e.who) + " gains " + std::to_string(e.amount) + " temporary HP.");
            break;
        case Kind::Save:
            after.push_back(name(e.who) + " saves (" + e.id + ", DC " + std::to_string(e.dc) + "): " + e.roll.describe() + (e.success ? " - saved" : " - failed"));
            break;
        case Kind::Check:
            after.push_back(name(e.by) + " tries (" + e.id + ", DC " + std::to_string(e.dc) + "): " + e.roll.describe() + (e.success ? " - success" : " - failure"));
            break;
        case Kind::ConditionAdded:
            after.push_back(name(e.who) + " is " + conditionName(e.id) + (e.amount > 1 ? " " + std::to_string(e.amount) : ""));
            break;
        case Kind::ConditionRemoved:
        case Kind::ConditionEnded:
            after.push_back(name(e.who) + " is no longer " + conditionName(e.id));
            break;
        case Kind::Modifier:
        case Kind::Move:
        case Kind::Resource:
        case Kind::Summon:
        case Kind::Light:
        case Kind::Surface:
        case Kind::Flag:
        case Kind::Choice:
            break; // nothing to read: the action's own log line says what was done
        }
    }
    flush();
}
