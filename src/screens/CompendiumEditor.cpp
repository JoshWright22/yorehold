// Compendium mode of the Create screen: a package's definition files, edited through forms built
// from create/compendium.json, and the desktop layout over them.

#include "CompendiumEditor.h"

#include <yorehold/framework/map/Objects.h>
#include <yorehold/framework/rpg/CharacterOptions.h>
#include <yorehold/framework/rpg/Compendium.h>
#include <yorehold/framework/rpg/Spell.h>
#include <yorehold/framework/rpg/Tactics.h>

#include <algorithm>
#include <memory>
#include <set>

namespace
{

constexpr size_t maxId = 64;

// "items/rope.json" -> "rope"
std::string stem(const std::string& path)
{
    const size_t slash = path.rfind('/');
    std::string name = slash == std::string::npos ? path : path.substr(slash + 1);
    if (name.ends_with(".json"))
        name.resize(name.size() - 5);
    return name;
}

std::string folderOf(const std::string& path)
{
    const size_t slash = path.rfind('/');
    return slash == std::string::npos ? std::string() : path.substr(0, slash);
}

// What the game's own reader says about a file: "" when it would load it.
std::string readerProblem(const std::string& reader, const std::string& text, const yh::FormOptions& lists)
{
    std::string error;
    bool ok = true;
    if (reader == "item")
        ok = yh::Compendium::itemFromJson(text, &error).has_value();
    else if (reader == "class")
        ok = yh::Compendium::classFromJson(text, &error).has_value();
    else if (reader == "creature")
        ok = yh::Compendium::creatureFromJson(text, &error).has_value();
    else if (reader == "kit")
        ok = yh::Kit::fromJson(text, &error).has_value();
    else if (reader == "spell")
        ok = yh::SpellDefinition::fromJson(text, &error).has_value();
    else if (reader == "race")
        ok = yh::raceFromJson(text, &error).has_value();
    else if (reader == "background")
        ok = yh::backgroundFromJson(text, &error).has_value();
    else if (reader == "feat")
        ok = yh::featFromJson(text, &error).has_value();
    else if (reader == "ai")
    {
        // A profile may start from any other: the built-in ones stand in for those in files,
        // since only the shape of this one is being checked.
        const auto found = lists.find("ai");
        const yh::AiProfile::Lookup lookup = [&](std::string_view name) -> const yh::AiProfile* {
            if (const yh::AiProfile* preset = yh::AiProfile::preset(name))
                return preset;
            if (found != lists.end() && std::find(found->second.begin(), found->second.end(), name) != found->second.end())
                return yh::AiProfile::preset("cunning");
            return nullptr;
        };
        ok = yh::AiProfile::fromJson(text, &error, lookup).has_value();
    }
    if (ok)
        return {};
    return error.empty() ? std::string("the game can't read it") : error;
}

}

bool CompendiumEditor::validId(std::string_view id)
{
    return !id.empty() && id.size() <= maxId
        && std::all_of(id.begin(), id.end(), [](char c) { return (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_'; });
}

bool CompendiumEditor::setKinds(std::string_view json, std::string* error)
{
    auto fail = [&](std::string why) {
        if (error)
            *error = std::move(why);
        return false;
    };
    const yh::FormJson j = yh::FormJson::parse(json, nullptr, false);
    if (!j.is_object() || !j.contains("kinds") || !j.at("kinds").is_array())
        return fail("the forms file is an object with a list of kinds");
    std::vector<Kind> kinds;
    for (const yh::FormJson& k : j.at("kinds"))
    {
        std::string why;
        std::optional<yh::FormSchema> form = yh::FormSchema::fromObject(k, &why);
        if (!form)
            return fail(why);
        if (form->folder.empty() || !validId(form->folder))
            return fail(form->id + ": folder is one plain folder name");
        if (std::any_of(kinds.begin(), kinds.end(), [&](const Kind& other) { return other.form.id == form->id; }))
            return fail(form->id + ": listed twice");
        Kind kind;
        kind.form = std::move(*form);
        kind.reader = k.value("reader", std::string());
        kind.ruleset = k.value("ruleset", false);
        kinds.push_back(std::move(kind));
    }
    kinds_ = std::move(kinds);
    state_.clear();
    saved_.clear();
    return true;
}

const CompendiumEditor::Kind* CompendiumEditor::kind(std::string_view id) const
{
    const auto found = std::find_if(kinds_.begin(), kinds_.end(), [&](const Kind& k) { return k.form.id == id; });
    return found == kinds_.end() ? nullptr : &*found;
}

yh::FormOptions CompendiumEditor::options() const
{
    yh::FormOptions lists = options_;
    for (const Entry& entry : state_)
        if (const Kind* k = kind(entry.kind))
        {
            std::vector<std::string>& list = lists[k->form.folder];
            if (std::find(list.begin(), list.end(), entry.id) == list.end())
                list.push_back(entry.id);
        }
    for (auto& [name, list] : lists)
        std::sort(list.begin(), list.end());
    return lists;
}

bool CompendiumEditor::addFile(const std::string& kindId, const std::string& path, std::string_view text, std::string* error)
{
    const Kind* k = kind(kindId);
    yh::FormJson value = yh::FormJson::parse(text, nullptr, false);
    if (!k || !value.is_object())
    {
        if (error)
            *error = path + (k ? ": isn't a JSON object" : ": no form for \"" + kindId + "\"");
        return false;
    }
    Entry entry{kindId, stem(path), path, std::move(value)};
    if (const std::optional<size_t> found = find(path))
        state_[*found] = std::move(entry);
    else
        state_.push_back(std::move(entry));
    // Compared in the editor's own form, so a hand-written file isn't rewritten until it is changed.
    saved_[path] = toJson(*find(path));
    return true;
}

std::vector<size_t> CompendiumEditor::of(std::string_view kindId) const
{
    std::vector<size_t> out;
    for (size_t i = 0; i < state_.size(); i++)
        if (state_[i].kind == kindId)
            out.push_back(i);
    std::sort(out.begin(), out.end(), [&](size_t a, size_t b) {
        return state_[a].id != state_[b].id ? state_[a].id < state_[b].id : state_[a].path < state_[b].path;
    });
    return out;
}

std::optional<size_t> CompendiumEditor::find(std::string_view path) const
{
    for (size_t i = 0; i < state_.size(); i++)
        if (state_[i].path == path)
            return i;
    return std::nullopt;
}

template <typename Change>
void CompendiumEditor::edit(std::string_view label, Change change, std::string_view mergeKey)
{
    const auto before = std::make_shared<const std::vector<Entry>>(state_);
    change();
    const auto after = std::make_shared<const std::vector<Entry>>(state_);
    history_.record(label, [this, after] { state_ = *after; }, [this, before] { state_ = *before; }, mergeKey);
}

std::string CompendiumEditor::freeId(const std::string& folder, const std::string& base) const
{
    std::string id = base;
    for (int n = 2; find(folder + "/" + id + ".json") || saved_.contains(folder + "/" + id + ".json"); n++)
        id = base + "-" + std::to_string(n);
    return id;
}

std::optional<size_t> CompendiumEditor::add(const std::string& kindId, const std::string& folder, std::string id)
{
    const Kind* k = kind(kindId);
    if (!k || folder.empty())
        return std::nullopt;
    if (id.empty())
        id = freeId(folder, "new-" + kindId);
    const std::string path = folder + "/" + id + ".json";
    if (!validId(id) || find(path) || saved_.contains(path))
        return std::nullopt;
    Entry entry{kindId, id, path, k->form.blank(id)};
    edit("Add " + id, [&] { state_.push_back(std::move(entry)); });
    return state_.size() - 1;
}

std::optional<size_t> CompendiumEditor::copy(size_t index, std::string id)
{
    if (index >= state_.size())
        return std::nullopt;
    const Entry& from = state_[index];
    const Kind* k = kind(from.kind);
    const std::string folder = folderOf(from.path);
    if (id.empty())
        id = freeId(folder, from.id + "-copy");
    const std::string path = folder + "/" + id + ".json";
    if (!k || !validId(id) || find(path) || saved_.contains(path))
        return std::nullopt;
    Entry entry{from.kind, id, path, from.value};
    if (!k->form.idKey.empty())
        entry.value[k->form.idKey] = id;
    edit("Copy " + from.id, [&] { state_.push_back(std::move(entry)); });
    return state_.size() - 1;
}

bool CompendiumEditor::setField(size_t index, std::string_view key, std::string_view text, std::string* error)
{
    if (index >= state_.size())
        return false;
    const Kind* k = kind(state_[index].kind);
    const yh::FormField* field = k ? k->form.field(key) : nullptr;
    if (!field)
    {
        if (error)
            *error = std::string(key) + " isn't on this form";
        return false;
    }
    const std::optional<yh::FormJson> value = field->parse(text, error);
    return value && setValue(index, key, *value);
}

bool CompendiumEditor::setValue(size_t index, std::string_view key, const yh::FormJson& value)
{
    if (index >= state_.size())
        return false;
    const Kind* k = kind(state_[index].kind);
    // The id follows the file name, so it isn't changed here.
    if (!k || !k->form.field(key) || key == k->form.idKey)
        return false;
    const yh::FormJson& object = state_[index].value;
    const std::string name(key);
    const bool had = object.contains(name);
    if ((value.is_null() && !had) || (had && object.at(name) == value))
        return true;
    const Entry& entry = state_[index];
    edit("Change " + entry.id + " " + k->form.field(key)->title(),
        [&] {
            yh::FormJson& target = state_[index].value;
            if (value.is_null())
                target.erase(name);
            else
                target[name] = value; // a new key goes at the end, existing ones stay where they were
        },
        "compendium:" + entry.path + ":" + name);
    return true;
}

std::string CompendiumEditor::toJson(size_t index) const
{
    return index < state_.size() ? state_[index].value.dump(2) + "\n" : std::string();
}

std::vector<CompendiumEditor::Problem> CompendiumEditor::problems(size_t index) const
{
    std::vector<Problem> out;
    if (index >= state_.size())
        return out;
    const Entry& entry = state_[index];
    const Kind* k = kind(entry.kind);
    if (!k)
        return {{"no form for " + entry.kind}};
    const yh::FormOptions lists = options();
    if (!validId(entry.id))
        out.push_back({"the file name is lowercase letters, digits, - and _"});
    if (!k->form.idKey.empty())
    {
        const yh::FormJson& id = entry.value.contains(k->form.idKey) ? entry.value.at(k->form.idKey) : yh::FormJson();
        if (!id.is_string() || id.get<std::string>() != entry.id)
            out.push_back({k->form.idKey + " has to be the file name, \"" + entry.id + "\""});
    }
    std::vector<std::string> warnings;
    for (std::string& text : k->form.problems(entry.value, lists, &warnings))
        out.push_back({std::move(text)});
    // The game's own reader has the last word on what loads.
    if (out.empty() && !k->reader.empty())
        if (std::string why = readerProblem(k->reader, entry.value.dump(), lists); !why.empty())
            out.push_back({std::move(why)});
    for (std::string& text : warnings)
        out.push_back({std::move(text), false});
    return out;
}

std::vector<size_t> CompendiumEditor::changed() const
{
    std::vector<size_t> out;
    for (size_t i = 0; i < state_.size(); i++)
    {
        const auto saved = saved_.find(state_[i].path);
        if (saved == saved_.end() || saved->second != toJson(i))
            out.push_back(i);
    }
    return out;
}

void CompendiumEditor::markSaved(size_t index)
{
    if (index < state_.size())
        saved_[state_[index].path] = toJson(index);
}

// The layout.

namespace
{

constexpr float kindColumn = 170, entryColumn = 230, labelWidth = 150;

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

}

bool CompendiumPanel::typing(const yh::Ui& ui) const
{
    return std::any_of(boxes_.begin(), boxes_.end(), [&](const std::string& id) { return ui.editing(id); });
}

void CompendiumPanel::draw(CompendiumEditor& editor, const std::map<std::string, std::string>& folders, yh::Ui& ui, const yh::Input&,
    yh::Renderer& renderer, const yh::Rect& area)
{
    if (editor.kinds().empty())
        return;
    const bool nowTyping = typing(ui);
    if (wasTyping_ && !nowTyping)
        editor.endTyping();
    wasTyping_ = nowTyping;
    boxes_.clear();

    if (!editor.kind(kind_))
        kind_ = editor.kinds().front().form.id;
    // An undo can take away the entry that was picked.
    const std::vector<size_t> listed = editor.of(kind_);
    std::optional<size_t> picked = editor.find(path_);
    if (!picked || editor.entries()[*picked].kind != kind_)
    {
        picked = listed.empty() ? std::nullopt : std::optional<size_t>(listed.front());
        path_ = picked ? editor.entries()[*picked].path : std::string();
    }

    // Kinds.
    const yh::Rect left{area.x, area.y, kindColumn, area.h};
    ui.panel(left);
    float y = left.y + 8;
    ui.label({left.x + 8, y}, "Kinds", ui.theme.accent);
    y += 26;
    for (const CompendiumEditor::Kind& k : editor.kinds())
    {
        if (k.ruleset && y + 2 < left.y + left.h && &k != &editor.kinds().front() && !(&k - 1)->ruleset)
        {
            ui.label({left.x + 8, y + 2}, "Ruleset", ui.theme.textDim);
            y += 24;
        }
        const std::string text = (k.form.label.empty() ? k.form.id : k.form.label) + " (" + std::to_string(editor.of(k.form.id).size()) + ")";
        if (ui.toggle({left.x + 8, y, left.w - 16, 28}, fit(ui, text, left.w - 32), k.form.id == kind_))
        {
            kind_ = k.form.id;
            path_.clear();
            hint_.clear();
            formScroll_ = 0;
        }
        y += 31;
    }

    const yh::Rect entries{left.x + left.w, area.y, entryColumn, area.h};
    ui.panel(entries);
    drawEntries(editor, folders, ui, entries);
    drawForm(editor, ui, renderer, {entries.x + entries.w, area.y, area.w - left.w - entries.w, area.h});
}

void CompendiumPanel::drawEntries(CompendiumEditor& editor, const std::map<std::string, std::string>& folders, yh::Ui& ui, const yh::Rect& column)
{
    const float x = column.x + 8, w = column.w - 16, h = 28;
    float y = column.y + 8;
    const std::vector<size_t> listed = editor.of(kind_);
    ui.label({x, y}, listed.empty() ? std::string("None yet") : std::to_string(listed.size()) + " in this package", ui.theme.textDim);
    y += 24;

    const float row = h + 3;
    const float bottom = column.y + column.h - 3 * (h + 6) - 26;
    const yh::Rect list{x, y, w, std::max(row, bottom - y)};
    ui.beginScroll(list, static_cast<float>(listed.size()) * row, entryScroll_);
    float top = 0;
    const std::vector<size_t> changed = editor.changed();
    for (const size_t i : listed)
    {
        const CompendiumEditor::Entry& entry = editor.entries()[i];
        // Where it lives when it isn't the usual folder: a chapter's own, or a ruleset's.
        const std::string folder = entry.path.substr(0, entry.path.rfind('/'));
        std::string text = entry.id;
        if (folder.starts_with("chapters/"))
            text += "  (" + folder.substr(9, folder.find('/', 9) - 9) + ")";
        if (std::find(changed.begin(), changed.end(), i) != changed.end())
            text += " *";
        if (ui.toggle({0, top, w, h}, fit(ui, text, w - 16), entry.path == path_))
        {
            path_ = entry.path;
            hint_.clear();
            formScroll_ = 0;
        }
        top += row;
    }
    ui.endScroll();

    y = bottom + 6;
    const auto folder = folders.find(kind_);
    ui.label({x, y}, "New id", ui.theme.textDim);
    y += 20;
    boxes_.insert("cmp-new-id");
    ui.textBox("cmp-new-id", {x, y, w, 30}, newId_, maxId);
    y += h + 8;
    const bool canAdd = folder != folders.end() && (newId_.empty() || CompendiumEditor::validId(newId_));
    if (ui.button({x, y, w / 2 - 2, h}, "Add", canAdd))
    {
        if (const std::optional<size_t> made = editor.add(kind_, folder->second, newId_))
        {
            path_ = editor.entries()[*made].path;
            newId_.clear();
            hint_.clear();
        }
        else
            hint_ = "That id is taken";
    }
    const std::optional<size_t> picked = editor.find(path_);
    if (ui.button({x + w / 2 + 2, y, w / 2 - 2, h}, "Copy", picked.has_value() && (newId_.empty() || CompendiumEditor::validId(newId_))))
    {
        if (const std::optional<size_t> made = editor.copy(*picked, newId_))
        {
            path_ = editor.entries()[*made].path;
            newId_.clear();
            hint_.clear();
        }
        else
            hint_ = "That id is taken";
    }
    y += h + 6;
    if (folder == folders.end())
        ui.label({x, y}, "No ruleset folder to add to", ui.theme.textDim);
    else if (!newId_.empty() && !CompendiumEditor::validId(newId_))
        ui.label({x, y}, "a-z, 0-9, - and _ only", ui.theme.bad);
    else
        ui.label({x, y}, fit(ui, "Goes in " + folder->second + "/", w), ui.theme.textDim);
}

void CompendiumPanel::drawForm(CompendiumEditor& editor, yh::Ui& ui, yh::Renderer& renderer, const yh::Rect& column)
{
    renderer.fillRect(column, {26, 27, 34, 255});
    const std::optional<size_t> picked = editor.find(path_);
    const CompendiumEditor::Kind* k = editor.kind(kind_);
    if (!picked || !k)
    {
        ui.label({column.x + 16, column.y + 16}, "Nothing here yet. Type an id and press Add.", ui.theme.textDim);
        return;
    }
    const size_t index = *picked;
    const float x = column.x + 12, w = column.w - 24;
    float y = column.y + 10;
    {
        const CompendiumEditor::Entry& entry = editor.entries()[index];
        ui.label({x, y}, entry.id, ui.theme.accent);
        ui.label({x + widthOf(ui, entry.id) + 16, y}, fit(ui, entry.path, w - widthOf(ui, entry.id) - 16), ui.theme.textDim);
    }
    y += 28;

    // What is wrong, at the bottom; the form scrolls above it.
    const std::vector<CompendiumEditor::Problem> problems = editor.problems(index);
    const size_t shownProblems = std::min<size_t>(problems.size(), 3);
    const float problemHeight = static_cast<float>(std::max<size_t>(shownProblems, 1)) * 20 + (hint_.empty() ? 8 : 28);
    const yh::Rect body{x, y, w, column.y + column.h - y - problemHeight - 4};

    const yh::FormOptions lists = editor.options();
    const float rowHeight = 36, helpHeight = 18;
    const std::vector<std::string> others = k->form.unlisted(editor.entries()[index].value);
    float content = 0;
    for (const yh::FormField& field : k->form.fields)
        content += rowHeight + (field.help.empty() ? 0 : helpHeight);
    content += others.empty() ? 0 : 30 + static_cast<float>(others.size()) * 22;

    ui.beginScroll(body, content, formScroll_);
    float top = 0;
    const float boxX = labelWidth, boxW = w - labelWidth - 4;
    for (const yh::FormField& field : k->form.fields)
    {
        // Read again each row: a change in the row above may have moved the value.
        const yh::FormJson& value = editor.entries()[index].value;
        const bool required = field.required;
        ui.label({0, top + 6}, fit(ui, field.title() + (required ? " *" : ""), labelWidth - 8), required ? ui.theme.text : ui.theme.textDim);
        const yh::Rect box{boxX, top, boxW, 30};
        const std::string id = "cmp-" + field.key;
        const std::vector<std::string> choices = field.type == yh::FormField::Type::Choice ? field.choices(lists) : std::vector<std::string>{};
        if (field.type == yh::FormField::Type::Flag)
        {
            bool on = value.contains(field.key) && value.at(field.key).is_boolean() && value.at(field.key).get<bool>();
            const bool set = value.contains(field.key);
            if (ui.checkbox({boxX, top, 140, 30}, on ? "yes" : "no", on))
                editor.setValue(index, field.key, on);
            if (set && !required && ui.button({boxX + 148, top, 80, 30}, "Clear"))
                editor.setValue(index, field.key, nullptr);
            if (!set)
                ui.label({boxX + 150, top + 6}, "not set", ui.theme.textDim);
        }
        else if (!choices.empty())
        {
            // Steps through what can be picked; "(none)" leaves it out when it isn't required.
            std::vector<std::string> names = choices;
            if (!required && std::find(names.begin(), names.end(), std::string()) == names.end())
                names.insert(names.begin(), std::string());
            const std::string current = field.text(value);
            const auto at = std::find(names.begin(), names.end(), current);
            const bool known = at != names.end();
            const size_t place = known ? static_cast<size_t>(at - names.begin()) : 0;
            int step = 0;
            if (ui.button({boxX, top, 28, 30}, "<"))
                step = -1;
            if (ui.button({boxX + boxW - 28, top, 28, 30}, ">"))
                step = 1;
            const std::string shown = current.empty() ? std::string("(none)") : current + (known ? "" : "  (not offered)");
            ui.label({boxX + 38, top + 6}, fit(ui, shown, boxW - 76), current.empty() || !known ? ui.theme.textDim : ui.theme.text);
            if (step != 0)
            {
                const size_t next = known ? (place + names.size() + static_cast<size_t>(step + static_cast<int>(names.size()))) % names.size() : 0;
                std::string why;
                if (!editor.setField(index, field.key, names[next], &why))
                    hint_ = why;
            }
        }
        else
        {
            boxes_.insert(id);
            const std::string current = field.text(value);
            std::string& typed = typed_[id];
            if (!ui.editing(id))
                typed = current;
            ui.textBox(id, box, typed, field.type == yh::FormField::Type::Json ? 65536 : 4000);
            if (typed != current)
            {
                std::string why;
                if (editor.setField(index, field.key, typed, &why))
                    hint_.clear();
                else
                    hint_ = why;
            }
        }
        top += rowHeight;
        if (!field.help.empty())
        {
            ui.label({boxX + 4, top - 4}, fit(ui, field.help, boxW - 8), ui.theme.textDim);
            top += helpHeight;
        }
    }
    if (!others.empty())
    {
        top += 6;
        ui.label({0, top}, "Other fields, kept as written", ui.theme.accent);
        top += 24;
        const yh::FormJson& value = editor.entries()[index].value;
        for (const std::string& key : others)
        {
            ui.label({0, top}, fit(ui, key, labelWidth - 8), ui.theme.textDim);
            ui.label({boxX, top}, fit(ui, value.at(key).dump(), boxW), ui.theme.textDim);
            top += 22;
        }
    }
    ui.endScroll();

    float py = body.y + body.h + 6;
    if (problems.empty())
        ui.label({x, py}, "Nothing wrong", ui.theme.good);
    for (size_t i = 0; i < shownProblems; i++)
    {
        std::string text = problems[i].text;
        if (i + 1 == shownProblems && problems.size() > shownProblems)
            text += "  (+" + std::to_string(problems.size() - shownProblems) + " more)";
        ui.label({x, py}, fit(ui, text, w), problems[i].error ? ui.theme.bad : ui.theme.accent);
        py += 20;
    }
    if (!hint_.empty())
        ui.label({x, column.y + column.h - 24}, fit(ui, hint_, w), ui.theme.bad);
}
