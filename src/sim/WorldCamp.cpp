// Camp: a small map the party goes to between fights and comes back from to the same place. The
// stash, revival for coins and the rests the ruleset keeps for camp are only there.

#include "World.h"

#include <yorehold/framework/assets/FileSystem.h>

#include <algorithm>
#include <deque>

std::unique_ptr<Chapter> World::loadChapterFor(const std::string& folder, size_t heroes, std::string* error) const
{
    std::optional<Chapter> loaded = Chapter::load(chapterFiles_, folder, error);
    if (!loaded)
        return nullptr;
    auto chapter = std::make_unique<Chapter>(std::move(*loaded));
    // Camp seats whoever comes: as many seats as heroes, the extra ones beside the first.
    if (folder == campFolder_ && heroes > 0 && !chapter->party.empty())
        chapter->party.resize(heroes, chapter->party.front());
    return chapter;
}

bool World::knownFolder(const std::string& folder) const
{
    return folder == homeFolder_ || (!campFolder_.empty() && folder == campFolder_) || (adventure_ && adventure_->hasFolder(folder));
}

bool World::atCamp() const
{
    return chapter_ && !campFolder_.empty() && chapter_->folder == campFolder_;
}

bool World::canMakeCamp(std::string* why) const
{
    auto refuse = [&](const std::string& text) { if (why) *why = text; return false; };
    if (why) why->clear();
    if (!chapter_ || campFolder_.empty())
        return refuse("There is nowhere to make camp.");
    if (atCamp())
        return refuse("The party is already at camp.");
    if (!chapter_->campAllowed)
        return refuse("The party can't make camp here.");
    if (fighting() || inCutscene_ || talk_ || partyDown())
        return refuse("Not now.");
    return true;
}

int World::suppliesHeld() const
{
    std::vector<const yh::Character*> packs;
    for (size_t i = 0; i < heroCount_; i++)
        if (!creatures_[i].sheet.death.dead)
            packs.push_back(&creatures_[i].sheet);
    return yh::supplyPoints(stash_, packs);
}

bool World::canRest(const yh::RestDefinition& rest, std::string* why) const
{
    auto refuse = [&](const std::string& text) { if (why) *why = text; return false; };
    if (why) why->clear();
    const std::string name = rest.name.empty() ? rest.id : rest.name;
    if (!chapter_ || fighting() || inCutscene_ || talk_ || partyDown())
        return refuse("");
    if (restsLeft(rest) == 0)
        return refuse("No " + name + " left.");
    if (rest.campOnly && !atCamp())
        return refuse("Make camp first to take a " + name + ".");
    if (const int held = suppliesHeld(); held < rest.supplyCost)
        return refuse("Not enough supplies for a " + name + ": " + std::to_string(held) + " of " + std::to_string(rest.supplyCost) + ".");
    return true;
}

bool World::canRevive(size_t payer, size_t target, std::string* why) const
{
    auto refuse = [&](const std::string& text) { if (why) *why = text; return false; };
    if (why) why->clear();
    if (!atCamp() || fighting() || inCutscene_ || talk_ || partyDown())
        return refuse("The dead can only be brought back at camp.");
    if (rules_.revivePrice <= 0)
        return refuse("Nobody here can bring back the dead.");
    if (payer >= heroCount_ || target >= heroCount_ || creatures_[payer].sheet.down())
        return refuse("");
    if (!creatures_[target].sheet.death.dead)
        return refuse(creatures_[target].sheet.name + " isn't dead.");
    if (creatures_[payer].sheet.coins < rules_.revivePrice)
        return refuse(creatures_[payer].sheet.name + " needs " + coinText(rules_.revivePrice) + " to pay for it.");
    return true;
}

void World::gatherAt(yh::Cell entry)
{
    // The first hero on the cell, the rest and then the companions on the nearest free squares.
    std::vector<size_t> party;
    for (size_t i = 0; i < creatures_.size(); i++)
        if (partyMember(i))
            party.push_back(i);
    std::deque<yh::Cell> open{entry};
    std::set<std::pair<int, int>> seen{{entry.x, entry.y}};
    for (size_t next = 0; next < party.size() && !open.empty();)
    {
        const yh::Cell c = open.front();
        open.pop_front();
        const size_t i = party[next];
        if (walkable(c) && !occupied(c, i))
        {
            tokens_.tokens[i].position = grid_.center(c);
            tokens_.tokens[i].path.clear();
            next++;
        }
        for (const yh::Cell n : {yh::Cell{c.x + 1, c.y}, yh::Cell{c.x - 1, c.y}, yh::Cell{c.x, c.y + 1}, yh::Cell{c.x, c.y - 1}})
            if (map().inside(n) && map().walkable(n) && seen.insert({n.x, n.y}).second)
                open.push_back(n);
    }
    for (size_t i = 0; i < heroCount_; i++)
        lastAt_[i] = tokens_.tokens[i].position;
}

World::Carried World::carry() const
{
    Carried c;
    c.heroes.assign(creatures_.begin(), creatures_.begin() + static_cast<std::ptrdiff_t>(heroCount_));
    for (size_t i = 0; i < heroCount_; i++)
        c.colors.push_back(tokens_.tokens[i].color);
    c.flags = flags_;
    c.rests = restsUsed_;
    c.fired = firedTriggers_;
    c.rolls = rolls_;
    c.stash = stash_;
    c.roster = companions_;
    c.companions = companionsAlong();
    return c;
}

void World::putBack(Carried carried)
{
    for (size_t i = 0; i < heroCount_ && i < carried.heroes.size(); i++)
    {
        Creature& hero = creatures_[i];
        hero.sheet = std::move(carried.heroes[i].sheet);
        hero.choices = std::move(carried.heroes[i].choices);
        hero.library = std::move(carried.heroes[i].library);
        hero.mayPrepare = carried.heroes[i].mayPrepare;
        hero.concentration = {}; // nobody holds a spell across the way to camp
        tokens_.tokens[i].name = hero.sheet.name;
        if (i < carried.colors.size())
            tokens_.tokens[i].color = carried.colors[i];
        tokens_.tokens[i].floor = hero.sheet.down() ? dead : 0;
    }
    flags_ = std::move(carried.flags);
    restsUsed_ = std::move(carried.rests);
    firedTriggers_.insert(carried.fired.begin(), carried.fired.end());
    rolls_ = carried.rolls;
    stash_ = std::move(carried.stash);
    companions_ = std::move(carried.roster);
    meetCompanions();
    placeCompanions(std::move(carried.companions));
    fallenConditions();
}

void World::makeCamp()
{
    std::string problem;
    std::unique_ptr<Chapter> camp = loadChapterFor(campFolder_, heroCount_, &problem);
    if (!camp)
    {
        say("Camp can't be made: " + problem);
        return;
    }
    endAllConcentration();
    // The chapter as the party leaves it, to come back to: who is dead, what is open, what is taken.
    const std::string away = stateJson();
    Carried carried = carry();
    const std::vector<std::optional<PartyPick>> picks = partyPicks_;
    std::vector<std::optional<PartyPick>> along;
    for (const Creature& hero : carried.heroes)
        along.push_back(PartyPick{hero.choices, hero.sheet.inventory, hero.library, hero.sheet.coins});

    chapter_ = std::move(camp);
    rules_ = chapter_->rules;
    partyPicks_ = std::move(along);
    newAdventure(seed_);
    partyPicks_ = picks;
    putBack(std::move(carried));
    campReturn_ = away;

    const std::optional<yh::Cell> entry = map().marker("entry");
    gatherAt(entry ? *entry : chapter_->party.front().at);
    heroMarker_.clear();
    for (size_t i = 0; i < heroCount_; i++)
        heroMarker_.push_back(exitMarkerAt(cellOf(i)));
    say("The party makes camp. Supplies: " + std::to_string(suppliesHeld()) + ".");
    emit({Event::Kind::Follow});
    checkpoint_ = stateJson();
    requestSave();
}

void World::leaveCamp()
{
    if (campReturn_.empty())
        return;
    Carried carried = carry();
    const std::string back = campReturn_;
    std::string problem;
    if (!restoreState(back, &problem))
    {
        say("The way back can't be found: " + problem);
        return;
    }
    putBack(std::move(carried));
    campReturn_.clear();
    say("The party breaks camp and heads back to " + chapter_->title + ".");
    emit({Event::Kind::Follow});
    checkpoint_ = stateJson();
    requestSave();
}

yh::Item World::takeEntry(yh::Character& sheet, size_t index)
{
    sheet.unequip(index);
    yh::Item item = sheet.inventory[index];
    // Later items move up a place: take their modifiers off and put them back under their new index.
    std::vector<bool> worn;
    for (size_t i = index + 1; i < sheet.inventory.size(); i++)
    {
        worn.push_back(sheet.inventory[i].equipped);
        sheet.unequip(i);
    }
    sheet.inventory.erase(sheet.inventory.begin() + static_cast<std::ptrdiff_t>(index));
    for (size_t i = 0; i < worn.size(); i++)
        if (worn[i])
            sheet.equip(index + i);
    sheet.hp = std::min(sheet.hp, sheet.maxHp());
    return item;
}

void World::toStash(size_t hero, size_t item)
{
    yh::Character& sheet = creatures_[hero].sheet;
    yh::Item stored = takeEntry(sheet, item);
    say(sheet.name + " puts " + stored.name + (stored.quantity > 1 ? " x" + std::to_string(stored.quantity) : "") + " in the stash.");
    stash_.add(std::move(stored));
}

void World::fromStash(size_t hero, size_t item)
{
    yh::Character& sheet = creatures_[hero].sheet;
    std::optional<yh::Item> taken = stash_.take(item, 0);
    if (!taken)
        return;
    say(sheet.name + " takes " + taken->name + (taken->quantity > 1 ? " x" + std::to_string(taken->quantity) : "") + " from the stash.");
    addTo(sheet, std::move(*taken));
}

void World::revive(size_t payer, size_t target)
{
    if (!canRevive(payer, target))
        return;
    yh::Character& pays = creatures_[payer].sheet;
    yh::Character& back = creatures_[target].sheet;
    pays.coins -= rules_.revivePrice;
    back.revive(rules_, rules_.reviveHp);
    back.removeCondition(deadCondition);
    tokens_.tokens[target].floor = 0;
    fallenConditions();
    say(pays.name + " pays " + coinText(rules_.revivePrice) + " and " + back.name + " is brought back with " + std::to_string(back.hp) + " HP.");
    emit({Event::Kind::Floater, "+" + std::to_string(back.hp), tokens_.tokens[target].position, FloatKind::Heal});
    requestSave();
}
