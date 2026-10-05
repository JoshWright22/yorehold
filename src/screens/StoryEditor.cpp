// Story mode of the Create screen: the adventure's story graph, the commands that change it, what
// the package suggests for it, and the desktop layout over them.

#include "StoryEditor.h"

#include <yorehold/framework/graphics/Font.h>

#include <nlohmann/json.hpp>
#include <SDL3/SDL_keycode.h>

#include <algorithm>
#include <charconv>
#include <cmath>
#include <iterator>
#include <memory>
#include <set>

namespace
{

// Ordered, so a hand-written file keeps its fields where the writer put them.
using nlohmann::ordered_json;
using Kind = StoryEditor::Kind;

constexpr int storyFormat = 1;
constexpr size_t maxName = 64, maxTitle = 200, maxText = 4000, maxNodes = 2000, maxSteps = 50;
constexpr int maxXp = 1000000, minMap = 8, maxMap = 200;
constexpr Kind allKinds[] = {Kind::Scene, Kind::Encounter, Kind::Dialogue, Kind::Quest, Kind::Ending};

std::string extraOf(const ordered_json& entry, const std::vector<std::string_view>& known)
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

bool validId(std::string_view id)
{
    return !id.empty() && id.size() <= maxName && std::all_of(id.begin(), id.end(), [](char c) {
        return (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
    });
}

bool validFlags(const std::vector<std::string>& flags)
{
    std::set<std::string> seen;
    return std::all_of(flags.begin(), flags.end(), [&](const std::string& f) { return !f.empty() && f.size() <= maxName && seen.insert(f).second; });
}

std::optional<Kind> kindFrom(std::string_view name)
{
    for (const Kind kind : allKinds)
        if (StoryEditor::kindName(kind) == name)
            return kind;
    return std::nullopt;
}

// "chapters/keep/dialogue/wren.json" -> "wren"
std::string fileName(const std::string& path)
{
    const size_t slash = path.rfind('/');
    std::string name = slash == std::string::npos ? path : path.substr(slash + 1);
    if (name.ends_with(".json"))
        name.resize(name.size() - 5);
    return name;
}

// An id made from any text: lowercase, with - for anything else.
std::string idFrom(std::string_view text)
{
    std::string id;
    for (const char c : text)
    {
        const char lower = c >= 'A' && c <= 'Z' ? static_cast<char>(c - 'A' + 'a') : c;
        if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9') || lower == '_')
            id += lower;
        else if (!id.empty() && id.back() != '-')
            id += '-';
    }
    while (!id.empty() && id.back() == '-')
        id.pop_back();
    if (id.size() > maxName - 4)
        id.resize(maxName - 4);
    return id;
}

std::vector<std::string> split(std::string_view key)
{
    std::vector<std::string> parts;
    size_t at = 0;
    while (true)
    {
        const size_t bar = key.find('|', at);
        parts.emplace_back(key.substr(at, bar == std::string_view::npos ? std::string_view::npos : bar - at));
        if (bar == std::string_view::npos)
            return parts;
        at = bar + 1;
    }
}

// What a node is called on the graph and in messages.
std::string nameOf(const StoryEditor::Node& node)
{
    return node.title.empty() ? node.id : node.title;
}

}

// ---------------------------------------------------------------- the graph and its commands

const StoryEditor::Catalog::Chapter* StoryEditor::Catalog::chapter(std::string_view folder) const
{
    for (const Chapter& c : chapters)
        if (c.folder == folder)
            return &c;
    return nullptr;
}

std::string_view StoryEditor::kindName(Kind kind)
{
    switch (kind)
    {
    case Kind::Scene: return "scene";
    case Kind::Encounter: return "encounter";
    case Kind::Dialogue: return "dialogue";
    case Kind::Quest: return "quest";
    case Kind::Ending: return "ending";
    }
    return "scene";
}

std::string_view StoryEditor::refField(Kind kind)
{
    switch (kind)
    {
    case Kind::Scene: return "";
    case Kind::Encounter: return "group";
    case Kind::Dialogue: return "dialogue";
    case Kind::Quest: return "quest";
    case Kind::Ending: return "cutscene";
    }
    return "";
}

bool StoryEditor::load(std::string_view text, std::string* error)
{
    auto fail = [&](std::string why) {
        if (error)
            *error = std::move(why);
        return false;
    };
    if (error)
        error->clear();
    try
    {
        const ordered_json j = ordered_json::parse(text);
        if (!j.is_object())
            return fail("a story file is a JSON object");
        if (j.contains("format") && (!j["format"].is_number_integer() || j["format"].get<int>() < 1))
            return fail("format has to be a whole number from 1");
        if (j.value("format", storyFormat) > storyFormat)
            return fail("made by a newer version of the game (format " + std::to_string(j["format"].get<int>()) + ")");

        State state;
        state.extra = extraOf(j, {"format", "nodes", "links", "dismissed"});
        std::set<std::string> ids;
        for (const ordered_json& n : j.value("nodes", ordered_json::array()))
        {
            if (!n.is_object() || !n.contains("id") || !n["id"].is_string())
                return fail("every node needs an id");
            Node node;
            node.id = n["id"].get<std::string>();
            if (!validId(node.id))
                return fail("node id \"" + node.id + "\" has to be a-z, 0-9, - and _");
            if (!ids.insert(node.id).second)
                return fail("two nodes are called " + node.id);
            const std::optional<Kind> kind = kindFrom(n.value("kind", std::string()));
            if (!kind)
                return fail(node.id + ": kind has to be scene, encounter, dialogue, quest or ending");
            node.kind = *kind;
            node.title = n.value("title", std::string());
            node.text = n.value("text", std::string());
            if (node.title.size() > maxTitle || node.text.size() > maxText)
                return fail(node.id + ": the title or notes are too long");
            if (n.contains("at"))
            {
                const auto at = n["at"].get<std::vector<float>>();
                if (at.size() != 2 || !std::isfinite(at[0]) || !std::isfinite(at[1]))
                    return fail(node.id + ": at is two numbers");
                node.at = {at[0], at[1]};
            }
            node.chapter = n.value("chapter", std::string());
            const std::string_view field = refField(node.kind);
            if (!field.empty())
                node.ref = n.value(std::string(field), std::string());
            if (n.contains("xp"))
            {
                if (!n["xp"].is_number_integer() || n["xp"].get<int>() < 0 || n["xp"].get<int>() > maxXp)
                    return fail(node.id + ": xp is a whole number from 0");
                node.xp = n["xp"].get<int>();
            }
            node.steps = n.value("steps", std::vector<std::string>());
            if (n.contains("map"))
            {
                node.mapWidth = n.at("map").at("width").get<int>();
                node.mapHeight = n.at("map").at("height").get<int>();
                if (node.mapWidth < minMap || node.mapHeight < minMap || node.mapWidth > maxMap || node.mapHeight > maxMap)
                    return fail(node.id + ": a map is 8 to 200 each way");
            }
            std::vector<std::string_view> known{"id", "kind", "title", "text", "at", "chapter", "xp", "steps", "map"};
            if (!field.empty())
                known.push_back(field);
            node.extra = extraOf(n, known);
            state.nodes.push_back(std::move(node));
        }
        std::set<std::pair<std::string, std::string>> pairs;
        for (const ordered_json& l : j.value("links", ordered_json::array()))
        {
            Link link;
            link.from = l.at("from").get<std::string>();
            link.to = l.at("to").get<std::string>();
            if (!ids.contains(link.from) || !ids.contains(link.to))
                return fail("a link from " + link.from + " to " + link.to + " names a node that isn't there");
            if (link.from == link.to || !pairs.insert({link.from, link.to}).second)
                return fail("the link from " + link.from + " to " + link.to + " is there twice or goes nowhere");
            link.text = l.value("text", std::string());
            link.when = l.value("when", std::vector<std::string>());
            if (!validFlags(link.when))
                return fail("the link from " + link.from + " to " + link.to + " has an empty or repeated flag");
            link.extra = extraOf(l, {"from", "to", "text", "when"});
            state.links.push_back(std::move(link));
        }
        state.dismissed = j.value("dismissed", std::vector<std::string>());
        state_ = std::move(state);
        loaded_ = true;
        return true;
    }
    catch (const std::exception& e)
    {
        return fail(e.what());
    }
}

void StoryEditor::create()
{
    state_ = {};
    loaded_ = true;
}

std::string StoryEditor::toJson() const
{
    if (!loaded_)
        return "{}";
    ordered_json j = ordered_json::object();
    j["format"] = storyFormat;
    ordered_json nodes = ordered_json::array();
    for (const Node& node : state_.nodes)
    {
        ordered_json n;
        n["id"] = node.id;
        n["kind"] = kindName(node.kind);
        n["title"] = node.title;
        if (!node.text.empty())
            n["text"] = node.text;
        n["at"] = {std::lround(node.at.x), std::lround(node.at.y)};
        if (!node.chapter.empty())
            n["chapter"] = node.chapter;
        if (!refField(node.kind).empty() && !node.ref.empty())
            n[std::string(refField(node.kind))] = node.ref;
        if (node.xp)
            n["xp"] = *node.xp;
        if (!node.steps.empty())
            n["steps"] = node.steps;
        if (node.mapWidth > 0)
            n["map"] = {{"width", node.mapWidth}, {"height", node.mapHeight}};
        addExtra(n, node.extra);
        nodes.push_back(std::move(n));
    }
    j["nodes"] = std::move(nodes);
    ordered_json links = ordered_json::array();
    for (const Link& link : state_.links)
    {
        ordered_json l;
        l["from"] = link.from;
        l["to"] = link.to;
        if (!link.text.empty())
            l["text"] = link.text;
        if (!link.when.empty())
            l["when"] = link.when;
        addExtra(l, link.extra);
        links.push_back(std::move(l));
    }
    j["links"] = std::move(links);
    if (!state_.dismissed.empty())
        j["dismissed"] = state_.dismissed;
    addExtra(j, state_.extra);
    return j.dump(2);
}

std::optional<size_t> StoryEditor::find(std::string_view node) const
{
    for (size_t i = 0; i < state_.nodes.size(); i++)
        if (state_.nodes[i].id == node)
            return i;
    return std::nullopt;
}

std::optional<size_t> StoryEditor::findLink(std::string_view from, std::string_view to) const
{
    for (size_t i = 0; i < state_.links.size(); i++)
        if (state_.links[i].from == from && state_.links[i].to == to)
            return i;
    return std::nullopt;
}

template <typename Change>
void StoryEditor::edit(std::string_view label, Change change, std::string_view mergeKey)
{
    const auto before = std::make_shared<const State>(state_);
    change();
    const auto after = std::make_shared<const State>(state_);
    history_.record(label, [this, after] { state_ = *after; }, [this, before] { state_ = *before; }, mergeKey);
}

std::string StoryEditor::freeId(std::string_view base) const
{
    const std::string start = base.empty() ? std::string("node") : std::string(base);
    if (!find(start))
        return start;
    for (int n = 2;; n++)
    {
        const std::string id = start + "-" + std::to_string(n);
        if (!find(id))
            return id;
    }
}

std::optional<size_t> StoryEditor::sceneOf(std::string_view chapter) const
{
    if (chapter.empty())
        return std::nullopt;
    for (size_t i = 0; i < state_.nodes.size(); i++)
        if (state_.nodes[i].kind == Kind::Scene && state_.nodes[i].chapter == chapter)
            return i;
    return std::nullopt;
}

yh::Vec2 StoryEditor::placeFor(const std::string& chapter) const
{
    const float down = nodeHeight + 22;
    if (const std::optional<size_t> scene = sceneOf(chapter))
    {
        // In a column to the right of the scene, so each link from it can be told apart.
        const yh::Vec2 top = state_.nodes[*scene].at;
        const float x = top.x + nodeWidth + 60;
        std::optional<float> bottom;
        for (const Node& node : state_.nodes)
            if (std::abs(node.at.x - x) < nodeWidth && node.at.y > top.y - nodeHeight)
                bottom = std::max(bottom.value_or(node.at.y), node.at.y);
        return {x, bottom ? *bottom + down : top.y};
    }
    // A new column to the right of everything.
    float right = -1e9f;
    for (const Node& node : state_.nodes)
        right = std::max(right, node.at.x);
    return {state_.nodes.empty() ? 0.0f : right + nodeWidth + 50, 0};
}

std::optional<size_t> StoryEditor::addNode(Kind kind, yh::Vec2 at, std::string id)
{
    if (!loaded_ || state_.nodes.size() >= maxNodes || !std::isfinite(at.x) || !std::isfinite(at.y))
        return std::nullopt;
    if (id.empty())
        id = freeId(kindName(kind));
    if (!validId(id) || find(id))
        return std::nullopt;
    Node node;
    node.id = id;
    node.kind = kind;
    node.at = at;
    edit("Add " + std::string(kindName(kind)), [&] { state_.nodes.push_back(std::move(node)); });
    return state_.nodes.size() - 1;
}

bool StoryEditor::removeNode(size_t node)
{
    if (!loaded_ || node >= state_.nodes.size())
        return false;
    edit("Remove " + nameOf(state_.nodes[node]), [&] {
        const std::string id = state_.nodes[node].id;
        std::erase_if(state_.links, [&](const Link& l) { return l.from == id || l.to == id; });
        state_.nodes.erase(state_.nodes.begin() + static_cast<std::ptrdiff_t>(node));
    });
    return true;
}

bool StoryEditor::renameNode(size_t node, const std::string& id)
{
    if (!loaded_ || node >= state_.nodes.size() || !validId(id))
        return false;
    if (state_.nodes[node].id == id)
        return true;
    if (find(id))
        return false;
    edit("Rename node", [&] {
        const std::string old = state_.nodes[node].id;
        for (Link& link : state_.links)
        {
            if (link.from == old)
                link.from = id;
            if (link.to == old)
                link.to = id;
        }
        state_.nodes[node].id = id;
    }, "id:" + std::to_string(node));
    return true;
}

bool StoryEditor::moveNode(size_t node, yh::Vec2 at)
{
    if (!loaded_ || node >= state_.nodes.size() || !std::isfinite(at.x) || !std::isfinite(at.y))
        return false;
    at = {std::round(at.x), std::round(at.y)};
    if (state_.nodes[node].at == at)
        return true;
    edit("Move " + nameOf(state_.nodes[node]), [&] { state_.nodes[node].at = at; }, "move:" + std::to_string(node));
    return true;
}

bool StoryEditor::setKind(size_t node, Kind kind)
{
    if (!loaded_ || node >= state_.nodes.size())
        return false;
    if (state_.nodes[node].kind == kind)
        return true;
    edit("Make it a " + std::string(kindName(kind)), [&] {
        Node& n = state_.nodes[node];
        n.kind = kind;
        n.ref.clear();
        if (kind != Kind::Encounter && kind != Kind::Quest)
            n.xp.reset();
        if (kind != Kind::Quest)
            n.steps.clear();
        if (kind != Kind::Scene)
            n.mapWidth = n.mapHeight = 0;
    });
    return true;
}

bool StoryEditor::setTitle(size_t node, const std::string& title)
{
    if (!loaded_ || node >= state_.nodes.size() || title.size() > maxTitle)
        return false;
    if (state_.nodes[node].title != title)
        edit("Title", [&] { state_.nodes[node].title = title; }, "title:" + std::to_string(node));
    return true;
}

bool StoryEditor::setText(size_t node, const std::string& text)
{
    if (!loaded_ || node >= state_.nodes.size() || text.size() > maxText)
        return false;
    if (state_.nodes[node].text != text)
        edit("Notes", [&] { state_.nodes[node].text = text; }, "text:" + std::to_string(node));
    return true;
}

bool StoryEditor::setChapter(size_t node, const std::string& chapter)
{
    if (!loaded_ || node >= state_.nodes.size() || (!chapter.empty() && !catalog_.chapter(chapter)))
        return false;
    if (state_.nodes[node].chapter == chapter)
        return true;
    edit("Chapter", [&] {
        Node& n = state_.nodes[node];
        n.chapter = chapter;
        n.ref.clear();
        // A scene that has its chapter has its map too.
        if (!chapter.empty())
            n.mapWidth = n.mapHeight = 0;
    });
    return true;
}

bool StoryEditor::setRef(size_t node, const std::string& ref)
{
    if (!loaded_ || node >= state_.nodes.size() || refField(state_.nodes[node].kind).empty() || ref.size() > 400)
        return false;
    if (state_.nodes[node].ref != ref)
        edit(std::string(refField(state_.nodes[node].kind)), [&] { state_.nodes[node].ref = ref; });
    return true;
}

bool StoryEditor::setXp(size_t node, std::optional<int> xp)
{
    if (!loaded_ || node >= state_.nodes.size() || (xp && (*xp < 0 || *xp > maxXp)))
        return false;
    const Kind kind = state_.nodes[node].kind;
    if (xp && kind != Kind::Encounter && kind != Kind::Quest)
        return false;
    if (state_.nodes[node].xp != xp)
        edit("XP", [&] { state_.nodes[node].xp = xp; }, "xp:" + std::to_string(node));
    return true;
}

bool StoryEditor::setSteps(size_t node, const std::vector<std::string>& steps)
{
    if (!loaded_ || node >= state_.nodes.size() || state_.nodes[node].kind != Kind::Quest || steps.size() > maxSteps)
        return false;
    if (std::any_of(steps.begin(), steps.end(), [](const std::string& s) { return s.empty() || s.size() > maxTitle; }))
        return false;
    if (state_.nodes[node].steps != steps)
        edit("Quest steps", [&] { state_.nodes[node].steps = steps; }, "steps:" + std::to_string(node));
    return true;
}

bool StoryEditor::setMapSize(size_t node, int width, int height)
{
    if (!loaded_ || node >= state_.nodes.size() || state_.nodes[node].kind != Kind::Scene)
        return false;
    const bool none = width == 0 && height == 0;
    if (!none && (width < minMap || height < minMap || width > maxMap || height > maxMap))
        return false;
    Node& n = state_.nodes[node];
    if (n.mapWidth != width || n.mapHeight != height)
        edit("Map size", [&] {
            state_.nodes[node].mapWidth = width;
            state_.nodes[node].mapHeight = height;
        }, "map:" + std::to_string(node));
    return true;
}

std::optional<size_t> StoryEditor::addLink(size_t from, size_t to)
{
    if (!loaded_ || from >= state_.nodes.size() || to >= state_.nodes.size() || from == to)
        return std::nullopt;
    const std::string a = state_.nodes[from].id, b = state_.nodes[to].id;
    if (findLink(a, b))
        return std::nullopt;
    edit("Link " + nameOf(state_.nodes[from]) + " to " + nameOf(state_.nodes[to]), [&] { state_.links.push_back({a, b, {}, {}, {}}); });
    return state_.links.size() - 1;
}

bool StoryEditor::removeLink(size_t link)
{
    if (!loaded_ || link >= state_.links.size())
        return false;
    edit("Remove link", [&] { state_.links.erase(state_.links.begin() + static_cast<std::ptrdiff_t>(link)); });
    return true;
}

bool StoryEditor::setLinkText(size_t link, const std::string& text)
{
    if (!loaded_ || link >= state_.links.size() || text.size() > maxTitle)
        return false;
    if (state_.links[link].text != text)
        edit("Link text", [&] { state_.links[link].text = text; }, "link:" + std::to_string(link));
    return true;
}

bool StoryEditor::setLinkWhen(size_t link, const std::vector<std::string>& when)
{
    if (!loaded_ || link >= state_.links.size() || !validFlags(when))
        return false;
    if (state_.links[link].when != when)
        edit("Link flags", [&] { state_.links[link].when = when; }, "when:" + std::to_string(link));
    return true;
}

std::vector<std::string> StoryEditor::refOptions(size_t node) const
{
    std::vector<std::string> out;
    if (node >= state_.nodes.size())
        return out;
    const Node& n = state_.nodes[node];
    auto add = [&](const std::string& option) {
        if (!option.empty() && std::find(out.begin(), out.end(), option) == out.end())
            out.push_back(option);
    };
    for (const Catalog::Chapter& c : catalog_.chapters)
    {
        if (!n.chapter.empty() && c.folder != n.chapter)
            continue;
        switch (n.kind)
        {
        case Kind::Scene:
            break;
        case Kind::Encounter:
            for (const Catalog::Group& g : c.groups)
                add(g.id);
            break;
        case Kind::Dialogue:
            for (const std::string& d : c.dialogues)
                add(d);
            break;
        case Kind::Quest:
            for (const Catalog::Quest& q : c.quests)
                add(q.id);
            break;
        case Kind::Ending:
            add(c.ending);
            break;
        }
    }
    return out;
}

std::vector<StoryEditor::Suggestion> StoryEditor::suggestions() const
{
    std::vector<Suggestion> out;
    if (!loaded_)
        return out;
    auto offer = [&](std::string key, std::string text) {
        if (std::find(state_.dismissed.begin(), state_.dismissed.end(), key) == state_.dismissed.end())
            out.push_back({std::move(key), std::move(text)});
    };
    auto has = [&](Kind kind, const std::string& chapter, const std::string& ref) {
        return std::any_of(state_.nodes.begin(), state_.nodes.end(), [&](const Node& n) {
            return n.kind == kind && n.ref == ref && (kind == Kind::Dialogue || n.chapter == chapter);
        });
    };

    // What the package has that the graph doesn't show yet.
    std::set<std::string> dialogues;
    for (const Catalog::Chapter& c : catalog_.chapters)
    {
        const std::string name = c.title.empty() ? c.id : c.title;
        if (!sceneOf(c.folder))
            offer("scene|" + c.folder, "Add a scene for " + name);
        for (const Catalog::Group& g : c.groups)
            if (!has(Kind::Encounter, c.folder, g.id))
                offer("encounter|" + c.folder + "|" + g.id, "Add the fight " + g.id + " in " + name);
        for (const std::string& d : c.dialogues)
            if (dialogues.insert(d).second && !has(Kind::Dialogue, c.folder, d))
                offer("dialogue|" + c.folder + "|" + d, "Add the conversation " + fileName(d));
        for (const Catalog::Quest& q : c.quests)
            if (!has(Kind::Quest, c.folder, q.id))
                offer("quest|" + c.folder + "|" + q.id, "Add the quest " + (q.title.empty() ? q.id : q.title));
        if (!c.ending.empty() && !has(Kind::Ending, c.folder, c.ending))
            offer("ending|" + c.folder, "Add the ending of " + name);
    }

    // What the graph itself proposes.
    for (const Node& n : state_.nodes)
    {
        const Catalog::Chapter* chapter = catalog_.chapter(n.chapter);
        if (n.kind == Kind::Encounter && chapter)
            for (const Catalog::Group& g : chapter->groups)
                if (g.id == n.ref && g.creatures > 0 && n.xp != g.xp)
                    offer("xp|" + n.id + "|" + std::to_string(g.xp), "XP for " + nameOf(n) + ": " + std::to_string(g.xp) + " from creature levels");
        if (n.kind == Kind::Quest && n.steps.empty())
        {
            int after = 0;
            for (const Link& l : state_.links)
                after += l.from == n.id ? 1 : 0;
            if (after > 0)
                offer("steps|" + n.id, "Steps for " + nameOf(n) + " from the " + std::to_string(after) + (after == 1 ? " node" : " nodes") + " after it");
        }
        if (n.kind == Kind::Scene && n.chapter.empty() && n.mapWidth == 0)
        {
            const auto [w, h] = mapFor(static_cast<size_t>(&n - state_.nodes.data()));
            offer("map|" + n.id, "Map for " + nameOf(n) + ": " + std::to_string(w) + " by " + std::to_string(h));
        }
    }
    return out;
}

std::pair<int, int> StoryEditor::mapFor(size_t node) const
{
    int fights = 0;
    for (const Link& l : state_.links)
        if (l.from == state_.nodes[node].id)
            if (const std::optional<size_t> to = find(l.to); to && state_.nodes[*to].kind == Kind::Encounter)
                fights++;
    // A room to start in, and more room for each fight that happens there.
    return {std::min(maxMap, mapBase.first + mapPerFight.first * fights), std::min(maxMap, mapBase.second + mapPerFight.second * fights)};
}

bool StoryEditor::apply(std::string_view key)
{
    const std::vector<std::string> part = split(key);
    const std::string& what = part[0];
    auto linkFromScene = [&](const std::string& chapter, const std::string& to) {
        if (const std::optional<size_t> scene = sceneOf(chapter))
            if (!findLink(state_.nodes[*scene].id, to))
                state_.links.push_back({state_.nodes[*scene].id, to, {}, {}, {}});
    };
    auto addFor = [&](Kind kind, const std::string& chapter, const std::string& ref, const std::string& title, const std::string& base) {
        if (state_.nodes.size() >= maxNodes)
            return false;
        Node node;
        node.kind = kind;
        node.id = freeId(idFrom(base).empty() ? std::string(kindName(kind)) : idFrom(base));
        node.title = title.substr(0, maxTitle);
        node.chapter = chapter;
        node.ref = ref;
        node.at = placeFor(chapter);
        state_.nodes.push_back(node);
        if (kind != Kind::Scene)
            linkFromScene(chapter, node.id);
        return true;
    };

    if (what == "scene" && part.size() == 2)
    {
        const Catalog::Chapter* c = catalog_.chapter(part[1]);
        if (!c || sceneOf(c->folder))
            return false;
        if (!addFor(Kind::Scene, c->folder, {}, c->title.empty() ? c->id : c->title, c->id))
            return false;
        const std::string id = state_.nodes.back().id;
        // Travel in adventure.json becomes links between the scenes it joins.
        for (const auto& [from, to] : catalog_.travel)
            for (const Catalog::Chapter& other : catalog_.chapters)
            {
                const std::optional<size_t> scene = sceneOf(other.folder);
                if (!scene || other.folder == c->folder)
                    continue;
                const std::string otherId = state_.nodes[*scene].id;
                if (from == c->id && to == other.id && !findLink(id, otherId))
                    state_.links.push_back({id, otherId, {}, {}, {}});
                if (from == other.id && to == c->id && !findLink(otherId, id))
                    state_.links.push_back({otherId, id, {}, {}, {}});
            }
        return true;
    }
    if (what == "encounter" && part.size() == 3)
        return catalog_.chapter(part[1]) && addFor(Kind::Encounter, part[1], part[2], part[2], part[2]);
    if (what == "dialogue" && part.size() == 3)
        return addFor(Kind::Dialogue, part[1], part[2], fileName(part[2]), fileName(part[2]));
    if (what == "quest" && part.size() == 3)
    {
        const Catalog::Chapter* c = catalog_.chapter(part[1]);
        if (!c)
            return false;
        for (const Catalog::Quest& q : c->quests)
            if (q.id == part[2])
                return addFor(Kind::Quest, c->folder, q.id, q.title.empty() ? q.id : q.title, q.id);
        return false;
    }
    if (what == "ending" && part.size() == 2)
    {
        const Catalog::Chapter* c = catalog_.chapter(part[1]);
        return c && !c->ending.empty() && addFor(Kind::Ending, c->folder, c->ending, "Ending", c->id + "-ending");
    }

    const std::optional<size_t> node = part.size() >= 2 ? find(part[1]) : std::nullopt;
    if (!node)
        return false;
    Node& n = state_.nodes[*node];
    if (what == "xp" && part.size() == 3 && n.kind == Kind::Encounter)
    {
        int xp = 0;
        const auto [end, problem] = std::from_chars(part[2].data(), part[2].data() + part[2].size(), xp);
        if (problem != std::errc() || xp < 0 || xp > maxXp)
            return false;
        n.xp = xp;
        return true;
    }
    if (what == "steps" && n.kind == Kind::Quest)
    {
        std::vector<std::string> steps;
        for (const Link& l : state_.links)
            if (l.from == n.id && steps.size() < maxSteps)
                if (const std::optional<size_t> to = find(l.to))
                    steps.push_back(nameOf(state_.nodes[*to]).substr(0, maxTitle));
        if (steps.empty())
            return false;
        state_.nodes[*node].steps = std::move(steps);
        return true;
    }
    if (what == "map" && n.kind == Kind::Scene && n.chapter.empty())
    {
        const auto [w, h] = mapFor(*node);
        n.mapWidth = w;
        n.mapHeight = h;
        return true;
    }
    return false;
}

bool StoryEditor::accept(std::string_view key)
{
    if (!loaded_)
        return false;
    const std::vector<Suggestion> shown = suggestions();
    const auto found = std::find_if(shown.begin(), shown.end(), [&](const Suggestion& s) { return s.key == key; });
    if (found == shown.end())
        return false;
    return take(*found);
}

bool StoryEditor::take(const Suggestion& suggestion)
{
    const auto before = std::make_shared<const State>(state_);
    if (!apply(suggestion.key))
    {
        state_ = *before;
        return false;
    }
    const auto after = std::make_shared<const State>(state_);
    history_.record(suggestion.text, [this, after] { state_ = *after; }, [this, before] { state_ = *before; });
    return true;
}

int StoryEditor::acceptAll()
{
    if (!loaded_)
        return 0;
    int taken = 0;
    history_.beginGroup("Take all suggestions");
    // Taking some brings up others (a fight added shows its XP), so a few rounds.
    for (int round = 0; round < 4; round++)
    {
        const std::vector<Suggestion> shown = suggestions();
        if (shown.empty())
            break;
        for (const Suggestion& s : shown)
            taken += take(s) ? 1 : 0;
    }
    history_.endGroup();
    return taken;
}

bool StoryEditor::dismiss(std::string_view key)
{
    if (!loaded_)
        return false;
    const std::vector<Suggestion> shown = suggestions();
    if (std::none_of(shown.begin(), shown.end(), [&](const Suggestion& s) { return s.key == key; }))
        return false;
    edit("Turn down a suggestion", [&] { state_.dismissed.emplace_back(key); });
    return true;
}

bool StoryEditor::restoreDismissed()
{
    if (!loaded_ || state_.dismissed.empty())
        return false;
    edit("Bring back suggestions", [&] { state_.dismissed.clear(); });
    return true;
}

std::vector<StoryEditor::Problem> StoryEditor::problems() const
{
    std::vector<Problem> out;
    if (!loaded_)
        return out;
    auto warn = [&](std::string text) { out.push_back({std::move(text), false}); };
    std::set<std::string> linked;
    for (const Link& l : state_.links)
    {
        linked.insert(l.from);
        linked.insert(l.to);
    }
    for (size_t i = 0; i < state_.nodes.size(); i++)
    {
        const Node& n = state_.nodes[i];
        if (n.title.empty())
            warn(n.id + " has no title");
        if (!n.chapter.empty() && !catalog_.chapter(n.chapter))
            warn(n.id + ": the package has no chapter " + n.chapter);
        else if (!n.ref.empty())
        {
            const std::vector<std::string> options = refOptions(i);
            if (std::find(options.begin(), options.end(), n.ref) == options.end())
                warn(n.id + ": " + std::string(refField(n.kind)) + " " + n.ref + (n.chapter.empty() ? std::string(" isn't in the package") : " isn't in " + n.chapter));
        }
        if (state_.nodes.size() > 1 && !linked.contains(n.id))
            warn(n.id + " isn't linked to anything");
        if (n.kind == Kind::Ending)
            for (const Link& l : state_.links)
                if (l.from == n.id)
                {
                    warn(n.id + " is an ending but the story goes on to " + l.to);
                    break;
                }
    }
    // A link between two scenes is travel, and the game only travels where adventure.json says.
    if (catalog_.adventure)
        for (const Link& l : state_.links)
        {
            const std::optional<size_t> a = find(l.from), b = find(l.to);
            const Catalog::Chapter* from = catalog_.chapter(state_.nodes[*a].chapter);
            const Catalog::Chapter* to = catalog_.chapter(state_.nodes[*b].chapter);
            if (state_.nodes[*a].kind != Kind::Scene || state_.nodes[*b].kind != Kind::Scene || !from || !to || from == to)
                continue;
            if (std::find(catalog_.travel.begin(), catalog_.travel.end(), std::pair{from->id, to->id}) == catalog_.travel.end())
                warn("adventure.json has no way from " + from->id + " to " + to->id + " for the link " + l.from + " to " + l.to);
        }
    return out;
}

// ---------------------------------------------------------------- the desktop layout

namespace
{

constexpr float listColumn = 270, fieldColumn = 300;

const char* const textBoxes[] = {"story-id", "story-title", "story-notes", "story-xp", "story-steps", "story-w", "story-h", "story-link", "story-when"};

bool field(yh::Ui& ui, std::string_view id, const yh::Rect& box, std::string& typed, const std::string& value, size_t maxBytes)
{
    if (!ui.editing(id))
        typed = value;
    ui.textBox(id, box, typed, maxBytes);
    return typed != value;
}

int stepper(yh::Ui& ui, const yh::Rect& row, std::string_view text, bool enabled = true)
{
    int step = 0;
    if (ui.button({row.x, row.y, 28, row.h}, "<", enabled))
        step = -1;
    if (ui.button({row.x + row.w - 28, row.y, 28, row.h}, ">", enabled))
        step = 1;
    ui.label({row.x + 36, row.y + 5}, text);
    return step;
}

std::vector<std::string> listFrom(const std::string& text, char by)
{
    std::vector<std::string> out;
    std::string item;
    auto finish = [&] {
        const size_t first = item.find_first_not_of(' '), last = item.find_last_not_of(' ');
        if (first != std::string::npos)
            out.push_back(item.substr(first, last - first + 1));
        item.clear();
    };
    for (const char c : text)
    {
        if (c == by)
            finish();
        else
            item += c;
    }
    finish();
    return out;
}

std::string joined(const std::vector<std::string>& items, std::string_view by)
{
    std::string text;
    for (const std::string& item : items)
        text += (text.empty() ? "" : std::string(by)) + item;
    return text;
}

float widthOf(const yh::Ui& ui, std::string_view text)
{
    return ui.theme.font ? ui.theme.font->measure(text) : static_cast<float>(text.size()) * 8;
}

std::string fit(const yh::Ui& ui, const std::string& text, float width)
{
    if (widthOf(ui, text) <= width)
        return text;
    std::string cut = text;
    while (!cut.empty() && widthOf(ui, cut + "...") > width)
        cut.pop_back();
    return cut + "...";
}

yh::Color colorOf(Kind kind)
{
    switch (kind)
    {
    case Kind::Scene: return {70, 120, 180, 255};
    case Kind::Encounter: return {180, 80, 70, 255};
    case Kind::Dialogue: return {90, 160, 110, 255};
    case Kind::Quest: return {190, 160, 80, 255};
    case Kind::Ending: return {130, 100, 170, 255};
    }
    return {120, 120, 120, 255};
}

std::string capital(std::string_view text)
{
    std::string out(text);
    if (!out.empty() && out[0] >= 'a' && out[0] <= 'z')
        out[0] = static_cast<char>(out[0] - 'a' + 'A');
    return out;
}

// Where the line from the middle of `box` toward `toward` leaves it.
yh::Vec2 edgeOf(const yh::Rect& box, yh::Vec2 toward)
{
    const yh::Vec2 c{box.x + box.w / 2, box.y + box.h / 2};
    const yh::Vec2 d = toward - c;
    if (std::abs(d.x) < 1e-3f && std::abs(d.y) < 1e-3f)
        return c;
    const float sx = std::abs(d.x) > 1e-3f ? (box.w / 2) / std::abs(d.x) : 1e9f;
    const float sy = std::abs(d.y) > 1e-3f ? (box.h / 2) / std::abs(d.y) : 1e9f;
    return c + d * std::min(sx, sy);
}

float distanceToSegment(yh::Vec2 p, yh::Vec2 a, yh::Vec2 b)
{
    const yh::Vec2 ab = b - a, ap = p - a;
    const float length = ab.x * ab.x + ab.y * ab.y;
    const float t = length > 0 ? std::clamp((ap.x * ab.x + ap.y * ab.y) / length, 0.0f, 1.0f) : 0.0f;
    const yh::Vec2 closest = a + ab * t;
    return std::hypot(p.x - closest.x, p.y - closest.y);
}

}

bool StoryPanel::typing(const yh::Ui& ui)
{
    return std::any_of(std::begin(textBoxes), std::end(textBoxes), [&](const char* id) { return ui.editing(id); });
}

void StoryPanel::pickNode(std::optional<size_t> node)
{
    node_ = node;
    link_.reset();
    hint_.clear();
}

void StoryPanel::draw(StoryEditor& editor, yh::Ui& ui, const yh::Input& input, yh::Renderer& renderer, const yh::Rect& area)
{
    if (!editor.loaded())
        return;
    // An undo can take away what was picked.
    if (node_ && *node_ >= editor.nodes().size())
        node_.reset();
    if (link_ && *link_ >= editor.links().size())
        link_.reset();
    if (!node_)
        linking_ = false;

    const bool nowTyping = typing(ui);
    if (wasTyping_ && !nowTyping)
        editor.endTyping();
    wasTyping_ = nowTyping;
    if (!nowTyping && input.keyPressed(SDLK_DELETE))
    {
        if (link_)
        {
            editor.removeLink(*link_);
            link_.reset();
        }
        else if (node_)
        {
            editor.removeNode(*node_);
            node_.reset();
        }
    }
    if (!nowTyping && input.keyPressed(SDLK_ESCAPE))
        linking_ = false;

    const yh::Rect left{area.x, area.y, listColumn, area.h};
    const yh::Rect right{area.x + area.w - fieldColumn, area.y, fieldColumn, area.h};
    const yh::Rect middle{left.x + left.w, area.y, area.w - left.w - right.w, area.h};
    ui.panel(left);
    drawList(editor, ui, renderer, left);
    drawGraph(editor, ui, input, renderer, middle);
    ui.panel(right);
    if (link_)
        drawLink(editor, ui, right);
    else if (node_)
        drawNode(editor, ui, right);
    else
    {
        ui.label({right.x + 8, right.y + 8}, "Nothing picked", ui.theme.accent);
        ui.label({right.x + 8, right.y + 34}, "Click a node or a link on the graph.", ui.theme.textDim);
    }
    if (!hint_.empty())
        ui.label({right.x + 8, right.y + right.h - 26}, fit(ui, hint_, right.w - 16), ui.theme.bad);
}

void StoryPanel::drawList(StoryEditor& editor, yh::Ui& ui, yh::Renderer& renderer, const yh::Rect& column)
{
    const float x = column.x + 8, w = column.w - 16, h = 26, row = h + 3;
    float y = column.y + 8;
    const std::vector<StoryEditor::Node>& nodes = editor.nodes();
    ui.label({x, y}, "Nodes", ui.theme.accent);
    ui.label({x + 64, y}, std::to_string(nodes.size()) + ", " + std::to_string(editor.links().size()) + (editor.links().size() == 1 ? " link" : " links"), ui.theme.textDim);
    y += 26;

    // New nodes go in the middle of what the graph shows.
    const float third = (w - 8) / 3;
    size_t k = 0;
    for (const Kind kind : allKinds)
    {
        const float bx = x + static_cast<float>(k % 3) * (third + 4), by = y + static_cast<float>(k / 3) * (h + 4);
        if (ui.button({bx, by, third, h}, capital(StoryEditor::kindName(kind))))
        {
            const float spread = static_cast<float>(nodes.size() % 5) * 16;
            if (const std::optional<size_t> added = editor.addNode(kind, yh::Vec2{160 + spread, 120 + spread} - pan_))
                pickNode(added);
        }
        k++;
    }
    y += 2 * (h + 4) + 6;

    const float listH = std::max(row * 3, (column.y + column.h - y) * 0.45f);
    const yh::Rect list{x, y, w, listH};
    ui.beginScroll(list, static_cast<float>(nodes.size()) * row, listScroll_);
    for (size_t i = 0; i < nodes.size(); i++)
    {
        const float top = static_cast<float>(i) * row;
        const bool clicked = ui.toggle({0, top, w, h}, fit(ui, nodes[i].title.empty() ? nodes[i].id : nodes[i].title, w - 30), node_ == i);
        renderer.fillRect({4, top + 6, 8, h - 12}, colorOf(nodes[i].kind));
        if (clicked)
        {
            pickNode(i);
            // Bring it into view.
            pan_ = yh::Vec2{120, 100} - nodes[i].at;
        }
    }
    ui.endScroll();
    y += listH + 10;

    const std::vector<StoryEditor::Suggestion> suggestions = editor.suggestions();
    ui.label({x, y}, "Suggestions", ui.theme.accent);
    ui.label({x + 104, y}, std::to_string(suggestions.size()), ui.theme.textDim);
    if (ui.button({x + w - 82, y - 4, 82, h}, "Take all", !suggestions.empty()))
        editor.acceptAll();
    y += 28;
    const float bottom = column.y + column.h - 8 - (editor.dismissed().empty() ? 0 : h + 6);
    const yh::Rect box{x, y, w, std::max(row, bottom - y)};
    const float two = 2 * row + 4; // the text on one line, the buttons under it
    ui.beginScroll(box, static_cast<float>(suggestions.size()) * two, suggestionScroll_);
    for (size_t i = 0; i < suggestions.size(); i++)
    {
        const float top = static_cast<float>(i) * two;
        ui.label({2, top + 4}, fit(ui, suggestions[i].text, w - 4), ui.theme.text);
        if (ui.button({0, top + row, (w - 4) / 2, h}, "Take"))
            editor.accept(suggestions[i].key);
        if (ui.button({(w - 4) / 2 + 4, top + row, (w - 4) / 2, h}, "Not this"))
            editor.dismiss(suggestions[i].key);
    }
    if (suggestions.empty())
        ui.label({2, 4}, "Nothing to suggest", ui.theme.textDim);
    ui.endScroll();
    if (!editor.dismissed().empty()
        && ui.button({x, column.y + column.h - 8 - h, w, h}, "Bring back " + std::to_string(editor.dismissed().size()) + " turned down"))
        editor.restoreDismissed();
}

void StoryPanel::drawGraph(StoryEditor& editor, yh::Ui& ui, const yh::Input& input, yh::Renderer& renderer, const yh::Rect& box)
{
    renderer.fillRect(box, {24, 25, 32, 255});
    const std::vector<StoryEditor::Node>& nodes = editor.nodes();
    const std::vector<StoryEditor::Link>& links = editor.links();
    const yh::Vec2 mouse = input.mouse() - yh::Vec2{box.x, box.y};
    const bool inside = box.contains(input.mouse());
    auto rectOf = [&](const StoryEditor::Node& n) { return yh::Rect{pan_.x + n.at.x, pan_.y + n.at.y, StoryEditor::nodeWidth, StoryEditor::nodeHeight}; };

    // Drawn in the box's own coordinates, clipped to it.
    float noScroll = 0;
    ui.beginScroll(box, 0, noScroll);

    // A faint grid, so moving the view reads as moving.
    const float cell = 40;
    for (float gx = std::fmod(pan_.x, cell); gx < box.w; gx += cell)
        renderer.fillRect({gx, 0, 1, box.h}, {32, 33, 42, 255});
    for (float gy = std::fmod(pan_.y, cell); gy < box.h; gy += cell)
        renderer.fillRect({0, gy, box.w, 1}, {32, 33, 42, 255});

    // Links under the nodes, each with an arrowhead where it arrives.
    std::optional<size_t> overLink;
    for (size_t i = 0; i < links.size(); i++)
    {
        const std::optional<size_t> a = editor.find(links[i].from), b = editor.find(links[i].to);
        if (!a || !b)
            continue;
        const yh::Rect ra = rectOf(nodes[*a]), rb = rectOf(nodes[*b]);
        const yh::Vec2 ca{ra.x + ra.w / 2, ra.y + ra.h / 2}, cb{rb.x + rb.w / 2, rb.y + rb.h / 2};
        // Two links between the same pair sit a little apart.
        const bool both = editor.findLink(links[i].to, links[i].from).has_value();
        const yh::Vec2 d = cb - ca;
        const float len = std::max(1.0f, std::hypot(d.x, d.y));
        const yh::Vec2 side = both ? yh::Vec2{-d.y / len, d.x / len} * 5 : yh::Vec2{};
        const yh::Vec2 from = edgeOf(ra, cb) + side, to = edgeOf(rb, ca) + side;
        const bool picked = link_ == i;
        if (inside && distanceToSegment(mouse, from, to) < 6)
            overLink = i;
        const yh::Color color = picked ? ui.theme.accent : links[i].when.empty() ? yh::Color{150, 152, 170, 255} : yh::Color{200, 170, 110, 255};
        renderer.drawLine(from, to, color, picked ? 3.0f : 2.0f);
        const yh::Vec2 back = (to - from) / std::max(1.0f, std::hypot(to.x - from.x, to.y - from.y));
        const yh::Vec2 across{-back.y, back.x};
        renderer.drawLine(to, to - back * 12 + across * 6, color, 2);
        renderer.drawLine(to, to - back * 12 - across * 6, color, 2);
        if (!links[i].text.empty())
        {
            const yh::Vec2 mid = (from + to) / 2;
            const std::string text = fit(ui, links[i].text, 160);
            ui.label({mid.x - widthOf(ui, text) / 2, mid.y - 22}, text, {190, 190, 205, 255});
        }
    }

    std::optional<size_t> overNode;
    for (size_t i = 0; i < nodes.size(); i++)
    {
        const yh::Rect r = rectOf(nodes[i]);
        if (r.x > box.w || r.y > box.h || r.x + r.w < 0 || r.y + r.h < 0)
            continue;
        if (inside && r.contains(mouse))
            overNode = i;
        const yh::Color color = colorOf(nodes[i].kind);
        renderer.fillRect(r, {40, 42, 54, 255});
        renderer.fillRect({r.x, r.y, 6, r.h}, color);
        renderer.drawRect(r, node_ == i ? ui.theme.accent : yh::Color{80, 84, 104, 255}, node_ == i ? 2.0f : 1.0f);
        ui.label({r.x + 12, r.y + 4}, fit(ui, nodes[i].title.empty() ? nodes[i].id : nodes[i].title, r.w - 18), ui.theme.text);
        std::string under(StoryEditor::kindName(nodes[i].kind));
        if (!nodes[i].ref.empty())
            under += ": " + nodes[i].ref.substr(nodes[i].ref.rfind('/') == std::string::npos ? 0 : nodes[i].ref.rfind('/') + 1);
        else if (nodes[i].kind == Kind::Scene && !nodes[i].chapter.empty())
            under += ": " + nodes[i].chapter.substr(nodes[i].chapter.rfind('/') == std::string::npos ? 0 : nodes[i].chapter.rfind('/') + 1);
        if (nodes[i].xp)
            under += ", " + std::to_string(*nodes[i].xp) + " xp";
        ui.label({r.x + 12, r.y + 26}, fit(ui, under, r.w - 18), ui.theme.textDim);
    }

    if (linking_ && node_)
    {
        const yh::Rect r = rectOf(nodes[*node_]);
        const yh::Vec2 from = edgeOf(r, mouse);
        renderer.drawLine(from, mouse, ui.theme.accent, 2);
    }
    ui.endScroll();

    // Mouse: left picks and drags a node or picks a link, left on nothing or right drags the view.
    if (inside && input.buttonPressed(yh::MouseButton::Left))
    {
        if (overNode && linking_ && node_ && *overNode != *node_)
        {
            const std::optional<size_t> link = editor.addLink(*node_, *overNode);
            hint_ = link ? std::string() : "Those two are linked that way already";
            linking_ = false;
            if (link)
            {
                node_.reset();
                link_ = link;
            }
        }
        else if (overNode)
        {
            pickNode(overNode);
            linking_ = false;
            dragging_ = overNode;
            grab_ = mouse - (pan_ + nodes[*overNode].at);
            editor.endTyping();
        }
        else if (overLink)
        {
            node_.reset();
            link_ = overLink;
            linking_ = false;
            hint_.clear();
        }
        else
        {
            panning_ = true;
            linking_ = false;
        }
    }
    if (inside && input.buttonPressed(yh::MouseButton::Right))
        panning_ = true;
    const bool held = input.buttonDown(yh::MouseButton::Left) || input.buttonDown(yh::MouseButton::Right);
    if (dragging_ && input.buttonDown(yh::MouseButton::Left) && *dragging_ < nodes.size())
        editor.moveNode(*dragging_, mouse - grab_ - pan_);
    else if (dragging_)
    {
        dragging_.reset();
        editor.endTyping();
    }
    if (panning_ && held)
        pan_ = pan_ + input.mouseDelta();
    else
        panning_ = false;

    const std::string help = linking_ ? "Click the node the link goes to, Esc stops" : "Drag nodes to move them, drag the background to look around";
    ui.label({box.x + 10, box.y + box.h - 26}, help, ui.theme.textDim);
}

void StoryPanel::drawNode(StoryEditor& editor, yh::Ui& ui, const yh::Rect& column)
{
    const size_t i = *node_;
    const StoryEditor::Node node = editor.nodes()[i];
    const float x = column.x + 8, w = column.w - 16, h = 30;
    float y = column.y + 8;
    std::string wrong;

    ui.label({x, y}, capital(StoryEditor::kindName(node.kind)), ui.theme.accent);
    y += 24;
    const int kindStep = stepper(ui, {x, y, w, h}, "Kind: " + std::string(StoryEditor::kindName(node.kind)));
    if (kindStep != 0)
    {
        const size_t count = std::size(allKinds);
        editor.setKind(i, allKinds[(static_cast<size_t>(node.kind) + (kindStep > 0 ? 1 : count - 1)) % count]);
    }
    y += h + 8;

    ui.label({x, y}, "Id", ui.theme.textDim);
    y += 20;
    if (field(ui, "story-id", {x, y, w, h}, idText_, node.id, 64) && !editor.renameNode(i, idText_))
        wrong = "Ids are a-z, 0-9, - and _, and not taken";
    y += h + 6;
    ui.label({x, y}, "Title", ui.theme.textDim);
    y += 20;
    if (field(ui, "story-title", {x, y, w, h}, titleText_, node.title, 200))
        editor.setTitle(i, titleText_);
    y += h + 6;
    ui.label({x, y}, "Notes", ui.theme.textDim);
    y += 20;
    if (field(ui, "story-notes", {x, y, w, h}, notesText_, node.text, 4000))
        editor.setText(i, notesText_);
    y += h + 10;

    // The chapter it happens in, and what it stands for there.
    const std::vector<StoryEditor::Catalog::Chapter>& chapters = editor.catalog().chapters;
    std::vector<std::string> folders{""};
    for (const StoryEditor::Catalog::Chapter& c : chapters)
        folders.push_back(c.folder);
    const auto at = std::find(folders.begin(), folders.end(), node.chapter);
    const size_t index = at == folders.end() ? 0 : static_cast<size_t>(at - folders.begin());
    const std::string shownChapter = node.chapter.empty() ? std::string("not made yet") : node.chapter.substr(node.chapter.rfind('/') == std::string::npos ? 0 : node.chapter.rfind('/') + 1);
    ui.label({x, y}, "Chapter", ui.theme.textDim);
    y += 20;
    if (const int step = stepper(ui, {x, y, w, h}, fit(ui, shownChapter, w - 72), folders.size() > 1); step != 0)
        editor.setChapter(i, folders[(index + (step > 0 ? 1 : folders.size() - 1)) % folders.size()]);
    y += h + 6;

    if (!StoryEditor::refField(node.kind).empty())
    {
        std::vector<std::string> options = editor.refOptions(i);
        options.insert(options.begin(), std::string());
        if (!node.ref.empty() && std::find(options.begin(), options.end(), node.ref) == options.end())
            options.push_back(node.ref);
        const auto found = std::find(options.begin(), options.end(), node.ref);
        const size_t current = found == options.end() ? 0 : static_cast<size_t>(found - options.begin());
        ui.label({x, y}, capital(StoryEditor::refField(node.kind)), ui.theme.textDim);
        y += 20;
        const std::string shownRef = node.ref.empty() ? std::string("(none)") : node.ref.substr(node.ref.rfind('/') == std::string::npos ? 0 : node.ref.rfind('/') + 1);
        if (const int step = stepper(ui, {x, y, w, h}, fit(ui, shownRef, w - 72), options.size() > 1); step != 0)
            editor.setRef(i, options[(current + (step == 1 ? 1 : options.size() - 1)) % options.size()]);
        y += h + 6;
    }

    if (node.kind == Kind::Encounter || node.kind == Kind::Quest)
    {
        ui.label({x, y}, "XP", ui.theme.textDim);
        y += 20;
        if (field(ui, "story-xp", {x, y, w, h}, xpText_, node.xp ? std::to_string(*node.xp) : std::string(), 12))
        {
            int xp = 0;
            const auto [end, problem] = std::from_chars(xpText_.data(), xpText_.data() + xpText_.size(), xp);
            if (xpText_.empty())
                editor.setXp(i, std::nullopt);
            else if (problem != std::errc() || end != xpText_.data() + xpText_.size() || !editor.setXp(i, xp))
                wrong = "XP is a whole number from 0";
        }
        y += h + 6;
    }
    if (node.kind == Kind::Quest)
    {
        ui.label({x, y}, "Steps, split by ;", ui.theme.textDim);
        y += 20;
        if (field(ui, "story-steps", {x, y, w, h}, stepsText_, joined(node.steps, "; "), 4000) && !editor.setSteps(i, listFrom(stepsText_, ';')))
            wrong = "Up to 50 steps, none empty";
        y += h + 4;
        for (size_t s = 0; s < node.steps.size() && y < column.y + column.h - 120; s++)
        {
            ui.label({x + 4, y}, fit(ui, std::to_string(s + 1) + ". " + node.steps[s], w - 8), ui.theme.text);
            y += 20;
        }
        y += 6;
    }
    if (node.kind == Kind::Scene && node.chapter.empty())
    {
        ui.label({x, y}, "Map it needs, width and height", ui.theme.textDim);
        y += 20;
        const float half = (w - 6) / 2;
        const bool a = field(ui, "story-w", {x, y, half, h}, widthText_, node.mapWidth ? std::to_string(node.mapWidth) : std::string(), 4);
        const bool b = field(ui, "story-h", {x + half + 6, y, half, h}, heightText_, node.mapHeight ? std::to_string(node.mapHeight) : std::string(), 4);
        if (a || b)
        {
            int mw = 0, mh = 0;
            std::from_chars(widthText_.data(), widthText_.data() + widthText_.size(), mw);
            std::from_chars(heightText_.data(), heightText_.data() + heightText_.size(), mh);
            if (!editor.setMapSize(i, mw, mh))
                wrong = "A map is 8 to 200 each way";
        }
        y += h + 6;
    }

    y += 4;
    const float half = (w - 6) / 2;
    if (ui.toggle({x, y, half, h}, linking_ ? "Pick the target" : "Link to...", linking_))
        linking_ = !linking_;
    if (ui.button({x + half + 6, y, half, h}, "Remove"))
    {
        editor.removeNode(i);
        node_.reset();
        linking_ = false;
        return;
    }
    y += h + 8;

    // Where it goes from here.
    for (size_t l = 0; l < editor.links().size() && y < column.y + column.h - 60; l++)
    {
        const StoryEditor::Link& link = editor.links()[l];
        if (link.from != node.id && link.to != node.id)
            continue;
        const std::string text = link.from == node.id ? "to " + link.to : "from " + link.from;
        if (ui.button({x, y, w, 26}, fit(ui, text, w - 16)))
        {
            node_.reset();
            link_ = l;
            return;
        }
        y += 29;
    }
    if (!wrong.empty())
        hint_ = wrong;
    else if (!typing(ui))
        hint_.clear();
}

void StoryPanel::drawLink(StoryEditor& editor, yh::Ui& ui, const yh::Rect& column)
{
    const size_t i = *link_;
    const StoryEditor::Link link = editor.links()[i];
    const float x = column.x + 8, w = column.w - 16, h = 30;
    float y = column.y + 8;
    ui.label({x, y}, "Link", ui.theme.accent);
    y += 26;
    const std::optional<size_t> from = editor.find(link.from), to = editor.find(link.to);
    if (ui.button({x, y, w, 26}, fit(ui, "From " + link.from, w - 16)) && from)
    {
        pickNode(from);
        return;
    }
    y += 30;
    if (ui.button({x, y, w, 26}, fit(ui, "To " + link.to, w - 16)) && to)
    {
        pickNode(to);
        return;
    }
    y += 36;
    ui.label({x, y}, "What takes the story there", ui.theme.textDim);
    y += 20;
    if (field(ui, "story-link", {x, y, w, h}, linkText_, link.text, 200))
        editor.setLinkText(i, linkText_);
    y += h + 6;
    ui.label({x, y}, "Needs flags", ui.theme.textDim);
    y += 20;
    std::string wrong;
    if (field(ui, "story-when", {x, y, w, h}, whenText_, joined(link.when, ", "), 2000) && !editor.setLinkWhen(i, listFrom(whenText_, ',')))
        wrong = "A flag can't be empty or there twice";
    y += h + 10;
    if (ui.button({x, y, w, h}, "Remove link"))
    {
        editor.removeLink(i);
        link_.reset();
        return;
    }
    if (!wrong.empty())
        hint_ = wrong;
}
