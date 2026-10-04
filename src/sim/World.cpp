#include "World.h"

#include <algorithm>

namespace
{

constexpr float cell = GameMap::cellSize;

}

World::World(const yh::FileSystem& files) : chapterFiles_(files) {}

World::~World() = default;

std::vector<World::Event> World::takeEvents()
{
    std::vector<Event> taken;
    taken.swap(events_);
    return taken;
}

bool World::loadChapter(const std::string& folder, std::string* error)
{
    std::optional<Chapter> chapter = Chapter::load(chapterFiles_, folder, error);
    if (!chapter)
    {
        chapter_.reset();
        return false;
    }
    chapter_ = std::make_unique<Chapter>(std::move(*chapter));
    rules_ = chapter_->rules;
    return true;
}

void World::say(std::string line)
{
    emit({Event::Kind::Log, std::move(line)});
}

void World::newAdventure(uint64_t seed)
{
    seed_ = seed;
    fights_ = 0;
    restsUsed_.clear();
    restRandom_ = yh::Random(seed ^ 0x5eedull);
    encounter_.reset();
    pendingMovement_.reset();
    pendingReaction_.reset();
    reactionPrompt_.reset();
    reactionSequence_ = 0;
    reach_.clear();
    pendingAttack_.reset();
    emit({Event::Kind::Reset});
    fog_.reset(0);
    tokens_.clearLinks();
    tokens_.tokens.clear();
    tokens_.settings.inCombat = false;
    tokens_.settings.activeTurn.reset();
    creatures_.clear();
    inCutscene_ = false;
    pendingWipe_ = false;
    wipeRequested_ = false;
    checkpoint_.clear();
    autoExploreStuck_ = 0;
    flags_.clear();
    journal_.reset();
    talk_.reset();
    pendingTalk_.reset();
    rolls_ = 0;
    pendingStep_.reset();

    heroCount_ = 0;
    if (!chapter_)
        return;
    if (!chapter_->quests.empty())
        if (const std::optional<std::string> text = chapterFiles_.readText(chapter_->quests))
            journal_ = yh::QuestJournal::fromJson(*text); // Chapter::load already checked it
    fog_ = yh::FogOfWar(map().width(), map().height(), GameMap::cellSize);
    lightLevels_ = yh::LightLevels(map().width(), map().height(), GameMap::cellSize);
    lightLevels_.ambient = map().lighting().ambient;
    lightLevels_.brightFraction = map().lighting().brightFraction;
    {
        std::vector<yh::Light> fixed;
        for (const GameMap::Light& l : map().lights())
            fixed.push_back({l.position, l.radius, l.color});
        lightLevels_.setFixed(fixed, map().walls());
    }

    // Everything below comes from the chapter's files; Chapter::load already checked the ids.
    yh::Random random(seed);
    for (const Chapter::PartyMember& member : chapter_->party)
    {
        // The ready-made hero is rolled even when someone else takes the seat, so the dice that
        // follow are the same either way.
        yh::CharacterChoices choices = yh::rollChoices(rules_, member.name, member.classId, random);
        choices.levels.resize(static_cast<size_t>(chapter_->level), choices.levels.front());
        if (chapter_->level > 1 && !rules_.xpForLevel.empty())
            choices.xp = rules_.xpForLevel.at(std::min<size_t>(chapter_->level - 2, rules_.xpForLevel.size() - 1));
        const size_t seat = creatures_.size();
        const PartyPick* pick = seat < partyPicks_.size() && partyPicks_[seat] ? &*partyPicks_[seat] : nullptr;
        std::string error;
        std::optional<yh::Character> brought = pick ? chapter_->compendium.build(rules_, pick->choices, &error) : std::nullopt;
        if (pick && !brought)
            say(pick->choices.name + " can't play this adventure (" + error + "); " + member.name + " takes the seat.");
        if (brought)
        {
            // What they carry comes with them, worn as it was.
            for (size_t i = brought->inventory.size(); i-- > 0;)
                brought->unequip(i);
            brought->inventory.clear();
            for (yh::Item item : pick->inventory)
            {
                const bool worn = item.equipped;
                item.equipped = false;
                brought->inventory.push_back(std::move(item));
                if (worn)
                    brought->equip(brought->inventory.size() - 1);
            }
            creatures_.push_back({std::move(*brought)});
            creatures_.back().choices = pick->choices;
            creatures_.back().library = pick->library;
        }
        else
        {
            creatures_.push_back({*chapter_->compendium.build(rules_, choices)});
            creatures_.back().choices = std::move(choices);
        }
        yh::Token token;
        token.name = creatures_.back().sheet.name;
        token.color = member.color;
        token.radius = cell * 0.4f;
        token.position = grid_.center(member.at);
        token.owner = tokens_.tokens.size() < seats_.size() ? seats_[tokens_.tokens.size()] : 0;
        tokens_.tokens.push_back(token);
    }
    heroCount_ = creatures_.size();
    selectOwnHero();
    for (size_t group = 0; group < chapter_->encounters.size(); group++)
    {
        for (const Chapter::Placement& placement : chapter_->encounters[group].creatures)
        {
            const yh::CreatureDefinition& definition = *chapter_->compendium.creature(placement.creatureId);
            creatures_.push_back({*chapter_->compendium.makeCreature(rules_, placement.creatureId, placement.name, random), 1,
                static_cast<int>(group)});
            creatures_.back().creatureId = placement.creatureId;
            creatures_.back().aiLayers = {chapter_->encounters[group].ai, placement.ai};
            creatures_.back().surrender = !placement.surrender.empty() ? placement.surrender
                : !chapter_->encounters[group].surrender.empty() ? chapter_->encounters[group].surrender : chapter_->surrender;
            yh::Token token;
            token.name = creatures_.back().sheet.name;
            token.owner = enemyOwner;
            token.color = definition.token.color;
            token.image = definition.token.image;
            token.radius = cell * definition.token.size;
            token.position = grid_.center(placement.at);
            token.floor = hidden;
            tokens_.tokens.push_back(token);
            creatures_.back().facing = chapter_->facingOf(placement);
        }
    }
    {
        sneak_.assign(heroCount_, yh::StealthTracker(chapter_->stealthOnMap()));
        lastAt_.clear();
        for (size_t i = 0; i < heroCount_; i++)
            lastAt_.push_back(tokens_.tokens[i].position);
        stealthRandom_ = yh::Random(seed ^ 0x57ea1ull);
    }
    npcStart_ = creatures_.size();
    for (size_t i = 0; i < chapter_->npcs.size(); i++)
    {
        const Chapter::Npc& npc = chapter_->npcs[i];
        const yh::CreatureDefinition& definition = *chapter_->compendium.creature(npc.creature);
        Creature creature{*chapter_->compendium.makeCreature(rules_, npc.creature, npc.name, random), 2,
            static_cast<int>(chapter_->encounters.size() + i)};
        creature.npc = static_cast<int>(i);
        creature.creatureId = npc.creature;
        creature.aiLayers = {npc.ai};
        creature.surrender = chapter_->surrender;
        creatures_.push_back(std::move(creature));
        yh::Token token;
        token.name = npc.name;
        token.owner = npcOwner;
        token.color = npc.color;
        token.radius = cell * definition.token.size;
        token.position = grid_.center(npc.at);
        token.floor = hidden;
        tokens_.tokens.push_back(token);
    }
    for (size_t i = 1; i < heroCount_; i++)
        tokens_.link(i, i - 1);

    for (const std::string& line : chapter_->intro)
        say(line);
    emit({Event::Kind::Banner, chapter_->title, {}, FloatKind::Miss, 3});
    checkpoint_ = stateJson();
}

bool World::partyDown() const
{
    return heroCount_ > 0 && std::all_of(creatures_.begin(), creatures_.begin() + heroCount_, [](const Creature& c) { return c.sheet.down(); });
}

bool World::chapterCleared() const
{
    if (!chapter_)
        return false;
    if (!chapter_->completeWhen.empty())
        return std::all_of(chapter_->completeWhen.begin(), chapter_->completeWhen.end(), [this](const std::string& f) { return flags_.contains(f); });
    // Every authored enemy is down (NPCs the party picked a fight with don't count).
    return !chapter_->encounters.empty()
        && std::none_of(creatures_.begin() + heroCount_, creatures_.end(), [](const Creature& c) { return c.npc < 0 && !c.sheet.down() && !c.surrendered; });
}

int World::restsLeft(const yh::RestDefinition& rest) const
{
    if (rest.perAdventure == 0)
        return -1; // unlimited
    const auto used = restsUsed_.find(rest.id);
    return std::max(0, rest.perAdventure - (used == restsUsed_.end() ? 0 : used->second));
}

bool World::canSave() const
{
    // Only between fights: the encounter points into creatures_ and isn't saved. A joined player's
    // game belongs to the host, who keeps the save.
    return chapter_ && !remote_ && !(encounter_ && !encounter_->finished()) && !partyDown() && !chapterCleared() && !inCutscene_;
}

void World::requestSave()
{
    if (chapter_ && !(encounter_ && !encounter_->finished()) && !partyDown() && !chapterCleared() && !inCutscene_)
    {
        checkpoint_ = stateJson();
        if (saves_ && !remote_) emit({Event::Kind::Save, checkpoint_});
    }
}

yh::Random World::nextRandom(uint64_t salt)
{
    return yh::Random(seed_ ^ salt ^ (++rolls_ * 0x9e3779b97f4a7c15ull));
}

// ---------------------------------------------------------------- story flags and quests

void World::setFlags(const std::vector<std::string>& flags)
{
    const std::set<std::string> before = flags_;
    flags_.insert(flags.begin(), flags.end());
    if (flags_ != before)
        flagsChanged(before);
}

// Tells the party what changed in the journal.
void World::flagsChanged(const std::set<std::string>& before)
{
    if (!journal_)
        return;
    for (const yh::Quest& quest : journal_->quests)
    {
        const yh::QuestProgress was = quest.progress(before);
        const yh::QuestProgress now = quest.progress(flags_);
        if (now.status == yh::QuestStatus::Hidden)
            continue;
        if (was.status == yh::QuestStatus::Hidden)
            say("New quest: " + quest.title + " (J: journal)");
        for (size_t i = 0; i < quest.objectives.size(); i++)
            if (now.objectiveComplete[i] && !was.objectiveComplete[i] && now.status != yh::QuestStatus::Failed)
                say("Done: " + quest.objectives[i].text);
        if (now.status != was.status && now.status == yh::QuestStatus::Completed)
        {
            say("Quest complete: " + quest.title);
            emit({Event::Kind::Banner, quest.title, {}, FloatKind::Miss, 2.5});
        }
        else if (now.status != was.status && now.status == yh::QuestStatus::Failed)
            say("Quest failed: " + quest.title);
    }
}

// ---------------------------------------------------------------- who plays what

bool World::mine(size_t creature) const
{
    if (creature < heroCount_)
        return tokens_.tokens[creature].owner == self_;
    return !remote_; // the host plays the enemies
}

bool World::mayAct(yh::PlayerId player, size_t creature) const
{
    if (creature < heroCount_)
        return tokens_.tokens[creature].owner == player || (player == 0 && autoPlay_);
    return player == 0;
}

void World::selectOwnHero()
{
    std::optional<size_t> pick;
    for (size_t i = 0; i < heroCount_; i++)
        if (mine(i) && !creatures_[i].sheet.down() && (!pick || tokens_.tokens[i].selected))
            pick = i;
    for (size_t i = 0; i < heroCount_; i++)
        tokens_.tokens[i].selected = pick && i == *pick;
}

std::string World::seatName(size_t hero) const
{
    const int owner = hero < heroCount_ ? tokens_.tokens[hero].owner : 0;
    const auto name = playerNames_.find(owner);
    return name != playerNames_.end() ? name->second : "Player " + std::to_string(owner + 1);
}

// ---------------------------------------------------------------- who stands where

bool World::talkable(size_t creature) const
{
    if (creature >= creatures_.size() || creature < heroCount_)
        return false;
    const Creature& c = creatures_[creature];
    return c.team == 2 && !c.sheet.down() && (c.npc >= 0 || c.surrendered);
}

std::optional<size_t> World::talkerAt(yh::Cell c) const
{
    if (!chapter_)
        return std::nullopt;
    for (size_t i = heroCount_; i < creatures_.size(); i++)
        if (talkable(i) && cellOf(i) == c)
            return i;
    return std::nullopt;
}

bool World::occupied(yh::Cell c, size_t except) const
{
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        if (i != except && tokens_.tokens[i].floor != dead && cellOf(i) == c)
            return true;
    }
    return false;
}

bool World::walkable(yh::Cell c) const
{
    return map().walkable(c) && !talkerAt(c);
}

std::optional<size_t> World::orderIndex(size_t creature) const
{
    if (!encounter_ || creature >= creatures_.size())
        return std::nullopt;
    const auto& order = encounter_->order();
    for (size_t i = 0; i < order.size(); i++)
    {
        if (order[i].character == &creatures_[creature].sheet)
            return i;
    }
    return std::nullopt;
}

std::optional<size_t> World::currentCreature() const
{
    if (!encounter_ || !encounter_->started() || encounter_->finished())
        return std::nullopt;
    const yh::Character* current = encounter_->order()[encounter_->currentIndex()].character;
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        if (&creatures_[i].sheet == current)
            return i;
    }
    return std::nullopt;
}

yh::Cell World::cellOf(size_t creature) const
{
    const yh::Token& token = tokens_.tokens[creature];
    return grid_.cellAt(token.path.empty() ? token.position : token.path.back());
}

bool World::adjacent(size_t a, size_t b) const
{
    return grid_.distance(cellOf(a), cellOf(b)) <= 1.01f;
}
