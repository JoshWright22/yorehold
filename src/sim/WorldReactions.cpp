#include "World.h"

#include <algorithm>

void World::react(bool take)
{
    if (reactionPrompt_)
        act("reaction", nlohmann::json{{"take", take}, {"creature", reactionPrompt_->creature}, {"offer", reactionPrompt_->id}}.dump());
}

void World::startMovement(size_t creature, std::vector<yh::Cell> path, bool prompts)
{
    pendingMovement_ = PendingMovement{creature, std::move(path), 1, 0, 0, 0, prompts};
    continueMovement();
}

void World::resolveReaction(bool take)
{
    if (!pendingReaction_) return;
    const PendingReaction offer = *pendingReaction_;
    pendingReaction_.reset();
    reactionPrompt_.reset();
    if (!take)
    {
        say(creatures_[offer.creature].sheet.name + " passes on " + offer.name + ".");
        return;
    }
    const std::optional<size_t> index = orderIndex(offer.creature);
    const yh::ActionDefinition* action = findAction(offer.action);
    if (!index || !action || !encounter_->useReaction(*index)) return;
    if (offer.readied)
        creatures_[offer.creature].readiedAction.clear();
    say(creatures_[offer.creature].sheet.name + " takes " + offer.name + ".");
    runActionEffect(offer.creature, *action, action->target == yh::ActionDefinition::Target::Self ? offer.creature : offer.target);
}

void World::continueMovement()
{
    if (!pendingMovement_ || pendingReaction_) return;
    PendingMovement& move = *pendingMovement_;
    const size_t mover = move.creature;
    yh::Token& token = tokens_.tokens[mover];
    while (move.edge < move.path.size() && !creatures_[mover].sheet.down() && !encounter_->finished())
    {
        const yh::Cell from = move.path[move.edge - 1], to = move.path[move.edge];
        const auto phase = move.phase == 0 ? yh::ReactionDefinition::Trigger::LeavesReach : yh::ReactionDefinition::Trigger::EntersReach;
        while (move.nextCreature < creatures_.size())
        {
            const size_t reactor = move.nextCreature++;
            const std::optional<size_t> index = orderIndex(reactor);
            if (reactor == mover || creatures_[reactor].team == creatures_[mover].team || !index
                || !encounter_->order()[*index].standing() || !encounter_->order()[*index].budget.reaction
                || creatures_[reactor].sheet.hasFlag(rules_, "cantAct"))
                continue;
            for (const yh::ReactionDefinition& def : chapter_->reactions)
            {
                if (def.trigger != phase) continue;
                const std::string& id = def.readied ? creatures_[reactor].readiedAction : def.action;
                const yh::ActionDefinition* action = findAction(id);
                const yh::Cell at = cellOf(reactor);
                if (!action || !action->meets(creatures_[reactor].sheet, rules_)
                    || !def.matches(grid_.distance(at, from), grid_.distance(at, to), action->range))
                    continue;
                const size_t triggerAt = move.phase == 0 ? move.edge - 1 : move.edge;
                token.position = grid_.center(move.path[triggerAt]);
                if (action->target == yh::ActionDefinition::Target::Creature && !validTarget(reactor, *action, mover))
                    continue;
                pendingReaction_ = PendingReaction{reactor, mover, action->id, def.name, def.readied};
                if (move.prompts && reactor < heroCount_ && !autoPlay_)
                {
                    move.animateFrom = triggerAt;
                    reactionPrompt_ = ReactionPrompt{reactor, mover, def.name, def.promptSeconds, ++reactionSequence_};
                    return;
                }
                resolveReaction(true);
                break;
            }
            if (creatures_[mover].sheet.down() || encounter_->finished()) break;
        }
        if (creatures_[mover].sheet.down() || encounter_->finished()) break;
        move.nextCreature = 0;
        if (move.phase == 0)
            move.phase = 1;
        else
        {
            move.phase = 0;
            move.edge++;
        }
    }
    if (!creatures_[mover].sheet.down())
    {
        token.position = grid_.center(move.path.empty() ? cellOf(mover) : move.path[move.animateFrom]);
        for (size_t i = move.animateFrom + 1; i < move.path.size(); i++)
            token.path.push_back(grid_.center(move.path[i]));
    }
    pendingMovement_.reset();
    if (encounter_->finished())
        endCombat();
    else if (creatures_[mover].sheet.down())
        endTurn();
    else
        computeReach(mover);
}

void World::reactionTime(double seconds)
{
    if (!reactionPrompt_) return;
    reactionPrompt_->secondsLeft -= std::max(0.0, seconds);
    if (!remote_ && reactionPrompt_->secondsLeft <= 0)
        react(true);
}
