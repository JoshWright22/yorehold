// Between fights: walking, talking, resting, and auto-play's exploring.

#include "World.h"

#include <yorehold/framework/map/Pathfinding.h>

#include <algorithm>

void World::update(double deltaSeconds)
{
    walk(deltaSeconds);
    arrive();
    takeTurns(deltaSeconds, true, {});
}

void World::walk(double deltaSeconds)
{
    if (!chapter_)
        return;
    const bool fighting = encounter_ && !encounter_->finished();
    const yh::TokenController::Passable passable = [this](yh::Cell c) { return walkable(c); };
    // Each sneaking hero is slower, whoever plays them, so they walk the same on every machine.
    // The same goes for anyone carrying too much: slower, or going nowhere at all. In a fight the
    // squares they may move already say so.
    for (size_t i = 0; i < heroCount_; i++)
    {
        float pace = !fighting && creatures_[i].sneaking() ? chapter_->stealth.sneakSpeed : 1.0f;
        if (const int weighed = fighting ? 0 : creatures_[i].sheet.encumbrance(rules_))
            pace = weighed == 2 ? 0.0f : pace * rules_.encumberedSpeed;
        tokens_.tokens[i].pace = pace;
    }
    tokens_.advance(grid_, passable, deltaSeconds);
}

void World::arrive()
{
    // Walking over to talk: the conversation opens on arrival.
    if (!chapter_ || !pendingTalk_ || (encounter_ && !encounter_->finished()))
        return;
    const size_t leader = leaderIndex();
    if (!tokens_.tokens[leader].path.empty())
        return;
    const size_t who = *pendingTalk_;
    pendingTalk_.reset();
    if (talkable(who) && grid_.distance(cellOf(leader), cellOf(who)) <= 1.5f)
        act("talk", nlohmann::json{{"creature", who}}.dump());
    else
        say("Can't reach " + creatures_[who].sheet.name + " from here.");
}

void World::takeTurns(double deltaSeconds, bool heroesListen, const std::function<void()>& heroInput)
{
    if (!chapter_)
        return;
    // The host runs the enemies (and the heroes in auto-play); a hero's own player runs their turn.
    if (pendingWipe_)
    {
        if (!inCutscene_ && !remote_ && !wipeRequested_)
        {
            wipeRequested_ = true;
            act("wipe-return");
        }
        return;
    }
    if (pendingMovement_)
    {
        reactionTime(deltaSeconds);
        updateVisibility();
        return;
    }
    if (const std::optional<size_t> now = currentCreature())
    {
        if (creatures_[*now].team == 0 && !autoPlay_)
        {
            if (heroesListen && mine(*now))
            {
                if (swingReady())
                    swingIfReady();
                else if (heroInput)
                    heroInput();
            }
        }
        else if (!remote_)
            updateEnemyTurn(deltaSeconds);
    }
    else if (autoPlay_ && !partyDown() && !remote_)
        autoExplore();
    updateVisibility();
}

size_t World::leaderIndex() const
{
    for (size_t i = 0; i < heroCount_; i++)
        if (tokens_.tokens[i].selected && mine(i) && !creatures_[i].sheet.down())
            return i;
    for (size_t i = 0; i < heroCount_; i++)
        if (mine(i) && !creatures_[i].sheet.down())
            return i;
    for (size_t i = 0; i < heroCount_; i++)
        if (!creatures_[i].sheet.down())
            return i;
    return 0;
}

std::string World::dialogueFor(size_t creature) const
{
    const Creature& c = creatures_[creature];
    if (c.surrendered)
        return c.surrender;
    return c.npc >= 0 ? chapter_->npcs[c.npc].dialogue : std::string();
}

void World::walkToTalk(size_t creature)
{
    const size_t leader = leaderIndex();
    if (creatures_[leader].sheet.down())
        return;
    yh::Token& token = tokens_.tokens[leader];
    const yh::Cell from = cellOf(leader);
    const yh::Cell goal = cellOf(creature);
    token.path.clear();
    if (grid_.distance(from, goal) > 1.5f)
    {
        // The nearest open square next to them.
        std::vector<yh::Cell> best;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                const yh::Cell next{goal.x + dx, goal.y + dy};
                if ((dx || dy) && walkable(next))
                {
                    std::vector<yh::Cell> path = findPath(grid_, from, next, [this](yh::Cell c) { return walkable(c); });
                    if (!path.empty() && (best.empty() || path.size() < best.size()))
                        best = std::move(path);
                }
            }
        for (size_t i = 1; i < best.size(); i++)
            token.path.push_back(grid_.center(best[i]));
    }
    pendingTalk_ = creature;
}

void World::startTalk(size_t creature)
{
    const std::string name = creatures_[creature].sheet.name;
    const std::string path = dialogueFor(creature);
    if (path.empty())
    {
        say(name + " has nothing to say.");
        return;
    }
    std::string error = "not found";
    const std::optional<std::string> text = chapterFiles_.readText(path);
    std::optional<yh::Dialogue> dialogue = text ? yh::Dialogue::fromJson(*text, &error) : std::nullopt;
    if (!dialogue)
    {
        say(name + "'s dialogue " + path + ": " + error);
        return;
    }
    for (yh::Token& token : tokens_.tokens)
        token.path.clear();
    talkWith_ = creature;
    talk_ = std::make_unique<yh::DialogueSession>(std::move(*dialogue), flags_);
    emit({Event::Kind::Talk});
    // Node entry effects of the first line count too.
    if (talk_->flags() != flags_)
    {
        const std::set<std::string> before = flags_;
        flags_ = talk_->flags();
        flagsChanged(before);
    }
    dialogueActions();
}

// What a conversation can make happen beyond story flags ("do" in the dialogue file):
//   release  they leave for good (no body)
//   kill     they die where they stand
//   fight    they attack the party (again)
void World::dialogueActions()
{
    if (!talk_)
        return;
    const size_t who = talkWith_;
    Creature& c = creatures_[who];
    yh::Token& token = tokens_.tokens[who];
    for (const std::string& action : talk_->takeActions())
    {
        if (c.sheet.down())
            break;
        if (action == "release")
        {
            say(c.sheet.name + " leaves.");
            c.sheet.hp = 0;
            c.fled = true;
            token.floor = dead;
        }
        else if (action == "kill")
        {
            say(c.sheet.name + " is killed.");
            c.sheet.hp = 0;
            token.floor = dead;
            if (c.npc >= 0)
                setFlags(chapter_->npcs[c.npc].killed);
        }
        else if (action == "fight")
        {
            turnHostile(who);
            return;
        }
        else if (action == "recruit" && c.npc >= 0)
        {
            // Recruit an NPC companion if they pass the approval check
            if (canRecruitCompanion(static_cast<size_t>(c.npc)))
            {
                companionParty_.insert(chapter_->npcs[c.npc].id);
                say(c.sheet.name + " joins the party.");
            }
            else
            {
                say(c.sheet.name + " is not ready to join yet.");
            }
        }
    }
    fallenConditions();
}

void World::chooseReply(size_t index, size_t hero)
{
    if (!talk_)
        return;
    const std::vector<const yh::DialogueChoice*> choices = talk_->choices();
    if (talk_->finished() || choices.empty())
    {
        // The last line: any key or click ends it.
        talk_.reset();
        if (chapterCleared())
            playEnding();
        else
            requestSave();
        return;
    }
    if (index >= choices.size())
        return;
    const yh::DialogueChoice choice = *choices[index];
    const yh::Character& speaker = creatures_[hero].sheet;
    const std::optional<yh::DialogueResult> result = talk_->choose(choice.id, [&](std::string_view skill) {
        yh::Random dice = nextRandom(0x7a1cull);
        return speaker.rollCheck(rules_, skill, yh::Advantage::None, dice);
    });
    if (!result)
        return;
    say(speaker.name + ": " + choice.text);
    if (result->roll && choice.check)
        say(speaker.name + " rolls " + choice.check->skill + ": " + result->roll->describe() + " vs " +
            std::to_string(choice.check->difficulty) + (result->passed ? ", success" : ", failure"));
    const std::set<std::string> before = flags_;
    flags_ = talk_->flags();
    if (flags_ != before)
        flagsChanged(before);
    dialogueActions();
    if (!talk_)
        return; // it ended in a fight
    if (talk_->finished() && !talk_->current())
    {
        talk_.reset();
        if (chapterCleared())
            playEnding();
        else
            requestSave();
    }
}

void World::rest(const yh::RestDefinition& rest)
{
    if (restsLeft(rest) == 0 || (encounter_ && !encounter_->finished()) || partyDown())
        return;
    restsUsed_[rest.id]++;
    restRandom_ = nextRandom(0x5eedull);
    say("The party takes a " + (rest.name.empty() ? rest.id : rest.name) + ".");
    endAllConcentration(); // nobody holds a spell through a rest
    for (size_t i = 0; i < heroCount_; i++)
    {
        yh::Character& c = creatures_[i].sheet;
        // Spell slots and whatever else the rest's file names come back, to the living.
        if (!c.death.dead && c.restoreResources(rest.restores) > 0)
            say(c.name + " is ready to cast again.");
        // Prepared casters may choose their spells again (K) until the next fight.
        const std::vector<std::string>& after = spellRules().prepareAfter;
        if (!c.death.dead && !c.preparable.empty() && std::find(after.begin(), after.end(), rest.id) != after.end())
        {
            if (!creatures_[i].mayPrepare)
                say(c.name + " may prepare spells again (K).");
            creatures_[i].mayPrepare = true;
        }
        if (c.hp >= c.maxHp())
            continue;
        std::string detail;
        const int healed = c.recover(rules_, rest.recovery, restRandom_, &detail);
        if (healed <= 0)
            continue;
        tokens_.tokens[i].floor = 0; // revived
        say(c.name + " recovers " + std::to_string(healed) + " HP" + (detail.empty() ? "." : " (" + detail + ")."));
        emit({Event::Kind::Floater, "+" + std::to_string(healed), tokens_.tokens[i].position, FloatKind::Heal});
    }
    raise("rest");
    fallenConditions();
    if (const int left = restsLeft(rest); left >= 0)
        say(std::to_string(left) + " left.");
    requestSave();
}

bool World::canGo(size_t hero, yh::Cell to) const
{
    if (hero >= heroCount_ || creatures_[hero].sheet.down() || !walkable(to))
        return false;
    return !findPath(grid_, cellOf(hero), to, [this](yh::Cell c) { return walkable(c); }).empty();
}

void World::go(size_t hero, yh::Cell to)
{
    yh::Token& token = tokens_.tokens[hero];
    const std::vector<yh::Cell> path = findPath(grid_, cellOf(hero), to, [this](yh::Cell c) { return walkable(c); });
    token.path.clear();
    for (size_t i = 1; i < path.size(); i++)
        token.path.push_back(grid_.center(path[i]));
}

void World::autoExplore()
{
    // The leader heads for the nearest enemy still standing; the rest follow their links.
    const size_t leader = 0;
    const bool hurt = std::any_of(creatures_.begin(), creatures_.begin() + heroCount_,
        [](const Creature& c) { return !c.sheet.down() && c.sheet.hp * 2 < c.sheet.maxHp(); });
    // Use the first rest that still has uses (short before long).
    for (size_t i = 0; i < rules_.rests.size(); i++)
    {
        if (hurt && restsLeft(rules_.rests[i]) != 0)
        {
            act("rest", nlohmann::json{{"rest", i}}.dump());
            break;
        }
    }
    yh::Token& token = tokens_.tokens[leader];
    if (!token.path.empty() || creatures_[leader].sheet.down())
        return;
    std::optional<yh::Cell> goal;
    float nearest = 0;
    for (size_t i = heroCount_; i < creatures_.size(); i++)
    {
        if (creatures_[i].sheet.down() || creatures_[i].team != 1)
            continue;
        const float d = grid_.distance(cellOf(leader), cellOf(i));
        if (!goal || d < nearest)
        {
            goal = cellOf(i);
            nearest = d;
        }
    }
    if (!goal)
        return;
    const std::vector<yh::Cell> path = findPath(grid_, cellOf(leader), *goal, [this](yh::Cell c) { return walkable(c); });
    if (path.size() < 4)
    {
        if (path.empty() && autoExploreStuck_++ == 0)
            say("Auto-play: no path to the next enemy.");
        return;
    }
    // Stop a few squares short; spotting them starts the fight anyway.
    for (size_t i = 1; i + 2 < path.size() && i < 8; i++)
        token.path.push_back(grid_.center(path[i]));
}
