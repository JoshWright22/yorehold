// Fights: starting and ending them, turns, strikes, and the turns of the creatures the game plays.

#include "World.h"

#include <yorehold/framework/animation/Cutscene.h>

#include <algorithm>
#include <cstdio>
#include <queue>

namespace
{

// "{xp}" in chapter text becomes the number.
std::string fillXp(std::string text, int xp)
{
    for (size_t at; (at = text.find("{xp}")) != std::string::npos;)
        text.replace(at, 4, std::to_string(xp));
    return text;
}

}

void World::syncLog()
{
    if (!encounter_)
        return;
    const std::vector<std::string>& lines = encounter_->log();
    for (; encounterLogShown_ < lines.size(); encounterLogShown_++)
        say(lines[encounterLogShown_]);
}

void World::startCombat(int group, std::optional<size_t> only, bool surprise)
{
    raise("fightStart");
    for (size_t i = 0; i < heroCount_; i++)
    {
        setSneaking(i, false); // whatever the ruleset says about Hidden, nobody sneaks through a fight yet
        sneak_[i].reset();
    }
    // Everyone stops on a square of their own.
    std::vector<yh::Cell> taken;
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        yh::Token& token = tokens_.tokens[i];
        token.path.clear();
        if (token.floor == dead)
            continue;
        yh::Cell spot = grid_.cellAt(token.position);
        for (int radius = 0; radius < 4; radius++)
        {
            bool found = false;
            for (int dy = -radius; dy <= radius && !found; dy++)
                for (int dx = -radius; dx <= radius && !found; dx++)
                {
                    const yh::Cell c{spot.x + dx, spot.y + dy};
                    if (map().walkable(c) && std::find(taken.begin(), taken.end(), c) == taken.end())
                    {
                        spot = c;
                        found = true;
                    }
                }
            if (found)
                break;
        }
        taken.push_back(spot);
        token.position = grid_.center(spot);
    }

    encounter_ = std::make_unique<yh::Encounter>(rules_, seed_ * 7919 + static_cast<uint64_t>(++fights_));
    encounterLogShown_ = 0;
    turnBlockShown_ = 0;
    sideAtStart_[0] = sideAtStart_[1] = 0;
    hadLeader_[0] = hadLeader_[1] = false;
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        Creature& c = creatures_[i];
        c.fleeing = false;
        c.breakAs.clear();
        c.sheet.syncDeath(rules_);
        if (c.sheet.down() && !(rules_.death.enabled && c.sheet.death.saves && !c.sheet.death.dead))
            continue;
        if (c.team == 0)
            encounter_->add(c.sheet, 0);
        else if (c.group == group && c.team == 1 && (!only || *only == i))
        {
            c.awake = true;
            encounter_->add(c.sheet, 1);
        }
        else
            continue;
        sideAtStart_[c.team]++;
        hadLeader_[c.team] |= aiFor(i).leader;
    }

    if (group < static_cast<int>(chapter_->encounters.size()) && !chapter_->encounters[group].text.empty())
        say(chapter_->encounters[group].text);
    tokens_.settings.inCombat = true;
    if (surprise)
    {
        say("The party strikes from hiding!");
        encounter_->surprise(1);
    }
    encounter_->start();
    syncLog();
    emit({Event::Kind::Banner, "Combat", {}, FloatKind::Miss, 1.5});
    beginTurn();
}

void World::endCombat()
{
    if (pendingWipe_) return;
    for (Creature& creature : creatures_)
        creature.readiedAction.clear();
    tokens_.settings.inCombat = false;
    tokens_.settings.activeTurn.reset();
    reach_.clear();
    pendingAttack_.reset();
    for (yh::Token& token : tokens_.tokens)
        token.selected = false;
    pendingStep_.reset();
    raise("fightEnd");
    fallenConditions();

    if (encounter_->winningTeam() != 0)
    {
        pendingWipe_ = true;
        emit({Event::Kind::Banner, chapter_->defeatText, {}, FloatKind::Miss, 2});
        say(chapter_->defeatText);
        if (!chapter_->wipeCutscene.empty())
        {
            if (const auto text = chapterFiles_.readText(chapter_->wipeCutscene))
            {
                emit({Event::Kind::Ending, *text});
                inCutscene_ = true;
            }
            else say("Wipe cutscene is missing; returning to the autosave.");
        }
        return;
    }

    // Healing after a win, as the ruleset says: revive the downed, then any victory recovery.
    restRandom_ = nextRandom(0x5eedull);
    for (size_t i = 0; i < heroCount_; i++)
    {
        Creature& c = creatures_[i];
        if (c.sheet.down() && !c.sheet.death.dead && rules_.reviveAfterVictory > 0)
        {
            c.sheet.hp = std::min(rules_.reviveAfterVictory, c.sheet.maxHp());
            say(c.sheet.name + " gets back up with " + std::to_string(c.sheet.hp) + " HP.");
        }
        if (const int healed = c.sheet.recover(rules_, rules_.afterVictory, restRandom_); healed > 0)
            say(c.sheet.name + " recovers " + std::to_string(healed) + " HP.");
        if (!c.sheet.down())
            tokens_.tokens[i].floor = 0;
        c.sheet.addXp(rules_, chapter_->xpPerVictory);
        gainLevels(i);
    }
    fallenConditions(); // the revived are no longer Downed
    selectOwnHero();
    say(fillXp(chapter_->victoryText, chapter_->xpPerVictory));

    // Every encounter with nobody left standing sets its story flags.
    std::vector<std::string> won;
    for (size_t group = 0; group < chapter_->encounters.size(); group++)
    {
        const bool beaten = std::none_of(creatures_.begin() + heroCount_, creatures_.end(),
            [&](const Creature& c) { return c.group == static_cast<int>(group) && !c.sheet.down() && !c.surrendered; });
        if (beaten)
            won.insert(won.end(), chapter_->encounters[group].set.begin(), chapter_->encounters[group].set.end());
    }
    setFlags(won);

    if (chapterCleared())
    {
        playEnding();
        return;
    }
    emit({Event::Kind::Banner, "Victory", {}, FloatKind::Miss, 2});
    if (!rules_.rests.empty() && restsLeft(rules_.rests.front()) != 0)
        say("Hurt? Rest (R) before pushing on.");
    requestSave();
}

void World::raise(std::string_view event)
{
    for (Creature& c : creatures_)
        c.sheet.conditionEvent(rules_, event);
}

void World::fallenConditions()
{
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        Creature& c = creatures_[i];
        if (rules_.death.enabled)
        {
            if (!c.fled) c.sheet.syncDeath(rules_);
            if (c.sheet.down()) tokens_.tokens[i].floor = dead;
            else if (tokens_.tokens[i].floor == dead) tokens_.tokens[i].floor = 0;
            continue;
        }
        const char* fallen = i < heroCount_ ? downedCondition : deadCondition;
        if (c.sheet.down())
        {
            // Someone who got away or was let go is out of the adventure, not lying in it.
            if (!c.fled && !c.sheet.hasCondition(fallen) && rules_.condition(fallen))
                c.sheet.addCondition(rules_, fallen);
        }
        else if (c.sheet.hasCondition(fallen))
        {
            c.sheet.conditionEvent(rules_, "healed");
            c.sheet.removeCondition(fallen); // whatever its file says ends it, nobody on their feet is down
        }
    }
}

void World::playEnding()
{
    std::string error = "not found";
    const std::string& path = chapter_->clearedCutscene;
    const std::optional<std::string> text = path.empty() ? std::nullopt : chapterFiles_.readText(path);
    if (!text || !yh::Cutscene::fromJson(*text, &error))
    {
        // No (working) cutscene: a plain banner and Play again.
        if (!path.empty())
            say("Ending cutscene " + path + ": " + error);
        emit({Event::Kind::Banner, chapter_->clearedText, {}, FloatKind::Miss, 1e9});
        say(chapter_->clearedText);
        return;
    }
    emit({Event::Kind::Ending, *text});
    inCutscene_ = true;
}

void World::endCutscene()
{
    inCutscene_ = false;
}

void World::turnHostile(size_t creature)
{
    Creature& them = creatures_[creature];
    say(them.sheet.name + (them.surrendered ? " takes up arms again!" : " fights back!"));
    them.team = 1;
    them.surrendered = false;
    tokens_.tokens[creature].owner = enemyOwner;
    if (them.npc >= 0)
        setFlags(chapter_->npcs[them.npc].attacked);
    talk_.reset();
    pendingTalk_.reset();
    // Only them: the rest of their group is down, gone, or will wake when they're seen.
    them.awake = false;
    startCombat(them.group, creature);
}

void World::beginTurn()
{
    fallenConditions();
    const std::optional<size_t> current = currentCreature();
    if (!current)
        return;
    if (turnBlockShown_ != encounter_->blockSerial())
    {
        turnBlockShown_ = encounter_->blockSerial();
        for (size_t i = encounter_->blockFirst(); i < encounter_->blockEnd(); i++)
            for (Creature& c : creatures_)
                if (&c.sheet == encounter_->order()[i].character && !encounter_->order()[i].turnDone)
                    c.readiedAction.clear();
    }
    tokens_.settings.activeTurn = *current;
    pendingAttack_.reset();
    enemyStep_ = EnemyStep::Think;
    enemyTimer_ = 0;
    enemyTarget_.reset();
    if (creatures_[*current].team == 0)
    {
        for (size_t i = 0; i < tokens_.tokens.size(); i++)
            tokens_.tokens[i].selected = i == *current;
        computeReach(*current);
    }
    else
        reach_.clear();
    emit({Event::Kind::Camera, {}, tokens_.tokens[*current].position});
}

bool World::canChooseTurn(size_t creature) const
{
    const auto index = orderIndex(creature);
    const auto current = currentCreature();
    return index && current && !inCutscene_ && !pendingMovement_ && !pendingAttack_
        && tokens_.tokens[*current].path.empty() && encounter_->canSelectTurn(*index);
}

void World::endTurn()
{
    if (const std::optional<size_t> current = currentCreature())
    {
        yh::Token& token = tokens_.tokens[*current];
        if (!token.path.empty())
            token.position = token.path.back();
        token.path.clear();
    }
    encounter_->nextTurn();
    syncLog();
    if (encounter_->finished())
        endCombat();
    else
        beginTurn();
}

void World::tryAttack(size_t target, std::string_view with)
{
    const size_t me = *currentCreature();
    const yh::ActionDefinition* action = findAction(with);
    if (!action || !canUse(me, *action))
    {
        say(creatures_[me].sheet.name + " doesn't have the actions left to attack. End the turn (Space).");
        return;
    }
    if (inRange(me, *action, target))
    {
        use(with, target);
        return;
    }

    // Walk to the cheapest reachable square the action reaches the target from, then swing.
    const yh::Cell goal = cellOf(target);
    const float range = static_cast<float>(action->range) + 0.01f;
    std::optional<yh::Cell> best;
    float bestCost = 0;
    for (const auto& [c, cost] : reach_)
    {
        if (grid_.distance(c, goal) <= range && (!best || cost < bestCost))
        {
            best = c;
            bestCost = cost;
        }
    }
    if (!best)
    {
        say(creatures_[target].sheet.name + " is out of reach this turn.");
        return;
    }
    pendingAttack_ = target;
    pendingAction_ = std::string(with);
    pendingStep_ = *best;
    act("step", nlohmann::json{{"at", {best->x, best->y}}}.dump());
}

bool World::swingReady() const
{
    const std::optional<size_t> me = currentCreature();
    return me && pendingAttack_ && pendingStep_ && tokens_.tokens[*me].path.empty() && cellOf(*me) == *pendingStep_;
}

void World::swingIfReady()
{
    // Walked up to swing at someone: swing once the step has landed.
    if (!swingReady())
        return;
    const size_t me = *currentCreature();
    const size_t target = *pendingAttack_;
    pendingAttack_.reset();
    pendingStep_.reset();
    const yh::ActionDefinition* action = findAction(pendingAction_);
    if (action && inRange(me, *action, target))
        use(pendingAction_, target);
}

void World::updateEnemyTurn(double deltaSeconds)
{
    const size_t me = *currentCreature();
    yh::Token& token = tokens_.tokens[me];
    enemyTimer_ += deltaSeconds;

    switch (enemyStep_)
    {
    case EnemyStep::Think:
    {
        if (enemyTimer_ < 0.45)
            return;
        // Score everything it could do and take the best (heroes run this too in auto-play).
        std::vector<size_t> who;
        yh::Random random(seed_ ^ (static_cast<uint64_t>(fights_) << 40) ^ (static_cast<uint64_t>(encounter_->round()) << 20) ^ me);
        const yh::AiProfile profile = aiFor(me);
        // The first time its nerve goes, it settles how it reacts for the rest of the fight.
        if (creatures_[me].breakAs.empty())
        {
            std::vector<size_t> ignored;
            if (yh::wantsToFlee(profile, tacticalView(me, ignored)))
                creatures_[me].breakAs = yh::pickBreak(profile, random);
        }
        const yh::TacticalView view = tacticalView(me, who);
        std::vector<yh::TacticalChoice> considered;
        const yh::TacticalChoice choice = yh::decide(profile, view, grid_, random, &considered);
        using Kind = yh::TacticalChoice::Kind;
        if (aiNotes_)
        {
            char score[32];
            std::snprintf(score, sizeof score, "%.1f", choice.score);
            std::string line = "[AI " + profile.base + (profile.model != "utility" ? "/" + profile.model : std::string()) + "] "
                + creatures_[me].sheet.name + ": " + yh::kindName(choice.kind)
                + (choice.kind == Kind::Attack ? " " + creatures_[who[choice.target]].sheet.name : std::string())
                + " (" + score + ", best of " + std::to_string(considered.size()) + ")";
            if (!creatures_[me].breakAs.empty())
                line += ", broken: " + creatures_[me].breakAs;
            say(line);
        }

        enemyTimer_ = 0;
        enemyTarget_.reset();
        if (choice.kind == Kind::Surrender)
        {
            act("surrender");
            return;
        }
        if (choice.kind == Kind::Attack)
            enemyTarget_ = who[choice.target];
        if ((choice.kind == Kind::Flee || choice.kind == Kind::Alarm) && !creatures_[me].fleeing)
            act("flee", nlohmann::json{{"as", choice.kind == Kind::Alarm ? "alarm" : "flee"}}.dump());
        if (choice.dash)
            use(strideAction); // recomputes reach_
        if (choice.cell != standing_)
            act("step", nlohmann::json{{"at", {choice.cell.x, choice.cell.y}}}.dump());
        if (currentCreature() != me)
            return;
        enemyStep_ = EnemyStep::Walk;
        return;
    }
    case EnemyStep::Walk:
        if (!token.path.empty())
            return;
        enemyTimer_ = 0;
        enemyStep_ = enemyTarget_ && adjacent(me, *enemyTarget_) && canUse(me, strikeAction) ? EnemyStep::Strike : EnemyStep::Wait;
        return;
    case EnemyStep::Strike:
        if (enemyTimer_ < 0.25)
            return;
        enemyTimer_ = 0;
        enemyStep_ = EnemyStep::Wait;
        use(strikeAction, *enemyTarget_);
        // Actions to spare and the target still up: hit it again.
        if (encounter_ && !encounter_->finished() && currentCreature() == me && canUse(me, strikeAction)
            && !creatures_[*enemyTarget_].sheet.down() && orderIndex(*enemyTarget_))
            enemyStep_ = EnemyStep::Strike;
        return;
    case EnemyStep::Wait:
        if (enemyTimer_ < 0.6)
            return;
        // Running for help and close enough to shout: the allies it reached join the fight.
        if (creatures_[me].fleeing && creatures_[me].breakAs == "alarm")
        {
            if (const std::optional<int> group = sleepingGroupNear(me, aiFor(me).alarmReach))
            {
                act("alarm", nlohmann::json{{"group", *group}}.dump());
                use(endTurnAction);
                return;
            }
        }
        // Running, far enough from everyone and out of their sight (or walled off from them): it's gone.
        // While the party can still see it they get a chance to chase it down.
        if (creatures_[me].fleeing && !(creatures_[me].breakAs == "alarm" && sleepingGroupNear(me, 1e6f)))
        {
            const yh::CellCosts away = distanceToFoes(creatures_[me].team);
            const auto distance = away.find(cellOf(me));
            const bool watched = creatures_[me].team != 0 && fog_.state(0, 0, cellOf(me)) == yh::FogState::Visible;
            if (distance == away.end() || (distance->second >= aiFor(me).escapeAt && !watched))
            {
                act("escape");
                return;
            }
        }
        use(endTurnAction);
        return;
    }
}

void World::computeReach(size_t mover, int extra)
{
    reach_.clear();
    standing_ = cellOf(mover);
    if (creatures_[mover].sheet.hasFlag(rules_, "cantMove"))
    {
        reach_[standing_] = 0;
        return;
    }
    const float budget = static_cast<float>(encounter_->current().budget.movementLeft + extra) + 0.01f;
    auto open = [&](yh::Cell c) { return walkable(c) && !occupied(c, mover); };

    using Entry = std::pair<float, yh::Cell>;
    auto later = [](const Entry& a, const Entry& b) { return a.first > b.first; };
    std::priority_queue<Entry, std::vector<Entry>, decltype(later)> queue(later);
    reach_[standing_] = 0;
    queue.push({0.0f, standing_});
    std::vector<yh::Cell> neighbours;
    while (!queue.empty())
    {
        const auto [cost, c] = queue.top();
        queue.pop();
        if (cost > reach_[c])
            continue;
        grid_.neighbours(c, neighbours);
        for (const yh::Cell next : neighbours)
        {
            if (!open(next))
                continue;
            // Same rule as findPath: no cutting corners past walls or creatures.
            if (next.x != c.x && next.y != c.y && (!open({next.x, c.y}) || !open({c.x, next.y})))
                continue;
            const float nextCost = cost + grid_.stepCost(c, next, 0);
            if (nextCost > budget)
                continue;
            const auto known = reach_.find(next);
            if (known == reach_.end() || nextCost < known->second)
            {
                reach_[next] = nextCost;
                queue.push({nextCost, next});
            }
        }
    }
}
