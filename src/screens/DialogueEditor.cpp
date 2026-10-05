// Dialogue mode of the Create screen: the commands that change one conversation file, and the
// desktop layout over them.

#include "DialogueEditor.h"

#include <nlohmann/json.hpp>
#include <SDL3/SDL_keycode.h>

#include <algorithm>
#include <charconv>
#include <initializer_list>
#include <memory>
#include <sstream>
#include <stdexcept>

namespace
{

// Ordered, so a hand-written file keeps its fields where the writer put them.
using nlohmann::ordered_json;

constexpr size_t maxId = 64, maxSpeaker = 64, maxLine = 4000, maxReply = 1000, maxAction = 200;

bool validId(std::string_view id)
{
    return !id.empty() && id.size() <= maxId
        && std::none_of(id.begin(), id.end(), [](char c) { return static_cast<unsigned char>(c) <= ' '; });
}

// Names a file can set, clear, need or forbid: no blanks or repeats.
bool validFlags(const std::vector<std::string>& flags)
{
    std::set<std::string> seen;
    return std::all_of(flags.begin(), flags.end(), [&](const std::string& f) { return !f.empty() && f.size() <= maxId && seen.insert(f).second; });
}

bool overlap(const std::vector<std::string>& a, const std::vector<std::string>& b)
{
    return std::any_of(a.begin(), a.end(), [&](const std::string& f) { return std::find(b.begin(), b.end(), f) != b.end(); });
}

bool validChanges(const yh::DialogueFlags& flags)
{
    return validFlags(flags.set) && validFlags(flags.clear) && !overlap(flags.set, flags.clear)
        && std::all_of(flags.actions.begin(), flags.actions.end(), [](const std::string& a) { return !a.empty() && a.size() <= maxAction; });
}

bool sameFlags(const yh::DialogueFlags& a, const yh::DialogueFlags& b)
{
    return a.set == b.set && a.clear == b.clear && a.actions == b.actions;
}

bool sameCheck(const std::optional<yh::DialogueCheck>& a, const std::optional<yh::DialogueCheck>& b)
{
    if (!a || !b)
        return !a && !b;
    return a->skill == b->skill && a->difficulty == b->difficulty && a->success == b->success && a->failure == b->failure;
}

std::vector<std::string> words(std::string_view text)
{
    std::istringstream in{std::string(text)};
    std::vector<std::string> out;
    for (std::string word; in >> word;)
        out.push_back(word);
    return out;
}

// "+5", "-3" or "2", as the game reads them.
std::optional<int> change(std::string_view number)
{
    if (!number.empty() && number.front() == '+')
        number.remove_prefix(1);
    int value = 0;
    const auto [end, problem] = std::from_chars(number.data(), number.data() + number.size(), value);
    return problem == std::errc() && end == number.data() + number.size() && !number.empty() ? std::optional<int>(value) : std::nullopt;
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
    // Kept in a local: items() of a temporary would outlive it.
    const ordered_json rest = ordered_json::parse(extra);
    for (const auto& [key, value] : rest.items())
        entry[key] = value;
}

yh::DialogueFlags flagsFrom(const ordered_json& j)
{
    return {j.value("set", std::vector<std::string>{}), j.value("clear", std::vector<std::string>{}), j.value("do", std::vector<std::string>{})};
}

void writeFlags(ordered_json& j, const yh::DialogueFlags& flags)
{
    if (!flags.set.empty())
        j["set"] = flags.set;
    if (!flags.clear.empty())
        j["clear"] = flags.clear;
    if (!flags.actions.empty())
        j["do"] = flags.actions;
}

}

// ---------------------------------------------------------------- the conversation and its commands

bool DialogueEditor::load(std::string_view text, std::string* error, Catalog catalog)
{
    if (error)
        error->clear();
    // The game's own reader first, so nothing it refuses opens here.
    if (!yh::Dialogue::fromJson(text, error))
        return false;
    try
    {
        const ordered_json j = ordered_json::parse(text);
        State state;
        state.id = j.at("id").get<std::string>();
        state.start = j.at("start").get<std::string>();
        state.extra = extraOf(j, {"id", "start", "nodes"});
        for (const ordered_json& n : j.at("nodes"))
        {
            Node node;
            node.id = n.at("id").get<std::string>();
            node.speaker = n.value("speaker", std::string());
            node.text = n.value("text", std::string());
            node.flags = flagsFrom(n);
            node.extra = extraOf(n, {"id", "speaker", "text", "set", "clear", "do", "choices"});
            for (const ordered_json& c : n.value("choices", ordered_json::array()))
            {
                Choice choice;
                choice.id = c.at("id").get<std::string>();
                choice.text = c.at("text").get<std::string>();
                choice.next = c.value("next", std::string());
                choice.require = c.value("require", std::vector<std::string>{});
                choice.forbid = c.value("forbid", std::vector<std::string>{});
                choice.flags = flagsFrom(c);
                if (c.contains("check"))
                {
                    const ordered_json& k = c.at("check");
                    choice.check = yh::DialogueCheck{k.at("skill").get<std::string>(), k.at("difficulty").get<int>(),
                        k.at("success").get<std::string>(), k.at("failure").get<std::string>()};
                }
                choice.extra = extraOf(c, {"id", "text", "next", "require", "forbid", "set", "clear", "do", "check"});
                node.choices.push_back(std::move(choice));
            }
            state.nodes.push_back(std::move(node));
        }
        state_ = std::move(state);
        catalog_ = std::move(catalog);
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

void DialogueEditor::create(const std::string& id, Catalog catalog)
{
    state_ = {};
    state_.id = id.empty() ? std::string("conversation") : id;
    state_.start = "start";
    state_.nodes.push_back({"start", "", "", {}, {}, {}});
    catalog_ = std::move(catalog);
    loaded_ = true;
}

std::string DialogueEditor::toJson() const
{
    if (!loaded_)
        return "{}";
    ordered_json j;
    j["id"] = state_.id;
    j["start"] = state_.start;
    addExtra(j, state_.extra);
    ordered_json nodes = ordered_json::array();
    for (const Node& node : state_.nodes)
    {
        ordered_json n;
        n["id"] = node.id;
        n["speaker"] = node.speaker;
        n["text"] = node.text;
        writeFlags(n, node.flags);
        if (!node.choices.empty())
        {
            ordered_json choices = ordered_json::array();
            for (const Choice& choice : node.choices)
            {
                ordered_json c;
                c["id"] = choice.id;
                c["text"] = choice.text;
                if (!choice.check)
                    c["next"] = choice.next;
                if (!choice.require.empty())
                    c["require"] = choice.require;
                if (!choice.forbid.empty())
                    c["forbid"] = choice.forbid;
                writeFlags(c, choice.flags);
                if (choice.check)
                    c["check"] = {{"skill", choice.check->skill}, {"difficulty", choice.check->difficulty},
                        {"success", choice.check->success}, {"failure", choice.check->failure}};
                addExtra(c, choice.extra);
                choices.push_back(std::move(c));
            }
            n["choices"] = std::move(choices);
        }
        addExtra(n, node.extra);
        nodes.push_back(std::move(n));
    }
    j["nodes"] = std::move(nodes);
    return j.dump(2);
}

std::optional<yh::Dialogue> DialogueEditor::dialogue(std::string* error) const
{
    if (!loaded_)
    {
        if (error)
            *error = "nothing is open";
        return std::nullopt;
    }
    return yh::Dialogue::fromJson(toJson(), error);
}

std::optional<size_t> DialogueEditor::find(std::string_view node) const
{
    for (size_t i = 0; i < state_.nodes.size(); i++)
        if (state_.nodes[i].id == node)
            return i;
    return std::nullopt;
}

int DialogueEditor::linksTo(std::string_view node) const
{
    int links = 0;
    for (const Node& n : state_.nodes)
        for (const Choice& c : n.choices)
            links += c.check ? (c.check->success == node) + (c.check->failure == node) : c.next == node;
    return links;
}

template <typename Change>
void DialogueEditor::edit(std::string_view label, Change change, std::string_view mergeKey)
{
    const auto before = std::make_shared<const State>(state_);
    change();
    const auto after = std::make_shared<const State>(state_);
    history_.record(label, [this, after] { state_ = *after; }, [this, before] { state_ = *before; }, mergeKey);
}

DialogueEditor::Choice* DialogueEditor::choice(size_t node, size_t index)
{
    if (!loaded_ || node >= state_.nodes.size() || index >= state_.nodes[node].choices.size())
        return nullptr;
    return &state_.nodes[node].choices[index];
}

std::string DialogueEditor::freeNodeId(std::string_view base) const
{
    for (size_t n = state_.nodes.size() + 1;; n++)
        if (const std::string id = std::string(base) + "-" + std::to_string(n); !find(id))
            return id;
}

bool DialogueEditor::setId(const std::string& id)
{
    if (!loaded_ || !validId(id) || id == state_.id)
        return false;
    edit("Rename conversation", [&] { state_.id = id; }, "dialogue-id");
    return true;
}

bool DialogueEditor::setStart(size_t node)
{
    if (!loaded_ || node >= state_.nodes.size() || state_.nodes[node].id == state_.start)
        return false;
    edit("Start at " + state_.nodes[node].id, [&] { state_.start = state_.nodes[node].id; });
    return true;
}

std::optional<size_t> DialogueEditor::addNode(std::string id)
{
    if (!loaded_ || state_.nodes.size() >= 4096 || (!id.empty() && (!validId(id) || find(id))))
        return std::nullopt;
    if (id.empty())
        id = freeNodeId("node");
    edit("Add node", [&] {
        Node node;
        node.id = std::move(id);
        // Most of a conversation is one person talking.
        node.speaker = state_.nodes.empty() ? std::string() : state_.nodes.back().speaker;
        state_.nodes.push_back(std::move(node));
    });
    return state_.nodes.size() - 1;
}

bool DialogueEditor::removeNode(size_t node)
{
    if (!loaded_ || node >= state_.nodes.size() || state_.nodes.size() == 1)
        return false;
    edit("Remove node " + state_.nodes[node].id, [&] {
        const std::string gone = state_.nodes[node].id;
        state_.nodes.erase(state_.nodes.begin() + static_cast<std::ptrdiff_t>(node));
        for (Node& n : state_.nodes)
            for (Choice& c : n.choices)
            {
                if (c.next == gone)
                    c.next.clear();
                if (c.check && c.check->success == gone)
                    c.check->success.clear();
                if (c.check && c.check->failure == gone)
                    c.check->failure.clear();
            }
        if (state_.start == gone)
            state_.start = state_.nodes.front().id;
    });
    return true;
}

bool DialogueEditor::renameNode(size_t node, const std::string& id)
{
    if (!loaded_ || node >= state_.nodes.size() || !validId(id) || find(id))
        return false;
    edit("Rename node", [&] {
        const std::string was = state_.nodes[node].id;
        state_.nodes[node].id = id;
        if (state_.start == was)
            state_.start = id;
        for (Node& n : state_.nodes)
            for (Choice& c : n.choices)
            {
                if (c.next == was)
                    c.next = id;
                if (c.check && c.check->success == was)
                    c.check->success = id;
                if (c.check && c.check->failure == was)
                    c.check->failure = id;
            }
    }, "node-id-" + std::to_string(node));
    return true;
}

bool DialogueEditor::setSpeaker(size_t node, const std::string& speaker)
{
    if (!loaded_ || node >= state_.nodes.size() || speaker.size() > maxSpeaker || state_.nodes[node].speaker == speaker)
        return false;
    edit("Change speaker", [&] { state_.nodes[node].speaker = speaker; }, "speaker-" + std::to_string(node));
    return true;
}

bool DialogueEditor::setText(size_t node, const std::string& text)
{
    if (!loaded_ || node >= state_.nodes.size() || text.size() > maxLine || state_.nodes[node].text == text)
        return false;
    edit("Change line", [&] { state_.nodes[node].text = text; }, "line-" + std::to_string(node));
    return true;
}

bool DialogueEditor::setNodeFlags(size_t node, const yh::DialogueFlags& flags)
{
    if (!loaded_ || node >= state_.nodes.size() || !validChanges(flags) || sameFlags(state_.nodes[node].flags, flags))
        return false;
    edit("Change node flags", [&] { state_.nodes[node].flags = flags; }, "node-flags-" + std::to_string(node));
    return true;
}

std::optional<size_t> DialogueEditor::addChoice(size_t node, std::string text)
{
    if (!loaded_ || node >= state_.nodes.size() || state_.nodes[node].choices.size() >= 128 || text.size() > maxReply)
        return std::nullopt;
    const std::vector<Choice>& choices = state_.nodes[node].choices;
    std::string id;
    for (size_t n = choices.size() + 1; id.empty(); n++)
        if (std::none_of(choices.begin(), choices.end(), [&](const Choice& c) { return c.id == "reply-" + std::to_string(n); }))
            id = "reply-" + std::to_string(n);
    edit("Add reply", [&] {
        Choice choice;
        choice.id = std::move(id);
        choice.text = text.empty() ? std::string("...") : std::move(text);
        state_.nodes[node].choices.push_back(std::move(choice));
    });
    return state_.nodes[node].choices.size() - 1;
}

bool DialogueEditor::removeChoice(size_t node, size_t index)
{
    if (!choice(node, index))
        return false;
    edit("Remove reply", [&] {
        std::vector<Choice>& choices = state_.nodes[node].choices;
        choices.erase(choices.begin() + static_cast<std::ptrdiff_t>(index));
    });
    return true;
}

bool DialogueEditor::moveChoice(size_t node, size_t index, int by)
{
    if (!choice(node, index) || (by != -1 && by != 1))
        return false;
    const size_t to = index + static_cast<size_t>(by);
    if (by < 0 ? index == 0 : to >= state_.nodes[node].choices.size())
        return false;
    edit("Move reply", [&] { std::swap(state_.nodes[node].choices[index], state_.nodes[node].choices[to]); });
    return true;
}

bool DialogueEditor::setChoiceId(size_t node, size_t index, const std::string& id)
{
    Choice* c = choice(node, index);
    const std::vector<Choice>* choices = c ? &state_.nodes[node].choices : nullptr;
    if (!c || !validId(id) || std::any_of(choices->begin(), choices->end(), [&](const Choice& other) { return other.id == id; }))
        return false;
    edit("Rename reply", [&] { c->id = id; }, "reply-id-" + std::to_string(node) + "-" + std::to_string(index));
    return true;
}

bool DialogueEditor::setChoiceText(size_t node, size_t index, const std::string& text)
{
    Choice* c = choice(node, index);
    if (!c || text.empty() || text.size() > maxReply || c->text == text)
        return false;
    edit("Change reply", [&] { c->text = text; }, "reply-text-" + std::to_string(node) + "-" + std::to_string(index));
    return true;
}

bool DialogueEditor::setNext(size_t node, size_t index, const std::string& next)
{
    Choice* c = choice(node, index);
    if (!c || c->check || c->next == next || (!next.empty() && !find(next)))
        return false;
    edit(next.empty() ? std::string("Reply ends it") : "Reply goes to " + next, [&] { c->next = next; });
    return true;
}

std::optional<size_t> DialogueEditor::branch(size_t node, size_t index)
{
    if (!choice(node, index) || state_.nodes.size() >= 4096)
        return std::nullopt;
    const std::string id = freeNodeId("node");
    edit("Add node " + id, [&] {
        Node added;
        added.id = id;
        added.speaker = state_.nodes[node].speaker;
        state_.nodes.push_back(std::move(added));
        // The vector may have moved: find the reply again.
        Choice& c = state_.nodes[node].choices[index];
        if (!c.check)
            c.next = id;
        else if (c.check->success.empty() || !c.check->failure.empty())
            c.check->success = id;
        else
            c.check->failure = id;
    });
    return state_.nodes.size() - 1;
}

bool DialogueEditor::setConditions(size_t node, size_t index, const std::vector<std::string>& require, const std::vector<std::string>& forbid)
{
    Choice* c = choice(node, index);
    if (!c || !validFlags(require) || !validFlags(forbid) || overlap(require, forbid) || (c->require == require && c->forbid == forbid))
        return false;
    edit("Change reply conditions", [&] {
        c->require = require;
        c->forbid = forbid;
    }, "reply-conditions-" + std::to_string(node) + "-" + std::to_string(index));
    return true;
}

bool DialogueEditor::setChoiceFlags(size_t node, size_t index, const yh::DialogueFlags& flags)
{
    Choice* c = choice(node, index);
    if (!c || !validChanges(flags) || sameFlags(c->flags, flags))
        return false;
    edit("Change reply flags", [&] { c->flags = flags; }, "reply-flags-" + std::to_string(node) + "-" + std::to_string(index));
    return true;
}

bool DialogueEditor::setCheck(size_t node, size_t index, const std::optional<yh::DialogueCheck>& check)
{
    Choice* c = choice(node, index);
    if (!c || sameCheck(c->check, check))
        return false;
    if (check && (check->skill.empty() || check->difficulty < 0 || check->difficulty > 100000
        || (!check->success.empty() && !find(check->success)) || (!check->failure.empty() && !find(check->failure))))
        return false;
    edit(check ? "Change check" : "Remove check", [&] {
        if (check && !c->check)
        {
            c->check = check;
            if (c->check->success.empty())
                c->check->success = c->next;
            c->next.clear();
        }
        else if (!check)
        {
            c->next = c->check->success;
            c->check.reset();
        }
        else
            c->check = check;
    }, check && c->check ? "check-" + std::to_string(node) + "-" + std::to_string(index) : std::string());
    return true;
}

std::vector<std::string_view> DialogueEditor::actions()
{
    return {"recruit", "dismiss", "approve", "release", "kill", "fight"};
}

std::string DialogueEditor::actionProblem(std::string_view action, const Catalog& catalog, bool* error)
{
    bool wrong = true;
    std::string why;
    const std::vector<std::string> w = words(action);
    if (action.size() > maxAction)
        why = "an action is at most 200 characters";
    else if (w.empty())
        why = "an action can't be blank";
    else if (w[0] == "release" || w[0] == "kill" || w[0] == "fight")
    {
        if (w.size() != 1)
            why = w[0] + " takes nothing after it";
    }
    else if (w[0] == "recruit" || w[0] == "dismiss")
    {
        if (w.size() != 1)
            why = w[0] + " takes nothing after it, it is always the one being talked to";
        else if (!catalog.companion)
        {
            wrong = false;
            why = w[0] + " only works for an NPC with a companion entry, and this file isn't one's";
        }
    }
    else if (w[0] == "approve")
    {
        if ((w.size() != 2 && w.size() != 3) || !change(w.back()))
            why = "approve takes a number, or a companion and a number: approve 5, approve wren -3";
        else if (w.size() == 3 && !catalog.companions.contains(w[1]))
        {
            wrong = false;
            why = "approve names " + w[1] + ", who isn't a companion in this chapter";
        }
        else if (w.size() == 2 && !catalog.companion)
        {
            wrong = false;
            why = "approve without a name changes the one being talked to, who can't join here";
        }
    }
    else
    {
        wrong = false;
        why = "the game does nothing with \"" + std::string(action) + "\"";
    }
    if (error)
        *error = !why.empty() && wrong;
    return why;
}

std::vector<DialogueEditor::Problem> DialogueEditor::problems() const
{
    std::vector<Problem> out;
    if (!loaded_)
        return out;
    std::string why;
    if (!dialogue(&why))
        out.push_back({"the game would refuse it: " + why});

    // What the start can lead to; the rest is never heard.
    std::set<std::string> reached;
    std::vector<std::string> next{state_.start};
    while (!next.empty())
    {
        const std::string id = next.back();
        next.pop_back();
        const std::optional<size_t> at = find(id);
        if (!at || !reached.insert(id).second)
            continue;
        for (const Choice& c : state_.nodes[*at].choices)
        {
            next.push_back(c.check ? c.check->success : c.next);
            if (c.check)
                next.push_back(c.check->failure);
        }
    }

    auto actionsOf = [&](const yh::DialogueFlags& flags, const std::string& where) {
        for (const std::string& action : flags.actions)
        {
            bool error = false;
            if (const std::string problem = actionProblem(action, catalog_, &error); !problem.empty())
                out.push_back({where + ": " + problem, error});
        }
    };
    for (const Node& node : state_.nodes)
    {
        if (!reached.contains(node.id))
            out.push_back({node.id + " can't be reached from the start", false});
        if (node.text.empty())
            out.push_back({node.id + " has no line", false});
        actionsOf(node.flags, node.id);
        for (const Choice& c : node.choices)
        {
            const std::string where = node.id + "/" + c.id;
            actionsOf(c.flags, where);
            if (c.check && !catalog_.skills.empty()
                && std::find(catalog_.skills.begin(), catalog_.skills.end(), c.check->skill) == catalog_.skills.end())
                out.push_back({where + " checks " + c.check->skill + ", which the ruleset doesn't have", false});
        }
    }
    return out;
}

// ---------------------------------------------------------------- the desktop layout

namespace
{

constexpr float nodeColumn = 200, choiceColumn = 300;

const char* const textBoxes[] = {"dlg-id", "dlg-node-id", "dlg-speaker", "dlg-line", "dlg-set", "dlg-clear", "dlg-do",
    "dlg-choice-id", "dlg-choice-text", "dlg-require", "dlg-forbid", "dlg-dc"};

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

// The name `step` places along from `current` in `names`, round at the ends.
std::string stepName(const std::vector<std::string>& names, const std::string& current, int step)
{
    if (names.empty())
        return current;
    const auto found = std::find(names.begin(), names.end(), current);
    const size_t index = found == names.end() ? 0 : static_cast<size_t>(found - names.begin());
    return names[(index + names.size() + static_cast<size_t>(step + static_cast<int>(names.size()))) % names.size()];
}

std::optional<int> wholeNumber(const std::string& text)
{
    int value = 0;
    const auto [end, problem] = std::from_chars(text.data(), text.data() + text.size(), value);
    return problem == std::errc() && end == text.data() + text.size() && !text.empty() ? std::optional<int>(value) : std::nullopt;
}

// "a, b" to its names, blanks dropped.
std::vector<std::string> listFrom(const std::string& text)
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
        if (c == ',')
            finish();
        else
            item += c;
    }
    finish();
    return out;
}

std::string joined(const std::vector<std::string>& items)
{
    std::string text;
    for (const std::string& item : items)
        text += (text.empty() ? "" : ", ") + item;
    return text;
}

float widthOf(const yh::Ui& ui, std::string_view text)
{
    return ui.theme.font ? ui.theme.font->measure(text) : static_cast<float>(text.size()) * 8;
}

// Cut to fit `width`, with "..." where it was cut.
std::string fit(const yh::Ui& ui, const std::string& text, float width)
{
    if (widthOf(ui, text) <= width)
        return text;
    std::string cut = text;
    while (!cut.empty() && widthOf(ui, cut + "...") > width)
        cut.pop_back();
    return cut + "...";
}

std::vector<std::string> wrap(const yh::Ui& ui, const std::string& text, float width)
{
    std::vector<std::string> lines{std::string()};
    for (const std::string& word : words(text))
    {
        const std::string longer = lines.back().empty() ? word : lines.back() + " " + word;
        if (!lines.back().empty() && widthOf(ui, longer) > width)
            lines.push_back(word);
        else
            lines.back() = longer;
    }
    return lines;
}

// Where a reply goes, for the list.
std::string whereTo(const DialogueEditor::Choice& c)
{
    if (c.check)
        return c.check->skill + " " + std::to_string(c.check->difficulty) + ": " + (c.check->success.empty() ? "end" : c.check->success)
            + " / " + (c.check->failure.empty() ? "end" : c.check->failure);
    return "-> " + (c.next.empty() ? std::string("end") : c.next);
}

}

bool DialoguePanel::typing(const yh::Ui& ui)
{
    return std::any_of(std::begin(textBoxes), std::end(textBoxes), [&](const char* id) { return ui.editing(id); });
}

void DialoguePanel::draw(DialogueEditor& editor, yh::Ui& ui, const yh::Input& input, yh::Renderer& renderer, const yh::Rect& area)
{
    if (!editor.loaded())
        return;
    // An undo can take away the node or reply that was picked.
    if (node_ >= editor.nodes().size())
    {
        node_ = editor.nodes().size() - 1;
        choice_.reset();
    }
    if (choice_ && *choice_ >= editor.nodes()[node_].choices.size())
        choice_.reset();

    const bool nowTyping = typing(ui);
    if (wasTyping_ && !nowTyping)
        editor.endTyping();
    wasTyping_ = nowTyping;
    if (choice_ && !nowTyping && input.keyPressed(SDLK_DELETE))
    {
        editor.removeChoice(node_, *choice_);
        choice_.reset();
    }

    const yh::Rect left{area.x, area.y, nodeColumn, area.h};
    const yh::Rect right{area.x + area.w - choiceColumn, area.y, choiceColumn, area.h};
    const yh::Rect middle{left.x + left.w, area.y, area.w - left.w - right.w, area.h};
    ui.panel(left);
    drawNodes(editor, ui, left);
    drawNode(editor, ui, renderer, middle);
    ui.panel(right);
    if (choice_)
        drawChoice(editor, ui, right);
    else
        drawNodeFlags(editor, ui, right);
    if (!hint_.empty())
        ui.label({right.x + 8, right.y + right.h - 26}, fit(ui, hint_, right.w - 16), ui.theme.bad);
}

void DialoguePanel::drawNodes(DialogueEditor& editor, yh::Ui& ui, const yh::Rect& column)
{
    const float x = column.x + 8, w = column.w - 16, h = 28;
    float y = column.y + 8;
    ui.label({x, y}, "Conversation id", ui.theme.textDim);
    y += 20;
    if (field(ui, "dlg-id", {x, y, w, 30}, idText_, editor.id(), maxId) && !editor.setId(idText_))
        hint_ = "An id is 1 to 64 characters, no spaces";
    y += 40;

    ui.label({x, y}, "Nodes", ui.theme.accent);
    ui.label({x + 60, y}, "> starts", ui.theme.textDim);
    y += 24;
    const std::vector<DialogueEditor::Node>& nodes = editor.nodes();
    const float row = h + 3;
    const float bottom = column.y + column.h - 2 * (h + 6) - 8;
    const yh::Rect list{x, y, w, std::max(row, bottom - y)};
    ui.beginScroll(list, static_cast<float>(nodes.size()) * row, nodeScroll_);
    float top = 0;
    for (size_t i = 0; i < nodes.size(); i++)
    {
        const bool start = nodes[i].id == editor.start();
        if (ui.toggle({0, top, w, h}, fit(ui, (start ? "> " : "  ") + nodes[i].id, w - 16), i == node_))
        {
            node_ = i;
            choice_.reset();
            hint_.clear();
        }
        top += row;
    }
    ui.endScroll();

    y = bottom + 6;
    if (ui.button({x, y, w / 2 - 2, h}, "Add"))
    {
        if (const std::optional<size_t> added = editor.addNode())
        {
            node_ = *added;
            choice_.reset();
        }
    }
    if (ui.button({x + w / 2 + 2, y, w / 2 - 2, h}, "Remove", nodes.size() > 1))
    {
        editor.removeNode(node_);
        choice_.reset();
    }
    y += h + 6;
    if (ui.button({x, y, w, h}, "Start here", nodes[node_].id != editor.start()))
        editor.setStart(node_);
}

void DialoguePanel::drawNode(DialogueEditor& editor, yh::Ui& ui, yh::Renderer& renderer, const yh::Rect& column)
{
    renderer.fillRect(column, {26, 27, 34, 255});
    const float x = column.x + 12, w = column.w - 24, h = 28;
    float y = column.y + 10;
    // A copy: the commands below change the one in the editor.
    const DialogueEditor::Node node = editor.nodes()[node_];
    const float half = w / 2 - 4;

    ui.label({x, y}, "Node", ui.theme.textDim);
    ui.label({x + half + 8, y}, "Speaker", ui.theme.textDim);
    y += 20;
    if (field(ui, "dlg-node-id", {x, y, half, 30}, idText_, node.id, maxId) && !editor.renameNode(node_, idText_))
        hint_ = idText_.empty() ? "A node needs an id" : "Another node has that id, or it has a space";
    if (field(ui, "dlg-speaker", {x + half + 8, y, half, 30}, speakerText_, node.speaker, maxSpeaker))
        editor.setSpeaker(node_, speakerText_);
    y += 38;

    ui.label({x, y}, "Line", ui.theme.textDim);
    const int links = editor.linksTo(node.id);
    const std::string from = node.id == editor.start() ? std::string("the start")
        : links == 0 ? std::string("no reply leads here") : links == 1 ? std::string("1 reply leads here") : std::to_string(links) + " replies lead here";
    ui.label({x + w - widthOf(ui, from), y}, from, links == 0 && node.id != editor.start() ? ui.theme.bad : ui.theme.textDim);
    y += 20;
    if (field(ui, "dlg-line", {x, y, w, 30}, lineText_, node.text, maxLine))
        editor.setText(node_, lineText_);
    y += 36;

    // The whole line, wrapped, since the box shows only a part of a long one.
    const std::vector<std::string> lines = wrap(ui, node.speaker.empty() ? node.text : node.speaker + ": " + node.text, w - 16);
    const float lineStep = ui.lineHeight() - 4;
    const size_t shown = std::min<size_t>(lines.size(), 6);
    const yh::Rect said{x, y, w, static_cast<float>(std::max<size_t>(shown, 1)) * lineStep + 12};
    renderer.fillRect(said, {18, 19, 24, 255});
    renderer.drawRect(said, ui.theme.panelBorder, 1);
    ui.label({x + 8, y + 6}, node.text.empty() ? std::string("(no line yet)") : lines[0], node.text.empty() ? ui.theme.textDim : ui.theme.text);
    for (size_t i = 1; i < shown && !node.text.empty(); i++)
        ui.label({x + 8, y + 6 + static_cast<float>(i) * lineStep}, i + 1 == shown && lines.size() > shown ? lines[i] + " ..." : lines[i]);
    y += said.h + 14;

    ui.label({x, y}, "Replies", ui.theme.accent);
    ui.label({x + 74, y}, node.choices.empty() ? std::string("none, so this is the last line") : std::string("click one to edit it"), ui.theme.textDim);
    y += 24;
    const float bottom = column.y + column.h - h - 14;
    for (size_t i = 0; i < node.choices.size(); i++)
    {
        if (y + h + 18 > bottom)
        {
            ui.label({x, y}, "... and " + std::to_string(node.choices.size() - i) + " more", ui.theme.textDim);
            break;
        }
        const DialogueEditor::Choice& c = node.choices[i];
        if (ui.toggle({x, y, w, h}, fit(ui, std::to_string(i + 1) + ". " + c.text, w - 20), choice_ == i))
        {
            choice_ = choice_ == i ? std::nullopt : std::optional<size_t>(i);
            hint_.clear();
        }
        y += h + 2;
        std::string about = whereTo(c);
        if (!c.require.empty())
            about += "   needs " + joined(c.require);
        if (!c.forbid.empty())
            about += "   not if " + joined(c.forbid);
        if (!c.flags.set.empty())
            about += "   sets " + joined(c.flags.set);
        if (!c.flags.actions.empty())
            about += "   do " + joined(c.flags.actions);
        ui.label({x + 14, y}, fit(ui, about, w - 14), ui.theme.textDim);
        y += 22;
    }

    y = bottom + 4;
    const float quarter = (w - 12) / 4;
    if (ui.button({x, y, quarter, h}, "Add reply"))
        if (const std::optional<size_t> added = editor.addChoice(node_))
            choice_ = added;
    if (ui.button({x + quarter + 4, y, quarter, h}, "Up", choice_ && *choice_ > 0) && editor.moveChoice(node_, *choice_, -1))
        choice_ = *choice_ - 1;
    if (ui.button({x + 2 * (quarter + 4), y, quarter, h}, "Down", choice_ && *choice_ + 1 < node.choices.size()) && editor.moveChoice(node_, *choice_, 1))
        choice_ = *choice_ + 1;
    if (ui.button({x + 3 * (quarter + 4), y, quarter, h}, "Remove", choice_.has_value()))
    {
        editor.removeChoice(node_, *choice_);
        choice_.reset();
    }
}

bool DialoguePanel::flagFields(yh::Ui& ui, const DialogueEditor::Catalog& catalog, float x, float& y, float w, const yh::DialogueFlags& flags,
    yh::DialogueFlags& changed)
{
    bool asked = false;
    changed = flags;
    ui.label({x, y}, "Sets flags", ui.theme.textDim);
    y += 20;
    if (field(ui, "dlg-set", {x, y, w, 30}, setText_, joined(flags.set), 400) && listFrom(setText_) != flags.set)
    {
        changed.set = listFrom(setText_);
        asked = true;
    }
    y += 36;
    ui.label({x, y}, "Clears flags", ui.theme.textDim);
    y += 20;
    if (field(ui, "dlg-clear", {x, y, w, 30}, clearText_, joined(flags.clear), 400) && listFrom(clearText_) != flags.clear)
    {
        changed.clear = listFrom(clearText_);
        asked = true;
    }
    y += 36;
    ui.label({x, y}, "Does", ui.theme.textDim);
    y += 20;
    if (field(ui, "dlg-do", {x, y, w, 30}, doText_, joined(flags.actions), 400) && listFrom(doText_) != flags.actions)
    {
        changed.actions = listFrom(doText_);
        asked = true;
    }
    y += 34;

    // The companion actions, so nobody has to remember how they are spelled. Approve steps the
    // one being talked to by one each click.
    auto has = [&](std::string_view action) { return std::find(flags.actions.begin(), flags.actions.end(), action) != flags.actions.end(); };
    auto approve = [&](int by) {
        changed.actions = flags.actions;
        for (size_t i = 0; i < changed.actions.size(); i++)
        {
            const std::vector<std::string> parts = words(changed.actions[i]);
            const std::optional<int> now = parts.size() == 2 && parts[0] == "approve" ? change(parts[1]) : std::nullopt;
            if (!now)
                continue;
            if (*now + by == 0)
                changed.actions.erase(changed.actions.begin() + static_cast<std::ptrdiff_t>(i));
            else
                changed.actions[i] = "approve " + std::to_string(*now + by);
            return;
        }
        changed.actions.push_back("approve " + std::to_string(by));
    };
    const float half = w / 2 - 2;
    if (ui.button({x, y, half, 28}, "Recruit", !has("recruit")))
    {
        changed.actions = flags.actions;
        changed.actions.push_back("recruit");
        asked = true;
    }
    if (ui.button({x + half + 4, y, half, 28}, "Dismiss", !has("dismiss")))
    {
        changed.actions = flags.actions;
        changed.actions.push_back("dismiss");
        asked = true;
    }
    y += 32;
    if (ui.button({x, y, half, 28}, "Approve +1"))
    {
        approve(1);
        asked = true;
    }
    if (ui.button({x + half + 4, y, half, 28}, "Approve -1"))
    {
        approve(-1);
        asked = true;
    }
    y += 34;
    for (const std::string& action : flags.actions)
    {
        bool error = false;
        if (const std::string why = DialogueEditor::actionProblem(action, catalog, &error); !why.empty())
        {
            ui.label({x, y}, fit(ui, why, w), error ? ui.theme.bad : ui.theme.textDim);
            y += 20;
            break;
        }
    }
    return asked;
}

void DialoguePanel::drawNodeFlags(DialogueEditor& editor, yh::Ui& ui, const yh::Rect& column)
{
    const float x = column.x + 8, w = column.w - 16;
    float y = column.y + 8;
    const DialogueEditor::Node node = editor.nodes()[node_];
    ui.label({x, y}, "On reaching " + node.id, ui.theme.accent);
    y += 22;
    ui.label({x, y}, "Pick a reply to edit it instead.", ui.theme.textDim);
    y += 28;
    yh::DialogueFlags changed;
    if (flagFields(ui, editor.catalog(), x, y, w, node.flags, changed))
    {
        // A button press is its own undo step, apart from any typing before or after it.
        const bool clicked = !typing(ui);
        if (clicked)
            editor.endTyping();
        if (!editor.setNodeFlags(node_, changed))
            hint_ = "Flags are 1 to 64 characters, not both set and cleared";
        else
            hint_.clear();
        if (clicked)
            editor.endTyping();
    }
}

void DialoguePanel::drawChoice(DialogueEditor& editor, yh::Ui& ui, const yh::Rect& column)
{
    const float x = column.x + 8, w = column.w - 16, h = 28;
    float y = column.y + 8;
    const DialogueEditor::Choice c = editor.nodes()[node_].choices[*choice_]; // a copy, as in drawNode
    const size_t at = *choice_;

    ui.label({x, y}, "Reply " + std::to_string(at + 1), ui.theme.accent);
    if (ui.button({x + w - 110, y - 4, 110, 24}, "Node's own"))
    {
        choice_.reset();
        return;
    }
    y += 24;
    if (field(ui, "dlg-choice-id", {x, y, w, 30}, choiceIdText_, c.id, maxId) && !editor.setChoiceId(node_, at, choiceIdText_))
        hint_ = "Another reply here has that id, or it has a space";
    y += 34;
    if (field(ui, "dlg-choice-text", {x, y, w, 30}, choiceText_, c.text, maxReply) && !editor.setChoiceText(node_, at, choiceText_))
        hint_ = "A reply needs some words";
    y += 38;

    // Where it goes: a node, the end, or a roll that picks between two.
    std::vector<std::string> targets{std::string()};
    for (const DialogueEditor::Node& n : editor.nodes())
        targets.push_back(n.id);
    auto shown = [](const std::string& id) { return id.empty() ? std::string("end") : id; };
    bool checked = c.check.has_value();
    if (ui.checkbox({x, y, w, h}, "Skill check", checked))
    {
        editor.endTyping();
        if (checked)
        {
            const std::string skill = editor.catalog().skills.empty() ? std::string("persuasion") : editor.catalog().skills.front();
            editor.setCheck(node_, at, yh::DialogueCheck{skill, 12, {}, {}});
        }
        else
            editor.setCheck(node_, at, std::nullopt);
        return;
    }
    y += h + 4;
    if (!c.check)
    {
        ui.label({x, y + 5}, "Goes to", ui.theme.textDim);
        if (const int step = stepper(ui, {x + 64, y, w - 64, h}, shown(c.next)))
            editor.setNext(node_, at, stepName(targets, c.next, step));
        y += h + 4;
    }
    else
    {
        yh::DialogueCheck check = *c.check;
        std::vector<std::string> skills = editor.catalog().skills;
        if (std::find(skills.begin(), skills.end(), check.skill) == skills.end())
            skills.insert(skills.begin(), check.skill);
        const float third = 64;
        ui.label({x, y + 5}, "Rolls", ui.theme.textDim);
        if (const int step = stepper(ui, {x + third, y, w - third - 58, h}, check.skill))
        {
            check.skill = stepName(skills, check.skill, step);
            editor.setCheck(node_, at, check);
        }
        if (field(ui, "dlg-dc", {x + w - 52, y, 52, h}, difficultyText_, std::to_string(c.check->difficulty), 6))
        {
            const std::optional<int> dc = wholeNumber(difficultyText_);
            check.difficulty = dc.value_or(-1);
            if (!dc || (*dc != c.check->difficulty && !editor.setCheck(node_, at, check)))
                hint_ = "The difficulty is a whole number, 0 to 100000";
        }
        y += h + 4;
        ui.label({x, y + 5}, "Pass", ui.theme.textDim);
        if (const int step = stepper(ui, {x + third, y, w - third, h}, shown(check.success)))
        {
            check.success = stepName(targets, check.success, step);
            editor.setCheck(node_, at, check);
        }
        y += h + 4;
        ui.label({x, y + 5}, "Fail", ui.theme.textDim);
        if (const int step = stepper(ui, {x + third, y, w - third, h}, shown(check.failure)))
        {
            check.failure = stepName(targets, check.failure, step);
            editor.setCheck(node_, at, check);
        }
        y += h + 4;
    }
    if (ui.button({x, y, w, h}, c.check ? "New node for an empty way" : "New node after it", !c.check || c.check->success.empty() || c.check->failure.empty()))
        if (const std::optional<size_t> added = editor.branch(node_, at))
        {
            node_ = *added;
            choice_.reset();
            return;
        }
    y += h + 10;

    const float half = w / 2 - 3;
    ui.label({x, y}, "Needs flags", ui.theme.textDim);
    ui.label({x + half + 6, y}, "Hidden if", ui.theme.textDim);
    y += 20;
    const bool requireTyped = field(ui, "dlg-require", {x, y, half, 30}, requireText_, joined(c.require), 400);
    const bool forbidTyped = field(ui, "dlg-forbid", {x + half + 6, y, half, 30}, forbidText_, joined(c.forbid), 400);
    if (requireTyped || forbidTyped)
    {
        const std::vector<std::string> require = listFrom(requireText_), forbid = listFrom(forbidText_);
        if ((require != c.require || forbid != c.forbid) && !editor.setConditions(node_, at, require, forbid))
            hint_ = "A flag can't be both needed and hidden by";
    }
    y += 40;

    yh::DialogueFlags changed;
    if (flagFields(ui, editor.catalog(), x, y, w, c.flags, changed))
    {
        const bool clicked = !typing(ui);
        if (clicked)
            editor.endTyping();
        if (!editor.setChoiceFlags(node_, at, changed))
            hint_ = "Flags are 1 to 64 characters, not both set and cleared";
        else
            hint_.clear();
        if (clicked)
            editor.endTyping();
    }
}
