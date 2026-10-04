#include "CharacterDraft.h"

#include <algorithm>
#include <iterator>
#include <set>

namespace
{

template <typename Map>
std::vector<std::string> idsByName(const Map& definitions)
{
    std::vector<std::string> ids;
    for (const auto& [id, definition] : definitions)
        ids.push_back(id);
    std::sort(ids.begin(), ids.end(), [&](const std::string& a, const std::string& b) {
        return std::pair(definitions.at(a).name, a) < std::pair(definitions.at(b).name, b);
    });
    return ids;
}

}

CharacterDraft::CharacterDraft(const yh::Ruleset& rules, const yh::Compendium& compendium) : rules_(rules), compendium_(compendium)
{
    choices_.ruleset = rules.id;
    const std::vector<std::string> classes = classIds();
    // The first class a new player sees: the plainest one there is, if the ruleset has it.
    const std::string first = compendium.characterClass("fighter") ? "fighter" : classes.empty() ? std::string() : classes.front();
    choices_.levels.push_back({first, {}});
    yh::Random unused(1);
    setMethod("array", unused);
}

CharacterDraft CharacterDraft::levelUp(const yh::Ruleset& rules, const yh::Compendium& compendium, yh::CharacterChoices choices)
{
    CharacterDraft draft(rules, compendium);
    draft.levellingUp_ = true;
    draft.step = steps - 1;
    draft.choices_ = std::move(choices);
    draft.choices_.levels.push_back({draft.choices_.levels.back().classId, {}});
    draft.rebuild();
    return draft;
}

bool CharacterDraft::stepDone(int which) const
{
    return stepProblem(which).empty();
}

std::string CharacterDraft::stepProblem(int which) const
{
    if (which == 0 && !levellingUp_)
    {
        if (choices_.name.empty()) return "Give the character a name.";
        if (!compendium_.races.empty() && !compendium_.race(choices_.race)) return "Pick a race.";
        if (!compendium_.backgrounds.empty() && !compendium_.background(choices_.background)) return "Pick a background.";
    }
    if (which == 1 && !levellingUp_)
    {
        if (!compendium_.characterClass(level().classId)) return "Pick a class.";
        std::string error;
        if (!choices_.check(rules_, &error)) return error;
    }
    if (which == 2)
    {
        if (!compendium_.characterClass(level().classId)) return "Pick a class.";
        const size_t skills = std::min(static_cast<size_t>(std::max(0, skillPicks())), skillOptions_.size());
        if (picked("skills").size() < skills)
        {
            const size_t more = skills - picked("skills").size();
            return "Pick " + std::to_string(more) + " more skill" + (more == 1 ? "." : "s.");
        }
        for (const std::string& kind : featKinds())
        {
            const std::vector<std::string>& feats = picked("feats");
            const bool chosen = std::any_of(feats.begin(), feats.end(), [&](const std::string& id) {
                const yh::FeatDefinition* feat = compendium_.feat(id);
                return feat && feat->kind == kind;
            });
            if (!chosen && !featOptions(kind).empty()) return "Pick a " + kind + " feat.";
        }
        if (!sheet_) return problem_;
    }
    return {};
}

bool CharacterDraft::finished() const
{
    for (int i = 0; i < steps; i++)
        if (!stepDone(i)) return false;
    return sheet_.has_value();
}

void CharacterDraft::setName(std::string name)
{
    choices_.name = std::move(name);
    rebuild();
}

void CharacterDraft::setRace(std::string id)
{
    choices_.race = std::move(id);
    rebuild();
}

void CharacterDraft::setBackground(std::string id)
{
    choices_.background = std::move(id);
    rebuild();
}

void CharacterDraft::setClass(std::string id)
{
    level() = {std::move(id), {}};
    rebuild();
}

std::vector<int> CharacterDraft::arrayValues() const
{
    std::vector<int> values = rules_.scoreMethods.standardArray;
    values.resize(rules_.abilities.size(), rules_.scoreMin); // a ruleset whose array doesn't fit still gets scores
    return values;
}

void CharacterDraft::setMethod(const std::string& method, yh::Random& random)
{
    choices_.scoreMethod = method;
    choices_.scores.clear();
    if (method == "roll")
    {
        reroll(random);
        return;
    }
    const std::vector<int> values = arrayValues();
    const int cheapest = rules_.scoreMethods.pointCosts.empty() ? rules_.scoreMin : rules_.scoreMethods.pointCosts.begin()->first;
    for (size_t i = 0; i < rules_.abilities.size(); i++)
        choices_.scores[rules_.abilities[i].id] = method == "pointBuy" ? cheapest : values[i];
    rebuild();
}

void CharacterDraft::reroll(yh::Random& random)
{
    choices_.scoreMethod = "roll";
    choices_.scores = yh::rollChoices(rules_, choices_.name, level().classId, random).scores;
    rebuild();
}

bool CharacterDraft::canRaise(const std::string& ability) const
{
    const auto score = choices_.scores.find(ability);
    if (score == choices_.scores.end()) return false;
    if (choices_.scoreMethod == "pointBuy")
    {
        const auto& costs = rules_.scoreMethods.pointCosts;
        const auto now = costs.find(score->second), next = costs.upper_bound(score->second);
        return now != costs.end() && next != costs.end() && next->second - now->second <= pointsLeft();
    }
    if (choices_.scoreMethod == "array")
        return std::any_of(choices_.scores.begin(), choices_.scores.end(), [&](const auto& other) { return other.second > score->second; });
    return false;
}

bool CharacterDraft::canLower(const std::string& ability) const
{
    const auto score = choices_.scores.find(ability);
    if (score == choices_.scores.end()) return false;
    if (choices_.scoreMethod == "pointBuy")
    {
        const auto& costs = rules_.scoreMethods.pointCosts;
        return costs.contains(score->second) && costs.begin()->first < score->second;
    }
    if (choices_.scoreMethod == "array")
        return std::any_of(choices_.scores.begin(), choices_.scores.end(), [&](const auto& other) { return other.second < score->second; });
    return false;
}

void CharacterDraft::raise(const std::string& ability)
{
    if (!canRaise(ability)) return;
    int& score = choices_.scores.at(ability);
    if (choices_.scoreMethod == "pointBuy")
        score = rules_.scoreMethods.pointCosts.upper_bound(score)->first;
    else
    {
        // Swap with whoever holds the next value up.
        auto swapWith = choices_.scores.end();
        for (auto it = choices_.scores.begin(); it != choices_.scores.end(); ++it)
            if (it->second > score && (swapWith == choices_.scores.end() || it->second < swapWith->second)) swapWith = it;
        std::swap(score, swapWith->second);
    }
    rebuild();
}

void CharacterDraft::lower(const std::string& ability)
{
    if (!canLower(ability)) return;
    int& score = choices_.scores.at(ability);
    if (choices_.scoreMethod == "pointBuy")
        score = std::prev(rules_.scoreMethods.pointCosts.find(score))->first;
    else
    {
        auto swapWith = choices_.scores.end();
        for (auto it = choices_.scores.begin(); it != choices_.scores.end(); ++it)
            if (it->second < score && (swapWith == choices_.scores.end() || it->second > swapWith->second)) swapWith = it;
        std::swap(score, swapWith->second);
    }
    rebuild();
}

int CharacterDraft::pointsLeft() const
{
    const int cost = yh::pointBuyCost(rules_, choices_.scores);
    return cost < 0 ? 0 : rules_.scoreMethods.pointBudget - cost;
}

const yh::ClassLevel* CharacterDraft::row() const
{
    const yh::ClassDefinition* definition = compendium_.characterClass(level().classId);
    if (!definition) return nullptr;
    const auto classLevel = std::count_if(choices_.levels.begin(), choices_.levels.end(), [&](const yh::LevelChoice& l) { return l.classId == definition->id; });
    return classLevel >= 1 && static_cast<size_t>(classLevel) <= definition->levels.size() ? &definition->levels[classLevel - 1] : nullptr;
}

int CharacterDraft::skillPicks() const
{
    const yh::ClassLevel* r = row();
    return r ? r->skills : 0;
}

std::vector<std::string> CharacterDraft::featKinds() const
{
    const yh::ClassLevel* r = row();
    return r ? r->feats : std::vector<std::string>{};
}

std::vector<std::string> CharacterDraft::featOptions(const std::string& kind) const
{
    const auto found = featOptions_.find(kind);
    return found == featOptions_.end() ? std::vector<std::string>{} : found->second;
}

const std::vector<std::string>& CharacterDraft::picked(const std::string& kind) const
{
    static const std::vector<std::string> none;
    const auto found = level().picks.find(kind);
    return found == level().picks.end() ? none : found->second;
}

void CharacterDraft::toggleSkill(const std::string& id)
{
    std::vector<std::string>& skills = level().picks["skills"];
    if (const auto found = std::find(skills.begin(), skills.end(), id); found != skills.end())
        skills.erase(found);
    else if (static_cast<int>(skills.size()) < skillPicks())
        skills.push_back(id);
    if (skills.empty())
        level().picks.erase("skills");
    rebuild();
}

void CharacterDraft::pickFeat(const std::string& id)
{
    const yh::FeatDefinition* feat = compendium_.feat(id);
    if (!feat) return;
    std::vector<std::string>& feats = level().picks["feats"];
    const auto same = std::find(feats.begin(), feats.end(), id);
    const bool wasPicked = same != feats.end();
    // One feat per kind the level offers: the new one replaces any of its kind.
    std::erase_if(feats, [&](const std::string& other) {
        const yh::FeatDefinition* o = compendium_.feat(other);
        return !o || o->kind == feat->kind;
    });
    if (!wasPicked)
        feats.push_back(id);
    if (feats.empty())
        level().picks.erase("feats");
    rebuild();
}

std::vector<std::string> CharacterDraft::raceIds() const
{
    return idsByName(compendium_.races);
}

std::vector<std::string> CharacterDraft::backgroundIds() const
{
    return idsByName(compendium_.backgrounds);
}

std::vector<std::string> CharacterDraft::classIds() const
{
    return idsByName(compendium_.classes);
}

void CharacterDraft::rebuild()
{
    problem_.clear();
    sheet_.reset();
    skillOptions_.clear();
    featOptions_.clear();
    yh::CharacterChoices built = choices_;
    if (built.name.empty())
        built.name = "New character"; // the sheet shows before the name is typed
    if (!built.check(rules_, &problem_))
        return;
    sheet_ = compendium_.build(rules_, built, &problem_);

    // Each feat this level could take, tried in place of the one of its kind: the rules decide.
    // Feats taken at earlier levels aren't offered again unless they can be.
    std::set<std::string> taken;
    for (size_t i = 0; i + 1 < choices_.levels.size(); i++)
        if (const auto feats = choices_.levels[i].picks.find("feats"); feats != choices_.levels[i].picks.end())
            taken.insert(feats->second.begin(), feats->second.end());
    for (const std::string& kind : featKinds())
        for (const std::string& id : idsByName(compendium_.feats))
        {
            const yh::FeatDefinition& feat = compendium_.feats.at(id);
            if (feat.kind != kind || (!feat.repeatable && taken.contains(id)))
                continue;
            yh::CharacterChoices trial = built;
            std::vector<std::string>& feats = trial.levels.back().picks["feats"];
            std::erase_if(feats, [&](const std::string& other) {
                const yh::FeatDefinition* o = compendium_.feat(other);
                return !o || o->kind == kind;
            });
            feats.push_back(id);
            if (compendium_.build(rules_, trial))
                featOptions_[kind].push_back(id);
        }
    // What this level could train: every skill the character doesn't have without its picks.
    built.levels.back().picks.erase("skills");
    if (const std::optional<yh::Character> without = compendium_.build(rules_, built))
        for (const yh::SkillDefinition& skill : rules_.skills)
        {
            const auto rank = without->proficiencyRanks.find(skill.id);
            const bool ranked = rank != without->proficiencyRanks.end() && rank->second != rules_.untrainedRank;
            if (!without->proficiencies.contains(skill.id) && !ranked)
                skillOptions_.push_back(skill.id);
        }
}
