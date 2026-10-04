// How the creatures the game plays think: their AI profile, and what they see of the fight.

#include "World.h"

#include <algorithm>
#include <queue>

// A creature's AI is worked out fresh each turn from layers, each on top of the last, so any of
// them can change mid-adventure:
//   1. the creature's file           creatures/goblin.json  "ai"
//   2. the encounter it belongs to   chapter.json           encounters[].ai
//   3. where it was placed           chapter.json           encounters[].creatures[].ai, npcs[].ai
//   4. the story                     chapter.json           aiChanges, once their flags are set
//   5. the server                    its "ai" config        creatures.<id>
// Names in any layer are AI profiles: ai/<name>.json, or the server's "profiles", which win.
yh::AiProfile World::aiFor(size_t index) const
{
    const Creature& c = creatures_[index];
    const yh::AiProfile::Lookup lookup = [this](std::string_view name) -> const yh::AiProfile* {
        if (const auto found = serverProfiles_.find(name); found != serverProfiles_.end())
            return &found->second;
        const auto found = chapter_->compendium.ai.find(name);
        return found == chapter_->compendium.ai.end() ? nullptr : &found->second;
    };
    yh::AiProfile profile = *yh::AiProfile::preset("cunning");
    // A layer that no longer makes sense (a profile the server removed) is skipped, not fatal.
    auto layer = [&](const std::string& json) {
        if (json.empty())
            return;
        if (std::optional<yh::AiProfile> next = yh::AiProfile::fromJson(json, nullptr, lookup, &profile))
            profile = std::move(*next);
    };

    if (index < heroCount_)
    {
        // Auto-play: heroes use the "hero" profile, or fight like goblins that never run.
        if (const yh::AiProfile* hero = lookup("hero"))
            return *hero;
        profile.fleeHp = 0;
        profile.fleeLosses = 2;
        profile.fleeLeaderless = false;
        return profile;
    }

    if (const yh::CreatureDefinition* definition = chapter_->compendium.creature(c.creatureId))
        layer(definition->ai);
    for (const std::string& json : c.aiLayers)
        layer(json);
    const std::string encounter = c.group >= 0 && c.group < static_cast<int>(chapter_->encounters.size()) ? chapter_->encounters[c.group].id : std::string();
    for (const Chapter::AiChange& change : chapter_->aiChanges)
    {
        const bool matches = (change.creature.empty() || change.creature == c.creatureId) && (change.encounter.empty() || change.encounter == encounter)
            && (change.name.empty() || change.name == c.sheet.name);
        if (matches && std::all_of(change.when.begin(), change.when.end(), [&](const std::string& flag) { return flags_.contains(flag); }))
            layer(change.ai);
    }
    if (const auto found = serverCreatureAi_.find(c.creatureId); found != serverCreatureAi_.end())
        layer(found->second);
    return profile;
}

// The server's "ai" config: {"profiles": {"coward": {...}}, "creatures": {"goblin": "coward"}}.
// Profiles add to (or replace) the ones in the files; creatures put a last layer on a kind of creature.
void World::applyServerAi(const nlohmann::json& config)
{
    serverProfiles_.clear();
    serverCreatureAi_.clear();
    if (!chapter_ || !config.is_object())
        return;
    const nlohmann::json profiles = config.value("profiles", nlohmann::json::object());
    const yh::AiProfile blank = [] { yh::AiProfile p; p.base = "custom"; return p; }();
    // Server profiles can build on each other; a few passes settle any order.
    for (int pass = 0; pass < 4 && profiles.is_object(); pass++)
    {
        for (auto it = profiles.begin(); it != profiles.end(); ++it)
        {
            if (serverProfiles_.contains(it.key()) || !it.value().is_object())
                continue;
            const std::string name = it.key();
            const yh::AiProfile::Lookup lookup = [&](std::string_view base) -> const yh::AiProfile* {
                if (const auto found = serverProfiles_.find(base); found != serverProfiles_.end())
                    return &found->second;
                // A profile that replaces one from the files may start from the one it replaces.
                const auto found = chapter_->compendium.ai.find(base);
                return found == chapter_->compendium.ai.end() ? nullptr : &found->second;
            };
            if (std::optional<yh::AiProfile> profile = yh::AiProfile::fromJson(it.value().dump(), nullptr, lookup, &blank))
            {
                profile->base = name;
                serverProfiles_[name] = std::move(*profile);
            }
        }
    }
    const nlohmann::json creatures = config.value("creatures", nlohmann::json::object());
    if (creatures.is_object())
        for (auto it = creatures.begin(); it != creatures.end(); ++it)
            if (it.value().is_string() || it.value().is_object())
                serverCreatureAi_[it.key()] = it.value().dump();
}

// AI and creature files edited while the game is open: read them again, keep the old ones if they're broken.
void World::reloadAi(const nlohmann::json& serverConfig)
{
    if (!chapter_)
        return;
    yh::Compendium fresh;
    std::string problem;
    if (!fresh.load(chapterFiles_, "", &problem) || !fresh.load(chapterFiles_, chapter_->folder, &problem))
    {
        say("AI files not reloaded: " + problem);
        return;
    }
    chapter_->compendium.ai = fresh.ai;
    for (auto& [id, definition] : chapter_->compendium.creatures)
        if (const yh::CreatureDefinition* updated = fresh.creature(id))
            definition.ai = updated->ai;
    applyServerAi(serverConfig);
    say("AI profiles reloaded.");
}

// Walking distance from every cell to the nearest standing foe of `team`.
yh::CellCosts World::distanceToFoes(int team) const
{
    std::vector<yh::Cell> foes;
    for (size_t i = 0; i < creatures_.size(); i++)
        if (creatures_[i].team != team && creatures_[i].team != 2 && !creatures_[i].sheet.down() && orderIndex(i))
            foes.push_back(cellOf(i));
    return distanceFrom(foes);
}

// Enemies not yet in the fight near `creature` (walking squares): the group it could bring in.
std::optional<int> World::sleepingGroupNear(size_t creature, float squares) const
{
    std::vector<yh::Cell> cells;
    std::vector<int> groups;
    for (size_t i = heroCount_; i < creatures_.size(); i++)
    {
        const Creature& c = creatures_[i];
        if (c.team == creatures_[creature].team && !c.awake && !c.sheet.down() && !orderIndex(i) && c.npc < 0)
        {
            cells.push_back(cellOf(i));
            groups.push_back(c.group);
        }
    }
    if (cells.empty())
        return std::nullopt;
    const yh::CellCosts distance = distanceFrom(cells);
    const auto here = distance.find(cellOf(creature));
    if (here == distance.end() || here->second > squares)
        return std::nullopt;
    // The group of whoever is nearest in a straight line.
    size_t nearest = 0;
    for (size_t i = 1; i < cells.size(); i++)
        if (grid_.distance(cells[i], cellOf(creature)) < grid_.distance(cells[nearest], cellOf(creature)))
            nearest = i;
    return groups[nearest];
}

// Walking distance from every cell to the nearest of `cells`, around walls and other creatures.
yh::CellCosts World::distanceFrom(const std::vector<yh::Cell>& cells) const
{
    yh::CellCosts distance;
    using Entry = std::pair<float, yh::Cell>;
    auto later = [](const Entry& a, const Entry& b) { return a.first > b.first; };
    std::priority_queue<Entry, std::vector<Entry>, decltype(later)> queue(later);
    for (const yh::Cell c : cells)
    {
        distance[c] = 0;
        queue.push({0.0f, c});
    }
    std::vector<yh::Cell> neighbours;
    while (!queue.empty())
    {
        const auto [cost, c] = queue.top();
        queue.pop();
        if (cost > distance[c])
            continue;
        grid_.neighbours(c, neighbours);
        for (const yh::Cell next : neighbours)
        {
            if (!walkable(next) || (next.x != c.x && next.y != c.y && (!walkable({next.x, c.y}) || !walkable({c.x, next.y}))))
                continue;
            const float nextCost = cost + grid_.stepCost(c, next, 0);
            const auto known = distance.find(next);
            if (known == distance.end() || nextCost < known->second)
            {
                distance[next] = nextCost;
                queue.push({nextCost, next});
            }
        }
    }
    return distance;
}

yh::TacticalView World::tacticalView(size_t me, std::vector<size_t>& who)
{
    yh::TacticalView view;
    who.clear();
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        const Creature& c = creatures_[i];
        if (c.sheet.down() || !orderIndex(i) || c.surrendered)
            continue;
        if (i == me)
            view.self = who.size();
        yh::TacticalUnit unit;
        unit.team = c.team;
        unit.at = cellOf(i);
        unit.hp = c.sheet.hp;
        unit.maxHp = c.sheet.maxHp();
        unit.armorClass = c.sheet.armorClass(rules_);
        unit.attackBonus = c.sheet.attackModifier(rules_);
        const std::optional<yh::DiceExpression> damage = yh::DiceExpression::parse(c.sheet.damageDice(rules_));
        unit.averageDamage = damage ? std::max(1.0f, static_cast<float>(damage->minimum() + damage->maximum()) / 2) : 1.0f;
        unit.speed = c.sheet.speedSquares(rules_);
        unit.leader = aiFor(i).leader;
        view.units.push_back(unit);
        who.push_back(i);
    }
    const int team = creatures_[me].team;
    view.actions = encounter_->current().budget.actions;
    view.strikeCost = encounter_->strikeCost();
    if (view.actions >= 1)
    {
        computeReach(me, creatures_[me].sheet.speedSquares(rules_));
        view.dashReach = reach_;
    }
    computeReach(me);
    view.reach = reach_;
    view.foeDistance = distanceToFoes(team);
    view.sideAtStart = sideAtStart_[team == 0 ? 0 : 1];
    view.hadLeader = hadLeader_[team == 0 ? 0 : 1];
    view.fleeing = creatures_[me].fleeing;
    view.breakAs = creatures_[me].breakAs;
    if (view.breakAs == "alarm")
    {
        std::vector<yh::Cell> help;
        for (size_t i = heroCount_; i < creatures_.size(); i++)
            if (creatures_[i].team == team && !creatures_[i].awake && !creatures_[i].sheet.down() && !orderIndex(i) && creatures_[i].npc < 0)
                help.push_back(cellOf(i));
        if (!help.empty())
            view.allyDistance = distanceFrom(help);
    }
    return view;
}
