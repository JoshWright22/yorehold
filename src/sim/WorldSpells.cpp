// Spells: where their areas fall, casting them, and concentration.

#include "World.h"

#include <yorehold/framework/graphics/Lighting.h>

#include <algorithm>
#include <map>

const yh::SpellDefinition* World::findSpell(std::string_view id) const
{
    return chapter_ ? chapter_->compendium.spell(id) : nullptr;
}

yh::AreaTemplate World::areaOf(size_t creature, const yh::ActionDefinition& action, yh::Cell aim) const
{
    if (!action.area || creature >= creatures_.size())
        return {};
    return action.area->place(grid_, grid_.center(cellOf(creature)), grid_.center(aim));
}

std::vector<size_t> World::creaturesIn(size_t me, const yh::ActionDefinition& action, yh::Cell aim) const
{
    std::vector<size_t> inside;
    if (!action.area || me >= creatures_.size())
        return inside;
    const yh::AreaTemplate area = areaOf(me, action, aim);
    const std::vector<yh::Cell> cells = area.cells(grid_);
    const yh::Cell origin = grid_.cellAt(area.origin);
    const bool fight = fighting();
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        const Creature& c = creatures_[i];
        if (i == me && action.area->directed())
            continue; // a cone or line starts at the doer's feet
        // In a fight it is whoever is in it; between fights only the party can be reached.
        if (fight)
        {
            const std::optional<size_t> index = orderIndex(i);
            if (!index || encounter_->order()[*index].out)
                continue;
        }
        else if (i >= heroCount_)
            continue;
        if (c.fled || c.sheet.death.dead || c.sheet.hasFlag(rules_, "dead") || (c.sheet.down() && !action.allowsDowned))
            continue;
        const bool sameSide = c.team == creatures_[me].team;
        if ((action.side == yh::ActionDefinition::Side::Enemy && sameSide) || (action.side == yh::ActionDefinition::Side::Ally && !sameSide))
            continue;
        const yh::Cell at = cellOf(i);
        if (std::find(cells.begin(), cells.end(), at) == cells.end())
            continue;
        // Walls stop it: there must be a clear line from where the area starts.
        if (at != origin && !yh::lineOfSight(area.origin, grid_.center(at), map().walls()))
            continue;
        inside.push_back(i);
    }
    return inside;
}

bool World::validAim(size_t me, const yh::ActionDefinition& action, yh::Cell at, std::string* why) const
{
    if (why) why->clear();
    auto refuse = [&](const char* text) { if (why) *why = text; return false; };
    if (action.target != yh::ActionDefinition::Target::Point || !action.area || me >= creatures_.size())
        return false;
    if (at.x < 0 || at.y < 0 || at.x >= map().width() || at.y >= map().height())
        return refuse("That is off the map.");
    const yh::Cell here = cellOf(me);
    if (action.area->directed())
        return at != here || refuse("Aim it away from yourself.");
    if (grid_.distance(here, at) > static_cast<float>(action.range) + 0.01f)
        return refuse("That spot is out of range.");
    if (at != here && !yh::lineOfSight(grid_.center(here), grid_.center(at), map().walls()))
        return refuse("There is no clear line to that spot.");
    return true;
}

bool World::canCast(size_t hero, std::string_view id, size_t target, std::string* why) const
{
    if (why) why->clear();
    auto refuse = [&](const char* text) { if (why) *why = text; return false; };
    const yh::SpellDefinition* spell = findSpell(id);
    if (!spell || hero >= heroCount_ || target >= creatures_.size() || fighting() || talk_ || inCutscene_ || partyDown())
        return refuse("That spell cannot be cast now.");
    const yh::Character& sheet = creatures_[hero].sheet;
    if (std::find(sheet.spells.begin(), sheet.spells.end(), spell->id()) == sheet.spells.end())
        return refuse("They do not know that spell.");
    if (sheet.down() || sheet.hasFlag(rules_, "cantAct") || !tokens_.tokens[hero].path.empty())
        return refuse("Wait until that hero can act.");
    const yh::ActionDefinition& action = spell->action;
    // Spells that harm or are aimed at the map need a fight, so damage cannot bypass initiative.
    const bool aimed = action.target != yh::ActionDefinition::Target::Self || action.area;
    if ((action.side == yh::ActionDefinition::Side::Enemy && aimed) || action.target == yh::ActionDefinition::Target::Point || target >= heroCount_)
        return refuse("Cast that during a fight.");
    if (std::string reason; !yh::canCast(sheet, *spell, spellRules(), &reason))
    {
        if (why) *why = spell->name() + " " + reason + ".";
        return false;
    }
    if (action.target == yh::ActionDefinition::Target::Self)
        return target == hero || refuse("Cast that on yourself.");
    const yh::Character& subject = creatures_[target].sheet;
    if (subject.death.dead || subject.hasFlag(rules_, "dead") || (subject.down() && !action.allowsDowned))
        return refuse("That spell cannot help them.");
    if (target != hero && (!inRange(hero, action, target)
        || !yh::lineOfSight(grid_.center(cellOf(hero)), grid_.center(cellOf(target)), map().walls())))
        return refuse("Stand within clear reach of the target.");
    return true;
}

void World::castSpell(size_t caster, const yh::SpellDefinition& spell, std::optional<size_t> target, std::optional<yh::Cell> at, int slot)
{
    yh::spendSlot(creatures_[caster].sheet, spellRules(), slot);
    say(creatures_[caster].sheet.name + " casts " + spell.name()
        + (slot > spell.level ? " from a level " + std::to_string(slot) + " slot." : "."));
    // One spell at a time: a new one that needs concentration ends the old.
    if (spell.concentration)
        endConcentration(caster, "to cast another");
    const yh::EffectResult result = runActionEffect(caster, spell.action, target, at, slot);
    if (!spell.concentration)
        return;
    Creature& c = creatures_[caster];
    c.concentration = yh::Concentration::begin(spell.id(), result);
    const auto sheets = [this](yh::EffectActor who) -> yh::Character* {
        return who >= 0 && static_cast<size_t>(who) < creatures_.size() ? &creatures_[static_cast<size_t>(who)].sheet : nullptr;
    };
    // Nothing took hold (everyone saved), or the casting dropped the caster: nothing to hold.
    if (c.concentration.tidy(sheets) && !(c.sheet.down() && spellRules().endsWhenDown))
        say(c.sheet.name + " concentrates on " + spell.name() + ".");
    else
        c.concentration = {};
}

void World::endConcentration(size_t creature, std::string_view why)
{
    Creature& c = creatures_[creature];
    if (!c.concentration.active())
        return;
    const yh::SpellDefinition* spell = findSpell(c.concentration.spell);
    const std::string name = spell ? spell->name() : c.concentration.spell;
    const std::vector<yh::Concentration::Hold> removed = c.concentration.end([this](yh::EffectActor who) -> yh::Character* {
        return who >= 0 && static_cast<size_t>(who) < creatures_.size() ? &creatures_[static_cast<size_t>(who)].sheet : nullptr;
    });
    say(c.sheet.name + " stops concentrating on " + name + (why.empty() ? std::string() : " (" + std::string(why) + ")") + ".");
    for (const yh::Concentration::Hold& hold : removed)
    {
        // Modifiers have no name of their own; conditions are told as they are elsewhere.
        const yh::ConditionDefinition* condition = rules_.condition(hold.id);
        if (condition)
            say(creatures_[static_cast<size_t>(hold.who)].sheet.name + " is no longer " + condition->name);
    }
}

void World::concentrationChecks(const yh::EffectResult& result, yh::Random& random)
{
    std::map<size_t, int> hurt; // one check for all the damage an effect did to someone
    for (const yh::EffectEvent& event : result.events)
        if (event.kind == yh::EffectEvent::Kind::Damage && event.amount > 0 && event.who >= 0 && static_cast<size_t>(event.who) < creatures_.size())
            hurt[static_cast<size_t>(event.who)] += event.amount;
    for (const auto& [who, amount] : hurt)
    {
        Creature& c = creatures_[who];
        if (!c.concentration.active() || c.sheet.down())
            continue; // someone who fell is dealt with by tidyConcentration
        const yh::ConcentrationCheck check = yh::concentrationCheck(c.sheet, rules_, spellRules(), amount, random);
        if (check.rolled)
            say(c.sheet.name + " holds concentration (" + spellRules().saveAbility + ", DC " + std::to_string(check.dc) + "): "
                + check.roll.describe() + (check.kept ? " - held" : " - lost"));
        if (!check.kept)
            endConcentration(who, "hurt");
    }
}

void World::tidyConcentration()
{
    if (!chapter_)
        return;
    const auto sheets = [this](yh::EffectActor who) -> yh::Character* {
        return who >= 0 && static_cast<size_t>(who) < creatures_.size() ? &creatures_[static_cast<size_t>(who)].sheet : nullptr;
    };
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        Creature& c = creatures_[i];
        if (!c.concentration.active())
            continue;
        if (c.sheet.down() && spellRules().endsWhenDown)
        {
            endConcentration(i, "down");
            continue;
        }
        const yh::SpellDefinition* spell = findSpell(c.concentration.spell);
        const std::string name = spell ? spell->name() : c.concentration.spell;
        if (!c.concentration.tidy(sheets))
            say(c.sheet.name + "'s " + name + " has run its course.");
    }
}

void World::endAllConcentration()
{
    for (size_t i = 0; i < creatures_.size(); i++)
        endConcentration(i, "");
}
