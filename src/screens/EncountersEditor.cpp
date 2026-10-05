// Encounters mode of the Create screen: the commands that change a chapter's groups, and the
// desktop layout over them.

#include "EncountersEditor.h"

#include <nlohmann/json.hpp>
#include <SDL3/SDL_keycode.h>

#include <algorithm>
#include <charconv>
#include <cmath>
#include <initializer_list>
#include <memory>
#include <numbers>
#include <stdexcept>

namespace
{

// Ordered, so a hand-written chapter keeps its fields where the writer put them.
using nlohmann::ordered_json;

constexpr float cell = GameMap::cellSize;
constexpr int maxXp = 1000000;

yh::Cell cellFrom(const ordered_json& j)
{
    if (!j.is_array() || j.size() != 2 || !j[0].is_number_integer() || !j[1].is_number_integer())
        throw std::invalid_argument("cells are [x, y]");
    return {j[0].get<int>(), j[1].get<int>()};
}

yh::Color colorFrom(const ordered_json& entry, yh::Color fallback)
{
    if (!entry.contains("color"))
        return fallback;
    const ordered_json& j = entry.at("color");
    if (!j.is_array() || (j.size() != 3 && j.size() != 4))
        return fallback;
    auto channel = [&](size_t i) { return static_cast<uint8_t>(std::clamp(j[i].get<int>(), 0, 255)); };
    return {channel(0), channel(1), channel(2), j.size() == 4 ? channel(3) : uint8_t(255)};
}

// The fields of `entry` that aren't in `known`, as a JSON object; empty if there are none.
std::string extraOf(const ordered_json& entry, std::initializer_list<std::string_view> known)
{
    ordered_json rest = ordered_json::object();
    for (const auto& [key, value] : entry.items())
        if (std::find(known.begin(), known.end(), std::string_view(key)) == known.end())
            rest[key] = value;
    return rest.empty() ? std::string() : rest.dump();
}

void addExtra(ordered_json& entry, const std::string& extra)
{
    if (extra.empty())
        return;
    const ordered_json rest = ordered_json::parse(extra);
    for (const auto& [key, value] : rest.items())
        entry[key] = value;
}

// The chapter's aiChanges with the encounter `from` called `to`, or, with no `to`, without the
// entries that name it.
std::string retarget(const std::string& changes, const std::string& from, const std::string& to)
{
    if (changes.empty())
        return changes;
    const ordered_json before = ordered_json::parse(changes);
    ordered_json after = ordered_json::array();
    for (const ordered_json& change : before)
    {
        const bool names = change.is_object() && change.value("encounter", std::string()) == from;
        if (names && to.empty())
            continue;
        after.push_back(change);
        if (names)
            after.back()["encounter"] = to;
    }
    return after.empty() ? std::string() : after.dump();
}

}

// ---------------------------------------------------------------- the groups and their commands

EncountersEditor::Catalog EncountersEditor::Catalog::from(const yh::Compendium& compendium)
{
    Catalog catalog;
    for (const auto& [id, creature] : compendium.creatures)
        catalog.creatures[id] = {creature.name.empty() ? id : creature.name, creature.level, creature.token.color, creature.token.size};
    for (const auto& [id, profile] : compendium.ai)
        catalog.ai.insert(id);
    for (const auto& [id, item] : compendium.items)
        catalog.items[id] = item.name.empty() ? id : item.name;
    return catalog;
}

bool EncountersEditor::load(std::string_view text, std::string* error, Catalog catalog, Walkable walkable)
{
    if (error)
        error->clear();
    try
    {
        const ordered_json j = ordered_json::parse(text);
        if (!j.is_object())
            throw std::invalid_argument("a chapter is an object");
        State state;
        state.chapterXp = j.value("xpPerVictory", 0);
        if (j.contains("aiChanges") && !j.at("aiChanges").empty())
            state.aiChanges = j.at("aiChanges").dump();

        std::set<std::string> ids;
        const ordered_json encounters = j.value("encounters", ordered_json::array());
        if (!encounters.is_array())
            throw std::invalid_argument("encounters must be an array");
        for (const ordered_json& e : encounters)
        {
            Group group;
            group.id = e.value("id", "encounter " + std::to_string(state.groups.size() + 1));
            if (group.id.empty() || !ids.insert(group.id).second)
                throw std::invalid_argument("duplicate encounter " + group.id);
            group.text = e.value("text", std::string());
            group.set = e.value("set", std::vector<std::string>{});
            if (e.contains("ai"))
                group.ai = e.at("ai").dump();
            if (e.contains("xp"))
                group.xp = e.at("xp").get<int>();
            if (e.contains("loot"))
            {
                std::string problem;
                std::optional<yh::LootTable> loot = yh::LootTable::fromJson(e.at("loot").dump(), &problem);
                if (!loot)
                    throw std::invalid_argument(group.id + " loot: " + problem);
                group.loot = std::move(*loot);
            }
            group.extra = extraOf(e, {"id", "text", "creatures", "set", "ai", "xp", "loot"});
            const ordered_json creatures = e.value("creatures", ordered_json::array());
            if (!creatures.is_array())
                throw std::invalid_argument("encounter creatures must be an array");
            for (const ordered_json& p : creatures)
            {
                Placement placement;
                placement.creature = p.at("creature").get<std::string>();
                placement.name = p.value("name", std::string());
                placement.at = cellFrom(p.at("at"));
                if (p.contains("facing"))
                    placement.facing = p.at("facing").get<float>();
                if (p.contains("ai"))
                    placement.ai = p.at("ai").dump();
                placement.extra = extraOf(p, {"creature", "name", "at", "facing", "ai"});
                group.creatures.push_back(std::move(placement));
            }
            state.groups.push_back(std::move(group));
        }

        // Everyone else with a cell of their own.
        std::vector<Fixed> fixed;
        auto others = [&](const char* key, Fixed::Kind kind, yh::Color color, const char* unnamed) {
            const ordered_json list = j.value(key, ordered_json::array());
            if (!list.is_array())
                return;
            for (const ordered_json& entry : list)
                if (entry.is_object() && entry.contains("at"))
                    fixed.push_back({kind, entry.value("name", std::string(unnamed)), cellFrom(entry.at("at")), colorFrom(entry, color)});
        };
        others("party", Fixed::Kind::Hero, {200, 200, 210, 255}, "Hero");
        others("npcs", Fixed::Kind::Npc, {200, 180, 140, 255}, "NPC");
        others("containers", Fixed::Kind::Chest, {200, 160, 70, 255}, "Chest");

        document_ = std::string(text);
        state_ = std::move(state);
        catalog_ = std::move(catalog);
        walkable_ = std::move(walkable);
        fixed_ = std::move(fixed);
        loaded_ = true;
        return true;
    }
    catch (const std::exception& e)
    {
        if (error)
            *error = e.what();
        return false;
    }
}

std::string EncountersEditor::toJson() const
{
    if (!loaded_)
        return "{}";
    ordered_json j = ordered_json::parse(document_);
    ordered_json encounters = ordered_json::array();
    std::string changes = state_.aiChanges;
    for (const Group& group : state_.groups)
    {
        if (group.creatures.empty())
        {
            changes = retarget(changes, group.id, {});
            continue;
        }
        ordered_json e;
        e["id"] = group.id;
        if (!group.set.empty())
            e["set"] = group.set;
        if (!group.text.empty())
            e["text"] = group.text;
        if (group.xp)
            e["xp"] = *group.xp;
        if (!group.ai.empty())
            e["ai"] = ordered_json::parse(group.ai);
        if (!group.loot.empty())
            e["loot"] = ordered_json::parse(group.loot.toJson());
        addExtra(e, group.extra);
        ordered_json creatures = ordered_json::array();
        for (const Placement& placement : group.creatures)
        {
            ordered_json p;
            p["creature"] = placement.creature;
            if (!placement.name.empty())
                p["name"] = placement.name;
            if (placement.facing)
                p["facing"] = *placement.facing;
            p["at"] = {placement.at.x, placement.at.y};
            if (!placement.ai.empty())
                p["ai"] = ordered_json::parse(placement.ai);
            addExtra(p, placement.extra);
            creatures.push_back(std::move(p));
        }
        e["creatures"] = std::move(creatures);
        encounters.push_back(std::move(e));
    }
    j["encounters"] = std::move(encounters);
    if (state_.chapterXp != 0 || j.contains("xpPerVictory"))
        j["xpPerVictory"] = state_.chapterXp;
    if (!changes.empty())
        j["aiChanges"] = ordered_json::parse(changes);
    else if (j.contains("aiChanges"))
        j.erase("aiChanges");
    return j.dump(2);
}

template <typename Change>
void EncountersEditor::edit(std::string_view label, Change change, std::string_view mergeKey)
{
    const auto before = std::make_shared<const State>(state_);
    change();
    const auto after = std::make_shared<const State>(state_);
    history_.record(label, [this, after] { state_ = *after; }, [this, before] { state_ = *before; }, mergeKey);
}

EncountersEditor::Placement* EncountersEditor::placement(size_t group, size_t index)
{
    if (!loaded_ || group >= state_.groups.size() || index >= state_.groups[group].creatures.size())
        return nullptr;
    return &state_.groups[group].creatures[index];
}

int EncountersEditor::xpOf(size_t group) const
{
    return group < state_.groups.size() && state_.groups[group].xp ? *state_.groups[group].xp : state_.chapterXp;
}

int EncountersEditor::proposedXp(size_t group) const
{
    if (group >= state_.groups.size())
        return 0;
    int levels = 0;
    for (const Placement& p : state_.groups[group].creatures)
    {
        const auto found = catalog_.creatures.find(p.creature);
        levels += found == catalog_.creatures.end() ? 1 : std::max(1, found->second.level);
    }
    return std::min(maxXp, levels * catalog_.xpPerLevel);
}

float EncountersEditor::facingOf(const Placement& p) const
{
    if (p.facing)
        return *p.facing;
    // Nobody told it where to look: it watches the way the party comes from, as in the game.
    for (const Fixed& other : fixed_)
    {
        if (other.kind != Fixed::Kind::Hero)
            continue;
        if (other.at == p.at)
            return 0;
        const float degrees = std::atan2(static_cast<float>(other.at.y - p.at.y), static_cast<float>(other.at.x - p.at.x)) * 180 / std::numbers::pi_v<float>;
        return degrees < 0 ? degrees + 360 : degrees;
    }
    return 0;
}

std::string EncountersEditor::profileOf(const std::string& ai)
{
    if (ai.empty())
        return {};
    const ordered_json j = ordered_json::parse(ai, nullptr, false);
    return j.is_string() ? j.get<std::string>() : std::string("custom");
}

std::optional<std::pair<size_t, size_t>> EncountersEditor::creatureAt(yh::Cell at) const
{
    for (size_t g = 0; g < state_.groups.size(); g++)
        for (size_t c = 0; c < state_.groups[g].creatures.size(); c++)
            if (state_.groups[g].creatures[c].at == at)
                return std::pair{g, c};
    return std::nullopt;
}

bool EncountersEditor::free(yh::Cell at) const
{
    if (!loaded_ || (walkable_ ? !walkable_(at) : at.x < 0 || at.y < 0) || creatureAt(at))
        return false;
    return std::none_of(fixed_.begin(), fixed_.end(), [&](const Fixed& other) { return other.at == at; });
}

std::optional<size_t> EncountersEditor::addGroup(std::string id)
{
    auto taken = [&](const std::string& name) {
        return std::any_of(state_.groups.begin(), state_.groups.end(), [&](const Group& g) { return g.id == name; });
    };
    if (!loaded_ || id.size() > 64 || (!id.empty() && taken(id)))
        return std::nullopt;
    for (size_t n = state_.groups.size() + 1; id.empty(); n++)
        if (!taken("encounter-" + std::to_string(n)))
            id = "encounter-" + std::to_string(n);
    edit("Add group", [&] {
        Group group;
        group.id = std::move(id);
        state_.groups.push_back(std::move(group));
    });
    return state_.groups.size() - 1;
}

bool EncountersEditor::removeGroup(size_t group)
{
    if (!loaded_ || group >= state_.groups.size())
        return false;
    edit("Remove group", [&] {
        state_.aiChanges = retarget(state_.aiChanges, state_.groups[group].id, {});
        state_.groups.erase(state_.groups.begin() + static_cast<std::ptrdiff_t>(group));
    });
    return true;
}

bool EncountersEditor::setGroupId(size_t group, const std::string& id)
{
    if (!loaded_ || group >= state_.groups.size() || id.empty() || id.size() > 64
        || std::any_of(state_.groups.begin(), state_.groups.end(), [&](const Group& g) { return g.id == id; }))
        return false;
    edit("Rename group", [&] {
        state_.aiChanges = retarget(state_.aiChanges, state_.groups[group].id, id);
        state_.groups[group].id = id;
    }, "group-id-" + std::to_string(group));
    return true;
}

bool EncountersEditor::setGroupText(size_t group, const std::string& text)
{
    if (!loaded_ || group >= state_.groups.size() || text.size() > 400 || state_.groups[group].text == text)
        return false;
    edit("Change group line", [&] { state_.groups[group].text = text; }, "group-text-" + std::to_string(group));
    return true;
}

bool EncountersEditor::setGroupFlags(size_t group, const std::vector<std::string>& flags)
{
    if (!loaded_ || group >= state_.groups.size() || state_.groups[group].set == flags
        || std::any_of(flags.begin(), flags.end(), [](const std::string& f) { return f.empty() || f.size() > 64; }))
        return false;
    edit("Change group flags", [&] { state_.groups[group].set = flags; }, "group-flags-" + std::to_string(group));
    return true;
}

bool EncountersEditor::setGroupAi(size_t group, std::string_view profile)
{
    if (!loaded_ || group >= state_.groups.size() || (!profile.empty() && !catalog_.ai.contains(profile)))
        return false;
    const std::string ai = profile.empty() ? std::string() : ordered_json(std::string(profile)).dump();
    if (state_.groups[group].ai == ai)
        return false;
    edit("Change group AI", [&] { state_.groups[group].ai = ai; });
    return true;
}

bool EncountersEditor::setGroupXp(size_t group, std::optional<int> xp)
{
    if (!loaded_ || group >= state_.groups.size() || state_.groups[group].xp == xp || (xp && (*xp < 0 || *xp > maxXp)))
        return false;
    edit("Change group XP", [&] { state_.groups[group].xp = xp; }, "group-xp-" + std::to_string(group));
    return true;
}

bool EncountersEditor::setGroupLoot(size_t group, const yh::LootTable& loot)
{
    if (!loaded_ || group >= state_.groups.size())
        return false;
    // Through the file's own reader, so dice, chances and quantities are checked the way the game will.
    const std::string text = loot.toJson();
    if (text == state_.groups[group].loot.toJson() || !yh::LootTable::fromJson(text)
        || std::any_of(loot.items.begin(), loot.items.end(), [&](const yh::LootEntry& entry) { return !catalog_.items.contains(entry.item); }))
        return false;
    edit("Change group loot", [&] { state_.groups[group].loot = loot; }, "group-loot-" + std::to_string(group));
    return true;
}

bool EncountersEditor::setChapterXp(int xp)
{
    if (!loaded_ || xp < 0 || xp > maxXp || state_.chapterXp == xp)
        return false;
    edit("Change chapter XP", [&] { state_.chapterXp = xp; }, "chapter-xp");
    return true;
}

std::optional<size_t> EncountersEditor::addCreature(size_t group, std::string_view creature, yh::Cell at)
{
    const auto found = catalog_.creatures.find(creature);
    if (!loaded_ || group >= state_.groups.size() || found == catalog_.creatures.end() || !free(at))
        return std::nullopt;
    edit("Place " + found->second.name, [&] {
        Placement p;
        p.creature = found->first;
        p.at = at;
        state_.groups[group].creatures.push_back(std::move(p));
    });
    return state_.groups[group].creatures.size() - 1;
}

bool EncountersEditor::moveCreature(size_t group, size_t index, yh::Cell at)
{
    Placement* p = placement(group, index);
    if (!p || p->at == at || !free(at))
        return false;
    edit("Move creature", [&] { p->at = at; });
    return true;
}

bool EncountersEditor::removeCreature(size_t group, size_t index)
{
    if (!placement(group, index))
        return false;
    edit("Remove creature", [&] {
        std::vector<Placement>& creatures = state_.groups[group].creatures;
        creatures.erase(creatures.begin() + static_cast<std::ptrdiff_t>(index));
    });
    return true;
}

bool EncountersEditor::setCreatureName(size_t group, size_t index, const std::string& name)
{
    Placement* p = placement(group, index);
    if (!p || name.size() > 64 || p->name == name)
        return false;
    edit("Rename creature", [&] { p->name = name; }, "creature-name-" + std::to_string(group) + "-" + std::to_string(index));
    return true;
}

bool EncountersEditor::setFacing(size_t group, size_t index, std::optional<float> degrees)
{
    Placement* p = placement(group, index);
    if (!p || p->facing == degrees || (degrees && (!std::isfinite(*degrees) || std::fabs(*degrees) > 360)))
        return false;
    edit("Turn creature", [&] { p->facing = degrees; });
    return true;
}

bool EncountersEditor::setCreatureAi(size_t group, size_t index, std::string_view profile)
{
    Placement* p = placement(group, index);
    if (!p || (!profile.empty() && !catalog_.ai.contains(profile)))
        return false;
    const std::string ai = profile.empty() ? std::string() : ordered_json(std::string(profile)).dump();
    if (p->ai == ai)
        return false;
    edit("Change creature AI", [&] { p->ai = ai; });
    return true;
}

std::optional<size_t> EncountersEditor::moveToGroup(size_t group, size_t index, size_t toGroup)
{
    if (!placement(group, index) || toGroup >= state_.groups.size() || toGroup == group)
        return std::nullopt;
    edit("Move to " + state_.groups[toGroup].id, [&] {
        std::vector<Placement>& from = state_.groups[group].creatures;
        state_.groups[toGroup].creatures.push_back(std::move(from[index]));
        from.erase(from.begin() + static_cast<std::ptrdiff_t>(index));
    });
    return state_.groups[toGroup].creatures.size() - 1;
}

std::vector<EncountersEditor::Problem> EncountersEditor::problems() const
{
    std::vector<Problem> out;
    if (!loaded_)
        return out;
    auto knownAi = [&](const std::string& ai) {
        const std::string profile = profileOf(ai);
        return profile.empty() || profile == "custom" || catalog_.ai.contains(profile);
    };
    std::vector<yh::Cell> taken;
    for (const Fixed& other : fixed_)
        taken.push_back(other.at);
    for (const Group& group : state_.groups)
    {
        if (group.creatures.empty())
            out.push_back({group.id + " has no creatures and won't be saved", false});
        if (!knownAi(group.ai))
            out.push_back({group.id + " names the AI profile " + profileOf(group.ai) + ", which isn't in this package"});
        for (const yh::LootEntry& entry : group.loot.items)
            if (!catalog_.items.contains(entry.item))
                out.push_back({group.id + " loot has the unknown item " + entry.item});
        for (const Placement& p : group.creatures)
        {
            const std::string who = (p.name.empty() ? p.creature : p.name) + " (" + group.id + ")";
            if (!catalog_.creatures.contains(p.creature))
                out.push_back({who + " is the unknown creature " + p.creature});
            if (!knownAi(p.ai))
                out.push_back({who + " names the AI profile " + profileOf(p.ai) + ", which isn't in this package"});
            if (walkable_ ? !walkable_(p.at) : p.at.x < 0 || p.at.y < 0)
                out.push_back({who + " is on a cell nobody can stand on"});
            if (std::find(taken.begin(), taken.end(), p.at) != taken.end())
                out.push_back({who + " shares a cell with someone"});
            taken.push_back(p.at);
        }
    }
    return out;
}

// ---------------------------------------------------------------- the desktop layout

namespace
{

constexpr float toolColumn = 172, propertyColumn = 252;
constexpr float minZoom = 0.02f, maxZoom = 3.0f;

const char* const textBoxes[] = {"enc-id", "enc-line", "enc-flags", "enc-xp", "enc-chapter-xp", "enc-coins", "enc-name"};

// One per group, round again after the last.
const yh::Color groupColors[] = {
    {240, 120, 90, 255}, {90, 180, 240, 255}, {240, 200, 80, 255}, {170, 120, 230, 255}, {110, 210, 140, 255}, {240, 140, 200, 255},
};

yh::Color colorOf(size_t group)
{
    return groupColors[group % std::size(groupColors)];
}

// A text box over a value the editor holds: it shows the value unless it is being typed in.
// True when what is typed differs from the value.
bool field(yh::Ui& ui, std::string_view id, const yh::Rect& box, std::string& typed, const std::string& value, size_t maxBytes)
{
    if (!ui.editing(id))
        typed = value;
    ui.textBox(id, box, typed, maxBytes);
    return typed != value;
}

// "<  text  >": -1 or 1 when an arrow is clicked.
int stepper(yh::Ui& ui, const yh::Rect& row, std::string_view text)
{
    int step = 0;
    if (ui.button({row.x, row.y, 28, row.h}, "<"))
        step = -1;
    if (ui.button({row.x + row.w - 28, row.y, 28, row.h}, ">"))
        step = 1;
    ui.label({row.x + 36, row.y + 5}, text);
    return step;
}

// The name `step` places along from `current` in `names`, round at the ends. A name that isn't
// there counts as the first.
template <typename Names>
std::string stepName(const Names& names, const std::string& current, int step)
{
    std::vector<std::string> all(names.begin(), names.end());
    if (all.empty())
        return current;
    const auto found = std::find(all.begin(), all.end(), current);
    const size_t index = found == all.end() ? 0 : static_cast<size_t>(found - all.begin());
    return all[(index + all.size() + static_cast<size_t>(step + static_cast<int>(all.size()))) % all.size()];
}

// The AI profiles an entry can be given, with "" (none) first.
std::vector<std::string> profiles(const EncountersEditor::Catalog& catalog)
{
    std::vector<std::string> names{std::string()};
    names.insert(names.end(), catalog.ai.begin(), catalog.ai.end());
    return names;
}

std::optional<int> wholeNumber(const std::string& text)
{
    int value = 0;
    const auto [end, problem] = std::from_chars(text.data(), text.data() + text.size(), value);
    return problem == std::errc() && end == text.data() + text.size() && !text.empty() ? std::optional<int>(value) : std::nullopt;
}

// "a, b" to its names, blanks dropped.
std::vector<std::string> flagsFrom(const std::string& text)
{
    std::vector<std::string> flags;
    std::string flag;
    auto finish = [&] {
        const size_t first = flag.find_first_not_of(' '), last = flag.find_last_not_of(' ');
        if (first != std::string::npos)
            flags.push_back(flag.substr(first, last - first + 1));
        flag.clear();
    };
    for (const char c : text)
    {
        if (c == ',')
            finish();
        else
            flag += c;
    }
    finish();
    return flags;
}

std::string joined(const std::vector<std::string>& flags)
{
    std::string text;
    for (const std::string& flag : flags)
        text += (text.empty() ? "" : ", ") + flag;
    return text;
}

}

bool EncountersPanel::typing(const yh::Ui& ui)
{
    return std::any_of(std::begin(textBoxes), std::end(textBoxes), [&](const char* id) { return ui.editing(id); });
}

yh::Vec2 EncountersPanel::toWorld(yh::Vec2 screen, const yh::Rect& view) const
{
    return (screen - view.position()) / zoom_ + pan_;
}

void EncountersPanel::draw(EncountersEditor& editor, GameMap& map, yh::Ui& ui, const yh::Input& input, yh::Renderer& renderer, const yh::Rect& area)
{
    if (!editor.loaded())
        return;
    const yh::Rect left{area.x, area.y, toolColumn, area.h};
    const yh::Rect right{area.x + area.w - propertyColumn, area.y, propertyColumn, area.h};
    const yh::Rect view{left.x + left.w, area.y, area.w - left.w - right.w, area.h};

    // An undo can take away the group or creature that was selected.
    const std::vector<EncountersEditor::Group>& groups = editor.groups();
    if (group_ >= groups.size())
    {
        group_ = groups.empty() ? 0 : groups.size() - 1;
        creature_.reset();
    }
    if (creature_ && (groups.empty() || *creature_ >= groups[group_].creatures.size()))
        creature_.reset();
    if (!creature_)
        dragging_ = false;
    const EncountersEditor::Catalog& catalog = editor.catalog();
    if (!catalog.creatures.contains(kind_))
        kind_ = catalog.creatures.empty() ? std::string() : catalog.creatures.begin()->first;
    if (!catalog.items.contains(lootItem_))
        lootItem_ = catalog.items.empty() ? std::string() : catalog.items.begin()->first;

    if (zoom_ <= 0 && view.w > 0 && view.h > 0)
    {
        const yh::Rect world = map.map().worldBounds();
        zoom_ = std::clamp(std::min(view.w / world.w, view.h / world.h) * 0.94f, minZoom, maxZoom);
        pan_ = {(world.w - view.w / zoom_) / 2, (world.h - view.h / zoom_) / 2};
    }

    const bool nowTyping = typing(ui);
    if (wasTyping_ && !nowTyping)
        editor.endTyping();
    wasTyping_ = nowTyping;

    useTool(editor, map, ui, input, view);
    drawMap(editor, map, ui, renderer, view);
    ui.panel(left);
    drawTools(editor, ui, left);
    ui.panel(right);
    if (tool_ == Tool::Place)
        drawPalette(editor, ui, right);
    else if (creature_)
        drawCreature(editor, ui, right);
    else
        drawGroup(editor, ui, right);
    if (!hint_.empty())
        ui.label({right.x + 8, right.y + right.h - 26}, hint_, ui.theme.bad);
}

void EncountersPanel::useTool(EncountersEditor& editor, GameMap& map, yh::Ui& ui, const yh::Input& input, const yh::Rect& view)
{
    using yh::MouseButton;
    const yh::Vec2 mouse = input.mouse();
    const bool over = input.mouseInside() && view.contains(mouse);
    const bool busy = typing(ui);
    hover_.reset();
    const yh::Vec2 world = toWorld(mouse, view);
    const yh::Cell at{static_cast<int>(std::floor(world.x / cell)), static_cast<int>(std::floor(world.y / cell))};
    if (over && map.inside(at))
        hover_ = at;

    // The view, as in map mode: the wheel zooms about the cursor, the middle button and the arrow keys move it.
    if (over && input.wheel() != 0)
    {
        zoom_ = std::clamp(zoom_ * std::pow(1.15f, input.wheel()), minZoom, maxZoom);
        pan_ = pan_ + world - toWorld(mouse, view);
    }
    if (input.buttonDown(MouseButton::Middle) && view.contains(input.buttonPressPosition(MouseButton::Middle)))
        pan_ = pan_ - input.mouseDelta() / zoom_;
    if (!busy && !input.shortcutDown())
    {
        const float step = 14 / zoom_;
        if (input.keyDown(SDLK_LEFT)) pan_.x -= step;
        if (input.keyDown(SDLK_RIGHT)) pan_.x += step;
        if (input.keyDown(SDLK_UP)) pan_.y -= step;
        if (input.keyDown(SDLK_DOWN)) pan_.y += step;
    }

    const std::optional<std::pair<size_t, size_t>> under = hover_ ? editor.creatureAt(*hover_) : std::nullopt;
    if (dragging_ && !input.buttonDown(MouseButton::Left))
    {
        // Let go: the creature goes where the cursor is, if it can stand there.
        dragging_ = false;
        if (creature_ && hover_ && !(*hover_ == editor.groups()[group_].creatures[*creature_].at))
            hint_ = editor.moveCreature(group_, *creature_, *hover_) ? "" : "Nobody can stand there";
    }
    else if (over && hover_ && input.buttonPressed(MouseButton::Left))
    {
        hint_.clear();
        if (under)
        {
            group_ = under->first;
            creature_ = under->second;
            dragging_ = true;
        }
        else if (tool_ == Tool::Select)
            creature_.reset();
        else if (kind_.empty())
            hint_ = "This package has no creatures";
        else
        {
            // The first creature of a chapter makes its first group.
            if (editor.groups().empty())
                group_ = editor.addGroup().value_or(0);
            creature_ = editor.addCreature(group_, kind_, *hover_);
            if (!creature_)
                hint_ = "Nobody can stand there";
        }
    }
    else if (over && under && input.buttonClicked(MouseButton::Right) && view.contains(input.buttonPressPosition(MouseButton::Right)))
    {
        editor.removeCreature(under->first, under->second);
        creature_.reset();
        dragging_ = false;
    }
    if (creature_ && !busy && !dragging_ && input.keyPressed(SDLK_DELETE))
    {
        editor.removeCreature(group_, *creature_);
        creature_.reset();
    }
}

void EncountersPanel::drawMap(EncountersEditor& editor, GameMap& map, yh::Ui& ui, yh::Renderer& renderer, const yh::Rect& view)
{
    map.bindTileset(renderer);
    renderer.fillRect(view, {10, 11, 16, 255});
    renderer.pushViewport(view);
    renderer.pushTransform({-pan_.x * zoom_, -pan_.y * zoom_}, zoom_);

    const yh::Rect visible{pan_.x, pan_.y, view.w / zoom_, view.h / zoom_};
    const yh::Rect bounds = map.map().worldBounds();
    const float px = 1 / zoom_; // one screen pixel in world units
    renderer.fillRect(bounds, {0, 0, 0, 255});
    // Creatures stand on floor 0, so that is the floor shown.
    map.map().draw(renderer, visible, zoom_, 0);

    const yh::Rect shown = visible.intersect(bounds);
    if (grid_ && cell * zoom_ >= 8 && shown.w > 0 && shown.h > 0)
    {
        const yh::Color line{0, 0, 0, 70};
        for (float x = std::floor(shown.x / cell) * cell; x <= shown.x + shown.w; x += cell)
            renderer.drawLine({x, shown.y}, {x, shown.y + shown.h}, line, px);
        for (float y = std::floor(shown.y / cell) * cell; y <= shown.y + shown.h; y += cell)
            renderer.drawLine({shown.x, y}, {shown.x + shown.w, y}, line, px);
    }
    map.objects().draw(renderer, 0);

    auto center = [](yh::Cell c) { return yh::Vec2{(c.x + 0.5f) * cell, (c.y + 0.5f) * cell}; };
    for (const EncountersEditor::Fixed& other : editor.fixed())
    {
        if (other.kind == EncountersEditor::Fixed::Kind::Chest)
        {
            const yh::Rect box{(other.at.x + 0.25f) * cell, (other.at.y + 0.3f) * cell, cell * 0.5f, cell * 0.4f};
            renderer.fillRect(box, other.color);
            renderer.drawRect(box, {40, 30, 10, 255}, 2 * px);
            continue;
        }
        // Heroes and NPCs are squares, so nobody takes them for something to fight.
        const yh::Rect box{(other.at.x + 0.2f) * cell, (other.at.y + 0.2f) * cell, cell * 0.6f, cell * 0.6f};
        renderer.fillRect(box, other.color);
        renderer.drawRect(box, {15, 15, 20, 255}, 2 * px);
    }

    const std::vector<EncountersEditor::Group>& groups = editor.groups();
    for (size_t g = 0; g < groups.size(); g++)
        for (size_t c = 0; c < groups[g].creatures.size(); c++)
        {
            const EncountersEditor::Placement& p = groups[g].creatures[c];
            const auto look = editor.catalog().creatures.find(p.creature);
            const bool known = look != editor.catalog().creatures.end();
            const float radius = (known ? std::clamp(look->second.size, 0.2f, 0.5f) : 0.36f) * cell;
            const yh::Vec2 middle = center(p.at);
            // The ring says which group it wakes with; the picked group's is thicker.
            // Never thinner than a few pixels, so it still reads on a big map zoomed out.
            renderer.fillCircle(middle, radius + (g == group_ ? std::max(cell * 0.12f, 4 * px) : std::max(cell * 0.07f, 2.5f * px)), colorOf(g), 32);
            renderer.fillCircle(middle, radius, known ? look->second.color : yh::Color{220, 90, 80, 255}, 32);
            // Where it looks: solid if the writer set it, faint if it only watches for the party.
            const float angle = editor.facingOf(p) * std::numbers::pi_v<float> / 180;
            const yh::Vec2 tip{middle.x + std::cos(angle) * cell * 0.8f, middle.y + std::sin(angle) * cell * 0.8f};
            renderer.drawLine(middle, tip, {0, 0, 0, 200}, 5 * px);
            renderer.drawLine(middle, tip, p.facing ? yh::Color{255, 255, 255, 255} : yh::Color{255, 255, 255, 120}, 3 * px);
            if (g == group_ && creature_ && *creature_ == c)
                renderer.drawRect({p.at.x * cell, p.at.y * cell, cell, cell}, ui.theme.accent, 3 * px);
        }

    if (hover_)
    {
        // Red where nobody could be put: while dragging one, and with the Place tool.
        const bool placing = dragging_ || tool_ == Tool::Place;
        const bool blocked = placing && !editor.free(*hover_) && !(dragging_ && creature_ && groups[group_].creatures[*creature_].at == *hover_);
        renderer.drawRect({hover_->x * cell, hover_->y * cell, cell, cell}, blocked ? ui.theme.bad : yh::Color{255, 255, 255, 220}, 2 * px);
    }
    renderer.drawRect(bounds, {150, 152, 170, 255}, 2 * px);
    renderer.pop();

    // Names, at screen size whatever the zoom.
    if (cell * zoom_ >= 14)
    {
        auto text = [&](yh::Vec2 worldAt, const std::string& words, yh::Color color) {
            const yh::Vec2 at = (worldAt - pan_) * zoom_;
            if (ui.theme.font)
            {
                ui.theme.font->draw(renderer, at + yh::Vec2{1, 1}, words, {0, 0, 0, 220});
                ui.theme.font->draw(renderer, at, words, color);
            }
            else
                renderer.drawText(at, words, color, 1.5f);
        };
        for (const EncountersEditor::Fixed& other : editor.fixed())
            text({other.at.x * cell, (other.at.y + 0.85f) * cell}, other.name, {200, 205, 220, 255});
        for (size_t g = 0; g < groups.size(); g++)
            for (const EncountersEditor::Placement& p : groups[g].creatures)
            {
                const auto look = editor.catalog().creatures.find(p.creature);
                const std::string& name = !p.name.empty() ? p.name : look != editor.catalog().creatures.end() ? look->second.name : p.creature;
                text({p.at.x * cell, (p.at.y + 0.95f) * cell}, name, colorOf(g));
            }
    }
    renderer.pop();
    renderer.drawRect(view, ui.theme.panelBorder, 1);
}

void EncountersPanel::drawTools(EncountersEditor& editor, yh::Ui& ui, const yh::Rect& column)
{
    const float x = column.x + 8, w = column.w - 16, h = 30;
    float y = column.y + 8;
    if (ui.toggle({x, y, w / 2 - 2, h}, "Select", tool_ == Tool::Select))
        tool_ = Tool::Select;
    if (ui.toggle({x + w / 2 + 2, y, w / 2 - 2, h}, "Place", tool_ == Tool::Place))
        tool_ = Tool::Place;
    y += h + 12;

    ui.label({x, y}, "Groups", ui.theme.accent);
    y += 24;
    const std::vector<EncountersEditor::Group>& groups = editor.groups();
    const float bottom = column.y + column.h - 190;
    size_t shown = 0;
    for (; shown < groups.size() && y + h <= bottom; shown++)
    {
        const std::string name = groups[shown].id + "  (" + std::to_string(groups[shown].creatures.size()) + ")";
        if (ui.toggle({x, y, w, h}, name, shown == group_))
        {
            // Picking a group shows the group, not whoever was picked in it.
            group_ = shown;
            creature_.reset();
            if (tool_ == Tool::Select)
                hint_.clear();
        }
        ui.label({x + w - 14, y + 5}, "o", colorOf(shown));
        y += h + 3;
    }
    if (shown < groups.size())
    {
        ui.label({x, y + 2}, "... and " + std::to_string(groups.size() - shown) + " more", ui.theme.textDim);
        y += 22;
    }
    if (groups.empty())
    {
        ui.label({x, y + 4}, "None yet", ui.theme.textDim);
        y += h;
    }
    y += 6;
    if (ui.button({x, y, w / 2 - 2, h}, "Add"))
    {
        if (const std::optional<size_t> added = editor.addGroup())
        {
            group_ = *added;
            creature_.reset();
        }
    }
    if (ui.button({x + w / 2 + 2, y, w / 2 - 2, h}, "Remove", !groups.empty()))
    {
        editor.removeGroup(group_);
        group_ = std::min(group_, groups.empty() ? size_t(0) : groups.size() - 1);
        creature_.reset();
    }
    y += h + 12;

    ui.checkbox({x, y, w, h}, "Grid", grid_);
    y += h + 4;
    if (ui.button({x, y, w, h}, "Fit map"))
        zoom_ = 0;
    y += h + 8;
    if (hover_)
        ui.label({x, y}, std::to_string(hover_->x) + ", " + std::to_string(hover_->y), ui.theme.textDim);
    y += 22;
    ui.label({x, y}, "Right: remove", ui.theme.textDim);
}

void EncountersPanel::drawGroup(EncountersEditor& editor, yh::Ui& ui, const yh::Rect& column)
{
    const float x = column.x + 8, w = column.w - 16, h = 28;
    float y = column.y + 8;
    if (editor.groups().empty())
    {
        ui.label({x, y}, "No groups yet.", ui.theme.accent);
        ui.label({x, y + 24}, "Pick Place, then click", ui.theme.textDim);
        ui.label({x, y + 44}, "the map.", ui.theme.textDim);
        return;
    }
    // A copy: the commands below change the one in the editor.
    const EncountersEditor::Group group = editor.groups()[group_];
    std::string wrong;

    ui.label({x, y}, "Group", colorOf(group_));
    y += 22;
    if (field(ui, "enc-id", {x, y, w, 30}, idText_, group.id, 64) && !editor.setGroupId(group_, idText_))
        wrong = idText_.empty() ? "A group needs an id" : "Another group has that id";
    y += 36;

    ui.label({x, y}, "Line when the fight starts", ui.theme.textDim);
    y += 20;
    if (field(ui, "enc-line", {x, y, w, 30}, lineText_, group.text, 400))
        editor.setGroupText(group_, lineText_);
    y += 36;

    ui.label({x, y}, "Flags set on a win", ui.theme.textDim);
    y += 20;
    if (field(ui, "enc-flags", {x, y, w, 30}, flagsText_, joined(group.set), 400) && flagsFrom(flagsText_) != group.set
        && !editor.setGroupFlags(group_, flagsFrom(flagsText_)))
        wrong = "Flags are 1 to 64 characters";
    y += 36;

    ui.label({x, y}, "AI for all of them", ui.theme.textDim);
    y += 20;
    const std::string profile = EncountersEditor::profileOf(group.ai);
    if (const int step = stepper(ui, {x, y, w, h}, profile.empty() ? "each one's own" : profile))
        editor.setGroupAi(group_, stepName(profiles(editor.catalog()), profile, step));
    y += h + 8;

    // What a win is worth: the group's own number, or the chapter's when the box is empty.
    const float half = w / 2 - 3;
    ui.label({x, y}, "XP", ui.theme.textDim);
    ui.label({x + half + 6, y}, "Chapter's", ui.theme.textDim);
    y += 20;
    if (field(ui, "enc-xp", {x, y, half, 30}, xpText_, group.xp ? std::to_string(*group.xp) : std::string(), 7))
    {
        const std::optional<int> xp = wholeNumber(xpText_);
        if (xpText_.empty() ? !editor.setGroupXp(group_, std::nullopt) : !xp || (*xp != group.xp && !editor.setGroupXp(group_, xp)))
            wrong = "XP is a whole number";
    }
    if (field(ui, "enc-chapter-xp", {x + half + 6, y, half, 30}, chapterXpText_, std::to_string(editor.chapterXp()), 7))
    {
        const std::optional<int> xp = wholeNumber(chapterXpText_);
        if (!xp || (*xp != editor.chapterXp() && !editor.setChapterXp(*xp)))
            wrong = "XP is a whole number";
    }
    y += 34;
    const int proposed = editor.proposedXp(group_);
    if (ui.button({x, y, w, h}, "Use " + std::to_string(proposed) + " from levels", group.xp != proposed && !group.creatures.empty()))
        editor.setGroupXp(group_, proposed);
    y += h + 10;

    ui.label({x, y}, "Loot", ui.theme.accent);
    ui.label({x + 48, y}, "coins, as dice", ui.theme.textDim);
    y += 22;
    if (field(ui, "enc-coins", {x, y, w, 30}, coinsText_, group.loot.coins, 24))
    {
        yh::LootTable loot = group.loot;
        loot.coins = coinsText_;
        if (!editor.setGroupLoot(group_, loot))
            wrong = "Coins are dice, like 2d6";
    }
    y += 36;
    // A click on the list is its own undo step, apart from any typing before or after it.
    auto click = [&](const yh::LootTable& loot) {
        editor.endTyping();
        editor.setGroupLoot(group_, loot);
        editor.endTyping();
    };
    const float bottom = column.y + column.h - 34 - 2 * (h + 4);
    for (size_t i = 0; i < group.loot.items.size() && y + h <= bottom; i++)
    {
        const yh::LootEntry& entry = group.loot.items[i];
        const auto item = editor.catalog().items.find(entry.item);
        ui.label({x, y + 5}, item != editor.catalog().items.end() ? item->second : entry.item);
        yh::LootTable loot = group.loot;
        // The chance steps down and comes round again; so does how many.
        if (ui.button({x + w - 116, y, 52, h}, std::to_string(static_cast<int>(std::lround(entry.chance * 100))) + "%"))
        {
            loot.items[i].chance = 1;
            for (const float lower : {0.75f, 0.5f, 0.25f, 0.1f})
                if (lower < entry.chance - 0.001f)
                {
                    loot.items[i].chance = lower;
                    break;
                }
            click(loot);
        }
        if (ui.button({x + w - 62, y, 34, h}, "x" + std::to_string(entry.quantity)))
        {
            loot.items[i].quantity = 1;
            for (const int more : {2, 3, 5, 10})
                if (more > entry.quantity)
                {
                    loot.items[i].quantity = more;
                    break;
                }
            click(loot);
        }
        if (ui.button({x + w - 26, y, 26, h}, "-"))
        {
            loot.items.erase(loot.items.begin() + static_cast<std::ptrdiff_t>(i));
            click(loot);
        }
        y += h + 3;
    }
    if (editor.catalog().items.empty())
    {
        ui.label({x, y + 4}, "No items in this package", ui.theme.textDim);
    }
    else
    {
        std::vector<std::string> ids;
        for (const auto& [id, name] : editor.catalog().items)
            ids.push_back(id);
        if (const int step = stepper(ui, {x, y, w, h}, editor.catalog().items.find(lootItem_)->second))
            lootItem_ = stepName(ids, lootItem_, step);
        y += h + 4;
        if (ui.button({x, y, w, h}, "Add to loot"))
        {
            yh::LootTable loot = group.loot;
            loot.items.push_back({lootItem_, 1, 1});
            click(loot);
        }
    }
    if (!wrong.empty())
        hint_ = wrong;
    else if (tool_ == Tool::Select && !dragging_ && typing(ui))
        hint_.clear();
}

void EncountersPanel::drawCreature(EncountersEditor& editor, yh::Ui& ui, const yh::Rect& column)
{
    const float x = column.x + 8, w = column.w - 16, h = 28;
    float y = column.y + 8;
    const EncountersEditor::Group& group = editor.groups()[group_];
    const EncountersEditor::Placement p = group.creatures[*creature_]; // a copy, as in drawGroup
    const auto look = editor.catalog().creatures.find(p.creature);
    const bool known = look != editor.catalog().creatures.end();

    ui.label({x, y}, known ? look->second.name : p.creature, ui.theme.accent);
    y += 22;
    ui.label({x, y}, known ? "Level " + std::to_string(look->second.level) + ", at " + std::to_string(p.at.x) + ", " + std::to_string(p.at.y)
        : std::string("Not in this package"), known ? ui.theme.textDim : ui.theme.bad);
    y += 26;

    ui.label({x, y}, "Name", ui.theme.textDim);
    y += 20;
    if (field(ui, "enc-name", {x, y, w, 30}, nameText_, p.name, 64))
        editor.setCreatureName(group_, *creature_, nameText_);
    y += 38;

    ui.label({x, y}, "Group", ui.theme.textDim);
    y += 20;
    if (const int step = stepper(ui, {x, y, w, h}, group.id); step != 0 && editor.groups().size() > 1)
    {
        const size_t count = editor.groups().size();
        const size_t to = (group_ + count + static_cast<size_t>(step + static_cast<int>(count))) % count;
        if (const std::optional<size_t> moved = editor.moveToGroup(group_, *creature_, to))
        {
            group_ = to;
            creature_ = moved;
        }
        return; // `group` and the place in it are no longer what was drawn above
    }
    y += h + 10;

    ui.label({x, y}, p.facing ? "Looks " + std::to_string(static_cast<int>(std::lround(*p.facing))) + " degrees" : std::string("Looks toward the party"),
        ui.theme.textDim);
    y += 22;
    // The eight ways on the map, with "toward the party" in the middle.
    const std::pair<const char*, float> ways[9] = {
        {"NW", 225.0f}, {"N", 270.0f}, {"NE", 315.0f}, {"W", 180.0f}, {"Party", -1.0f}, {"E", 0.0f}, {"SW", 135.0f}, {"S", 90.0f}, {"SE", 45.0f},
    };
    const float third = (w - 8) / 3;
    for (int i = 0; i < 9; i++)
    {
        const std::optional<float> way = ways[i].second < 0 ? std::nullopt : std::optional<float>(ways[i].second);
        if (ui.toggle({x + (i % 3) * (third + 4), y + (i / 3) * (h + 4), third, h}, ways[i].first, p.facing == way))
            editor.setFacing(group_, *creature_, way);
    }
    y += 3 * (h + 4) + 8;

    ui.label({x, y}, "AI", ui.theme.textDim);
    y += 20;
    const std::string profile = EncountersEditor::profileOf(p.ai);
    if (const int step = stepper(ui, {x, y, w, h}, profile.empty() ? "the group's" : profile))
        editor.setCreatureAi(group_, *creature_, stepName(profiles(editor.catalog()), profile, step));
    y += h + 14;

    if (ui.button({x, y, w, h}, "Remove (Del)"))
    {
        editor.removeCreature(group_, *creature_);
        creature_.reset();
    }
}

void EncountersPanel::drawPalette(EncountersEditor& editor, yh::Ui& ui, const yh::Rect& column)
{
    const float x = column.x + 8, w = column.w - 16, h = 30;
    float y = column.y + 8;
    ui.label({x, y}, "Place", ui.theme.accent);
    y += 22;
    ui.label({x, y}, editor.groups().empty() ? std::string("into a new group") : "into " + editor.groups()[group_].id,
        editor.groups().empty() ? ui.theme.textDim : colorOf(group_));
    y += 26;

    const EncountersEditor::Catalog& catalog = editor.catalog();
    if (catalog.creatures.empty())
    {
        ui.label({x, y}, "No creatures in this package", ui.theme.textDim);
        return;
    }
    const float row = h + 3;
    const yh::Rect list{x, y, w, std::max(row, column.y + column.h - 34 - y)};
    ui.beginScroll(list, catalog.creatures.size() * row, scroll_);
    float top = 0;
    for (const auto& [id, creature] : catalog.creatures)
    {
        if (ui.toggle({0, top, w, h}, creature.name + "  L" + std::to_string(creature.level), id == kind_))
            kind_ = id;
        top += row;
    }
    ui.endScroll();
}
