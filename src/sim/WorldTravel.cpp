// Moving between the chapters of an adventure: exit markers, and what the party takes along.

#include "World.h"

#include <algorithm>

std::string World::exitMarkerAt(yh::Cell cell) const
{
    if (!adventure_ || !chapter_)
        return {};
    for (const Adventure::Transition& t : adventure_->transitions)
        if (t.fromChapter == chapter_->id && map().marker(t.exitMarker) == cell)
            return t.exitMarker;
    return {};
}

void World::watchExits()
{
    if (!adventure_ || !chapter_ || remote_)
        return;
    heroMarker_.resize(heroCount_);
    const bool calm = !fighting() && !inCutscene_ && !talk_ && !partyDown();
    const std::vector<std::string> flags(flags_.begin(), flags_.end());
    for (size_t h = 0; h < heroCount_; h++)
    {
        const std::string marker = creatures_[h].sheet.down() ? std::string() : exitMarkerAt(cellOf(h));
        if (marker == heroMarker_[h])
            continue;
        heroMarker_[h] = marker;
        // A closed way (its flags aren't set yet) is just a square; stepping off and on tries again.
        if (!marker.empty() && calm && adventure_->transition(chapter_->id, marker, flags))
        {
            act("travel", nlohmann::json{{"hero", h}, {"marker", marker}}.dump());
            return;
        }
    }
}

void World::travel(const std::string& toChapter, const std::string& entryMarker)
{
    const std::string folder = adventure_->folderOf(toChapter);
    std::string problem;
    std::optional<Chapter> next = Chapter::load(chapterFiles_, folder, &problem);
    if (!next)
    {
        say("The way on can't be opened: " + problem);
        return;
    }

    // What goes along: the heroes as they are, the story so far and the rests already taken.
    const std::vector<Creature> heroes(creatures_.begin(), creatures_.begin() + static_cast<std::ptrdiff_t>(heroCount_));
    std::set<std::string> flags = flags_;
    for (const std::string& local : chapter_->localFlags)
        flags.erase(local);
    const std::map<std::string, int> rests = restsUsed_;
    const std::set<std::string> fired = firedTriggers_;
    yh::Stash stash = stash_;
    const std::vector<std::optional<PartyPick>> picks = partyPicks_;
    std::vector<std::optional<PartyPick>> along;
    for (const Creature& hero : heroes)
        along.push_back(PartyPick{hero.choices, hero.sheet.inventory, hero.library, hero.sheet.coins});

    chapter_ = std::make_unique<Chapter>(std::move(*next));
    rules_ = chapter_->rules;
    partyPicks_ = std::move(along);
    newAdventure(seed_);
    partyPicks_ = picks;

    // Wounds, spent slots and conditions stay as they were; Adventure::load made sure every
    // chapter seats as many heroes.
    for (size_t i = 0; i < heroCount_ && i < heroes.size(); i++)
    {
        creatures_[i].sheet = heroes[i].sheet;
        creatures_[i].choices = heroes[i].choices;
        creatures_[i].library = heroes[i].library;
        creatures_[i].mayPrepare = heroes[i].mayPrepare;
        tokens_.tokens[i].name = creatures_[i].sheet.name;
        if (creatures_[i].sheet.down())
            tokens_.tokens[i].floor = dead;
    }
    flags_ = std::move(flags);
    restsUsed_ = rests;
    firedTriggers_.insert(fired.begin(), fired.end()); // trigger ids are per adventure too
    stash_ = std::move(stash);

    // Everyone stands at the entry: the first hero on the marker, the rest on the nearest free squares.
    gatherAt(*map().marker(entryMarker)); // Adventure::load checked it
    heroMarker_.clear();
    for (size_t i = 0; i < heroCount_; i++)
        heroMarker_.push_back(exitMarkerAt(cellOf(i)));

    fallenConditions();
    say("The party goes on to " + chapter_->title + ".");
    emit({Event::Kind::Follow});
    checkTriggers();
    checkpoint_ = stateJson();
    requestSave();
}
