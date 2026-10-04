#include "World.h"

#include <yorehold/framework/graphics/Lighting.h>

bool World::canConsume(size_t hero, size_t item, size_t target, std::string* why) const
{
    if (why) why->clear();
    auto refuse = [&](const char* text) { if (why) *why = text; return false; };
    if (!chapter_ || hero >= heroCount_ || target >= creatures_.size() || talk_ || inCutscene_ || pendingMovement_)
        return refuse("An item cannot be used now.");
    const auto& sheet = creatures_[hero].sheet;
    if (sheet.down() || sheet.hasFlag(rules_, "cantAct") || !tokens_.tokens[hero].path.empty())
        return refuse("Wait until that hero can act.");
    if (item >= sheet.inventory.size() || !sheet.inventory[item].use || sheet.inventory[item].quantity < 1 || sheet.inventory[item].equipped)
        return refuse("That item cannot be consumed.");
    const auto& action = *sheet.inventory[item].use;
    if (!action.meets(sheet, rules_, why) || !action.effect.check(rules_, why)) return false;
    if (fighting() && (currentCreature() != hero || !encounter_->canAct(action.cost)))
        return refuse("Using that item needs actions on this hero's turn.");
    if (action.target == yh::ActionDefinition::Target::Self)
        return target == hero || refuse("Use that item on yourself.");
    const auto& subject = creatures_[target];
    if (!inRange(hero, action, target)
        || !yh::lineOfSight(grid_.center(cellOf(hero)), grid_.center(cellOf(target)), map().walls()))
        return refuse("Stand within clear reach of the target.");
    if (subject.fled || subject.sheet.death.dead || subject.sheet.hasFlag(rules_, "dead") || (subject.sheet.down() && !action.allowsDowned))
        return refuse("That creature cannot receive this item.");
    if (fighting())
        return validTarget(hero, action, target) || refuse("That target is outside the item's reach or on the wrong side.");
    // Hostile items need an encounter, so damage cannot bypass initiative or encounter rewards.
    if (target >= heroCount_ || action.side == yh::ActionDefinition::Side::Enemy)
        return refuse("Use hostile items during a fight.");
    return (inRange(hero, action, target) && yh::lineOfSight(grid_.center(cellOf(hero)), grid_.center(cellOf(target)), map().walls()))
        || refuse("Stand within reach of the target.");
}

void World::consume(size_t hero, size_t item, size_t target)
{
    if (!canConsume(hero, item, target)) return;
    const yh::Item used = creatures_[hero].sheet.inventory[item];
    const bool inFight = fighting();
    if (inFight) encounter_->spendActions(used.use->cost);
    creatures_[hero].sheet.removeItem(item);
    say(creatures_[hero].sheet.name + " uses " + used.name + ".");
    runActionEffect(hero, *used.use, target);
    if (inFight && encounter_->finished()) endCombat();
    else if (inFight && currentCreature() == hero) computeReach(hero);
    if (!fighting()) requestSave();
}
