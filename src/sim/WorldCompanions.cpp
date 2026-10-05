// Companions: chapter NPCs who join the party through their dialogue, what they think of the
// party's choices, and taking them along to other chapters and to camp.

#include "World.h"

#include <algorithm>
#include <charconv>
#include <sstream>

bool World::companion(size_t creature) const
{
    if (creature < heroCount_ || creature >= creatures_.size())
        return false;
    const Creature& c = creatures_[creature];
    return c.team == 0 && !c.companionId.empty() && companions_.member(c.companionId);
}

bool World::partyMember(size_t creature) const
{
    return creature < heroCount_ || companion(creature);
}

int World::partyMemberCount() const
{
    return static_cast<int>(heroCount_ + companions_.members().size());
}

std::optional<size_t> World::companionToken(std::string_view id) const
{
    for (size_t i = heroCount_; i < creatures_.size(); i++)
        if (creatures_[i].companionId == id)
            return i;
    return std::nullopt;
}

void World::meetCompanions()
{
    for (const Chapter::Npc& npc : chapter_->npcs)
        if (npc.companion)
            companions_.define(*npc.companion);
}

void World::followParty()
{
    if (heroCount_ == 0)
        return;
    // Links don't cross owners: companions belong to whoever plays the first hero and walk at the
    // back of that player's line.
    const int owner = tokens_.tokens[0].owner;
    size_t leader = 0;
    for (size_t i = 0; i < heroCount_; i++)
        if (tokens_.tokens[i].owner == owner)
            leader = i;
    for (size_t i = heroCount_; i < creatures_.size(); i++)
    {
        if (!companion(i))
            continue;
        tokens_.unlink(i);
        tokens_.tokens[i].owner = owner;
        if (!creatures_[i].sheet.down() && tokens_.link(i, leader))
            leader = i;
    }
}

void World::joinParty(size_t creature)
{
    Creature& c = creatures_[creature];
    const std::string& name = c.sheet.name;
    switch (c.companionId.empty() ? yh::CompanionJoin::Unknown : companions_.join(rules_.companions, c.companionId, static_cast<int>(heroCount_)))
    {
    case yh::CompanionJoin::Joined:
        c.team = 0;
        c.awake = true;
        c.sheet.death.saves = true; // a companion goes down like a hero, not straight to dead
        say(name + " joins the party.");
        followParty();
        break;
    case yh::CompanionJoin::Already:
        if (c.team != 0) // back from a side change the roster didn't see
        {
            c.team = 0;
            followParty();
        }
        break;
    case yh::CompanionJoin::LowApproval:
        say(name + " isn't ready to join yet.");
        break;
    case yh::CompanionJoin::Full:
        say("The party is full. " + name + " can't join.");
        break;
    case yh::CompanionJoin::Unknown:
        say(name + " won't join the party.");
        break;
    }
}

void World::companionLeaves(std::string_view id)
{
    companions_.leave(id);
    const std::optional<size_t> found = companionToken(id);
    if (!found || creatures_[*found].team != 0)
        return;
    Creature& c = creatures_[*found];
    c.team = 2; // a neutral NPC again, where they stand
    tokens_.unlink(*found);
    tokens_.tokens[*found].owner = npcOwner;
    tokens_.tokens[*found].path.clear();
    tokens_.tokens[*found].selected = false;
    say(c.sheet.name + " leaves the party.");
    followParty();
}

void World::changeApproval(std::string_view id, int delta)
{
    if (!companions_.definition(id) || delta == 0)
        return;
    const int before = companions_.approval(id);
    const bool left = companions_.adjust(rules_.companions, id, delta);
    const int change = companions_.approval(id) - before;
    const std::optional<size_t> found = companionToken(id);
    const std::string name = found ? creatures_[*found].sheet.name : std::string(id);
    if (change != 0)
        say(name + (change > 0 ? " approves (+" : " disapproves (") + std::to_string(change) + ").");
    if (left)
        companionLeaves(id);
}

void World::companionFlags()
{
    std::map<std::string, int> before;
    for (const yh::CompanionDefinition& d : companions_.definitions())
        before[d.id] = companions_.approval(d.id);
    const std::vector<std::string> left = companions_.flagsSet(rules_.companions, flags_);
    for (const auto& [id, was] : before)
    {
        const int change = companions_.approval(id) - was;
        if (change == 0)
            continue;
        const std::optional<size_t> found = companionToken(id);
        say((found ? creatures_[*found].sheet.name : id) + (change > 0 ? " approves (+" : " disapproves (") + std::to_string(change) + ").");
    }
    for (const std::string& id : left)
        companionLeaves(id);
}

bool World::companionAction(size_t creature, const std::string& action)
{
    std::istringstream in(action);
    std::vector<std::string> words;
    for (std::string word; in >> word;)
        words.push_back(word);
    if (words.empty())
        return false;
    const std::string& self = creatures_[creature].companionId;
    if (words[0] == "recruit" && words.size() == 1)
    {
        joinParty(creature);
        return true;
    }
    if (words[0] == "dismiss" && words.size() == 1)
    {
        if (companion(creature))
            companionLeaves(self);
        return true;
    }
    if (words[0] == "approve" && (words.size() == 2 || words.size() == 3))
    {
        // "approve 5" is the one being talked to, "approve wren -3" anyone the party has met.
        const std::string id = words.size() == 3 ? words[1] : self;
        std::string_view number = words.back();
        if (!number.empty() && number.front() == '+')
            number.remove_prefix(1);
        int delta = 0;
        const auto [end, problem] = std::from_chars(number.data(), number.data() + number.size(), delta);
        if (problem == std::errc() && end == number.data() + number.size())
            changeApproval(id, delta);
        return true;
    }
    return false;
}

std::vector<World::Along> World::companionsAlong() const
{
    std::vector<Along> along;
    for (size_t i = heroCount_; i < creatures_.size(); i++)
    {
        if (!companion(i))
            continue;
        Along a{creatures_[i], tokens_.tokens[i]};
        a.creature.companionTalk = dialogueFor(i);
        a.creature.npc = -1;
        a.creature.group = -1;
        a.creature.concentration = {};
        a.creature.readiedAction.clear();
        a.token.path.clear();
        a.token.selected = false;
        along.push_back(std::move(a));
    }
    return along;
}

void World::placeCompanions(std::vector<Along> along)
{
    for (Along& a : along)
    {
        // Already on this map (their own chapter, or the one the party left for camp): that one
        // takes the sheet as it is now.
        if (const std::optional<size_t> here = companionToken(a.creature.companionId))
        {
            Creature& c = creatures_[*here];
            c.sheet = std::move(a.creature.sheet);
            c.mayPrepare = a.creature.mayPrepare;
            c.team = 0;
            c.awake = true;
            c.fled = false;
            tokens_.tokens[*here].name = c.sheet.name;
            tokens_.tokens[*here].floor = c.sheet.down() ? dead : 0;
            continue;
        }
        a.creature.team = 0;
        a.creature.awake = true;
        a.token.floor = a.creature.sheet.down() ? dead : 0;
        creatures_.push_back(std::move(a.creature));
        tokens_.tokens.push_back(std::move(a.token));
    }
    followParty();
}
