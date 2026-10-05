// Voice lines in Create: the package's voice files, their undo steps, and the panel that imports
// recordings off the main thread.

#include "screens/VoiceImporter.h"

#include <nlohmann/json.hpp>
#include <SDL3/SDL_clipboard.h>

#include <algorithm>
#include <chrono>
#include <cstdio>

namespace
{

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

std::vector<std::string> wrap(const yh::Ui& ui, const std::string& text, float width)
{
    std::vector<std::string> lines{std::string()};
    for (const std::string& word : splitWords(text))
    {
        const std::string longer = lines.back().empty() ? word : lines.back() + " " + word;
        if (!lines.back().empty() && widthOf(ui, longer) > width)
            lines.push_back(word);
        else
            lines.back() = longer;
    }
    return lines;
}

std::string seconds(double value)
{
    char text[32];
    std::snprintf(text, sizeof text, "%.2f", value);
    return text;
}

std::vector<std::string> normalized(std::string_view text)
{
    std::vector<std::string> out;
    for (const std::string& word : splitWords(text))
        if (std::string n = normalizeWord(word); !n.empty())
            out.push_back(std::move(n));
    return out;
}

}

// ---- VoiceImporter ----

std::optional<VoiceImporter::Settings> VoiceImporter::Settings::fromJson(std::string_view json, std::string* error)
{
    const nlohmann::json j = nlohmann::json::parse(json, nullptr, false);
    auto fail = [&](const char* why) -> std::optional<Settings> {
        if (error)
            *error = why;
        return std::nullopt;
    };
    if (j.is_discarded() || !j.is_object())
        return fail("not a JSON object");
    Settings settings;
    if (j.contains("flagBelow"))
    {
        if (!j["flagBelow"].is_number() || j["flagBelow"].get<double>() < 0 || j["flagBelow"].get<double>() > 1)
            return fail("flagBelow is a number from 0 to 1");
        settings.flagBelow = j["flagBelow"].get<float>();
    }
    if (j.contains("extensions"))
    {
        settings.extensions.clear();
        if (!j["extensions"].is_array())
            return fail("extensions is a list like [\"wav\", \"ogg\"]");
        for (const nlohmann::json& e : j["extensions"])
        {
            if (!e.is_string() || e.get<std::string>().empty())
                return fail("extensions is a list like [\"wav\", \"ogg\"]");
            settings.extensions.push_back(e.get<std::string>());
        }
    }
    return settings;
}

std::string VoiceImporter::stem(std::string_view conversation, std::string_view node)
{
    return std::string(conversation) + "." + std::string(node);
}

const VoiceImporter::Line& VoiceImporter::line(const yh::FileSystem& files, const std::string& stem, bool again)
{
    auto found = lines_.find(stem);
    if (found != lines_.end())
    {
        const Line& known = found->second;
        const bool unsaved = known.voice && known.voice->toJson() != known.saved;
        if (!again || unsaved)
            return known;
    }
    Line& line = lines_[stem];
    line.stem = stem;
    line.audio.clear();
    for (const std::string& extension : settings_.extensions)
        if (const std::string path = "voice/" + stem + "." + extension; files.exists(path))
        {
            line.audio = path;
            break;
        }
    line.voice.reset();
    line.saved.clear();
    line.error.clear();
    if (const std::optional<std::string> text = files.readText(voicePath(stem)))
    {
        std::string error;
        line.voice = VoiceLine::fromJson(*text, &error);
        if (line.voice)
            line.saved = line.voice->toJson();
        else
            line.error = voicePath(stem) + ": " + error;
    }
    return line;
}

const VoiceImporter::Line* VoiceImporter::find(std::string_view stem) const
{
    const auto found = lines_.find(stem);
    return found == lines_.end() ? nullptr : &found->second;
}

void VoiceImporter::put(const std::string& stem, std::string_view label, std::optional<VoiceLine> voice)
{
    Line& line = lines_[stem];
    line.stem = stem;
    std::optional<VoiceLine> before = line.voice;
    const std::string errorBefore = line.error;
    history_.perform(label,
        [this, stem, voice] {
            lines_[stem].voice = voice;
            lines_[stem].error.clear();
        },
        [this, stem, before, errorBefore] {
            lines_[stem].voice = before;
            lines_[stem].error = errorBefore;
        });
}

bool VoiceImporter::setVoice(const std::string& stem, const VoiceLine& voice)
{
    if (stem.empty())
        return false;
    put(stem, "Import voice line", voice);
    return true;
}

bool VoiceImporter::match(const std::string& stem, std::string_view written)
{
    const Line* line = find(stem);
    if (!line || !line->voice || splitWords(written).empty())
        return false;
    // Only the words whose timing was heard: guessed ones are guessed again.
    std::vector<VoiceWord> heard;
    for (const VoiceWord& word : line->voice->words)
        if (word.matched)
            heard.push_back(word);
    VoiceLine voice = *line->voice;
    voice.words = matchWords(heard, written, voice.words.empty() ? 0.0 : voice.words.back().end);
    voice.text = std::string(written);
    if (voice.toJson() == line->voice->toJson())
        return false;
    put(stem, "Match voice to the line", std::move(voice));
    return true;
}

bool VoiceImporter::differs(const VoiceLine& voice, std::string_view written)
{
    std::string words;
    for (const VoiceWord& word : voice.words)
        words += word.text + " ";
    return normalized(written) != normalized(words);
}

std::vector<VoiceImporter::Problem> VoiceImporter::problems(std::string_view stem, std::string_view written) const
{
    std::vector<Problem> out;
    const Line* line = find(stem);
    if (!line)
        return out;
    if (!line->error.empty())
        out.push_back({line->error, true});
    if (!line->voice)
    {
        if (!line->audio.empty())
            out.push_back({line->audio + " isn't imported yet"});
        return out;
    }
    const VoiceLine& voice = *line->voice;
    if (line->audio.empty())
        out.push_back({voicePath(stem) + " has no recording beside it"});
    if (!splitWords(written).empty() && differs(voice, written))
        out.push_back({std::string(stem) + ": the line changed since its voice was matched"});
    if (const int low = voice.lowConfidence(settings_.flagBelow); low > 0)
        out.push_back({std::string(stem) + ": " + std::to_string(low) + (low == 1 ? " word" : " words") + " heard unsurely"});
    const auto guessed = std::count_if(voice.words.begin(), voice.words.end(), [](const VoiceWord& w) { return !w.matched; });
    if (guessed > 0)
        out.push_back({std::string(stem) + ": " + std::to_string(guessed) + (guessed == 1 ? " word" : " words") + " not heard as written, timed by guess"});
    return out;
}

std::vector<std::pair<std::string, std::string>> VoiceImporter::changed() const
{
    std::vector<std::pair<std::string, std::string>> out;
    for (const auto& [stem, line] : lines_)
        if (line.voice)
            if (std::string text = line.voice->toJson(); text != line.saved)
                out.push_back({voicePath(stem), std::move(text)});
    return out;
}

void VoiceImporter::markSaved(std::string_view path)
{
    for (auto& [stem, line] : lines_)
        if (line.voice && voicePath(stem) == path)
            line.saved = line.voice->toJson();
}

std::vector<std::string> VoiceImporter::vocabulary(const yh::FileSystem& files)
{
    std::vector<std::string> out;
    const std::optional<std::string> text = files.readText("voice/vocabulary.txt");
    if (!text)
        return out;
    std::string name;
    auto add = [&] {
        while (!name.empty() && (name.back() == ' ' || name.back() == '\r' || name.back() == '\t'))
            name.pop_back();
        const size_t first = name.find_first_not_of(" \t");
        if (first != std::string::npos && name[first] != '#')
            out.push_back(name.substr(first));
        name.clear();
    };
    for (const char c : *text)
        if (c == '\n')
            add();
        else
            name += c;
    add();
    return out;
}

// ---- VoicePanel ----

VoicePanel::VoicePanel() : slot_(std::make_shared<Slot>())
{
    slot_->loader = [](std::string* error) { return loadWhisper(findVoiceModel(), error); };
}

// A running import is waited for: the future std::async made blocks until its thread is done.
VoicePanel::~VoicePanel() = default;

void VoicePanel::setLoader(Loader loader)
{
    std::lock_guard lock(slot_->mutex);
    slot_->loader = std::move(loader);
    slot_->transcriber.reset();
}

void VoicePanel::reset()
{
    node_ = 0;
    nodeScroll_ = 0;
    wordScroll_ = 0;
    if (!busy())
        status_.clear();
}

bool VoicePanel::startImport(VoiceImporter& voices, const yh::FileSystem& files, const std::string& stem, const std::string& written)
{
    if (busy())
    {
        status_ = "Still listening to " + jobStem_;
        return false;
    }
    // A recording dropped in since the line was first read is looked for again.
    const VoiceImporter::Line* found = &voices.line(files, stem);
    if (found->audio.empty())
        found = &voices.line(files, stem, true);
    const VoiceImporter::Line& line = *found;
    if (line.audio.empty())
    {
        status_ = "There is no recording for " + stem + " yet";
        return false;
    }
    std::optional<std::vector<unsigned char>> bytes = files.read(line.audio);
    if (!bytes)
    {
        status_ = line.audio + " can't be read";
        return false;
    }
    const std::string audioName = line.audio.substr(line.audio.rfind('/') + 1);
    std::vector<std::string> vocabulary = VoiceImporter::vocabulary(files);
    std::shared_ptr<Slot> slot = slot_;
    job_ = std::async(std::launch::async, [slot, recording = std::move(*bytes), audioName, written, vocabulary = std::move(vocabulary)] {
        Result result;
        std::lock_guard lock(slot->mutex);
        if (!slot->transcriber && slot->loader)
            slot->transcriber = slot->loader(&result.error);
        if (!slot->transcriber)
        {
            if (result.error.empty())
                result.error = "there is nothing to listen with";
            return result;
        }
        result.line = transcribeVoiceLine(recording, audioName, written, vocabulary, *slot->transcriber, &result.error);
        return result;
    });
    jobStem_ = stem;
    status_ = "Listening to " + line.audio + "...";
    return true;
}

bool VoicePanel::finish(VoiceImporter& voices, bool wait)
{
    if (!busy())
        return false;
    if (!wait && job_.wait_for(std::chrono::seconds(0)) != std::future_status::ready)
        return false;
    Result result = job_.get();
    if (result.line)
    {
        voices.setVoice(jobStem_, *result.line);
        status_ = "Imported " + jobStem_ + ": " + std::to_string(result.line->words.size()) + " words";
    }
    else
        status_ = jobStem_ + " not imported: " + result.error;
    return true;
}

void VoicePanel::draw(VoiceImporter& voices, DialogueEditor& conversation, const yh::FileSystem& files, yh::Ui& ui, const yh::Input&,
    yh::Renderer& renderer, const yh::Rect& area)
{
    finish(voices);
    if (conversation.nodes().empty())
        return;
    node_ = std::min(node_, conversation.nodes().size() - 1);
    const float left = std::min(280.0f, area.w * 0.3f);
    drawNodes(voices, conversation, files, ui, {area.x, area.y, left, area.h});
    drawLine(voices, conversation, files, ui, renderer, {area.x + left, area.y, area.w - left, area.h});
}

void VoicePanel::drawNodes(VoiceImporter& voices, DialogueEditor& conversation, const yh::FileSystem& files, yh::Ui& ui, const yh::Rect& column)
{
    const float x = column.x + 8, w = column.w - 16, h = 28, row = h + 3;
    float y = column.y + 8;
    ui.label({x, y}, "Lines", ui.theme.accent);
    y += 24;
    const std::vector<DialogueEditor::Node>& nodes = conversation.nodes();
    const yh::Rect list{x, y, w, std::max(row, column.y + column.h - y - 8)};
    ui.beginScroll(list, static_cast<float>(nodes.size()) * row, nodeScroll_);
    float top = 0;
    for (size_t i = 0; i < nodes.size(); i++)
    {
        const std::string stem = VoiceImporter::stem(conversation.id(), nodes[i].id);
        const VoiceImporter::Line& line = voices.line(files, stem);
        // How far along it is, after the node's id.
        std::string mark = "  -";
        if (!line.error.empty())
            mark = "  broken";
        else if (line.voice)
            mark = voices.problems(stem, nodes[i].text).empty() ? "  ok" : "  check";
        else if (!line.audio.empty())
            mark = "  to import";
        if (ui.toggle({0, top, w, h}, fit(ui, nodes[i].id + mark, w - 16), i == node_))
        {
            node_ = i;
            wordScroll_ = 0;
        }
        top += row;
    }
    ui.endScroll();
}

void VoicePanel::drawLine(VoiceImporter& voices, DialogueEditor& conversation, const yh::FileSystem& files, yh::Ui& ui, yh::Renderer& renderer,
    const yh::Rect& column)
{
    renderer.fillRect(column, {26, 27, 34, 255});
    const float x = column.x + 12, w = column.w - 24, h = 28;
    float y = column.y + 10;
    const DialogueEditor::Node node = conversation.nodes()[node_];
    const std::string stem = VoiceImporter::stem(conversation.id(), node.id);
    const VoiceImporter::Line& line = voices.line(files, stem);

    ui.label({x, y}, node.speaker.empty() ? node.id : node.id + "  (" + node.speaker + ")", ui.theme.text);
    y += 24;
    auto say = [&](const std::string& text, yh::Color colour) {
        ui.label({x, y}, fit(ui, text, w), colour);
        y += 22;
    };
    if (node.text.empty())
        say("No line written yet: an import suggests one.", ui.theme.textDim);
    for (const std::string& part : wrap(ui, node.text, w))
        if (!part.empty())
            say(part, ui.theme.textDim);
    y += 6;
    if (line.audio.empty())
        ui.label({x, y}, fit(ui, "No recording. Put one at voice/" + stem + "." + voices.settings().extensions.front() + " in the package.", w), ui.theme.textDim);
    else
        ui.label({x, y}, fit(ui, "Recording: " + line.audio, w), ui.theme.text);
    y += 28;

    // The buttons, in one row.
    const float bw = std::min(150.0f, (w - 5 * 6) / 6);
    float bx = x;
    auto next = [&] {
        const yh::Rect r{bx, y, bw, h};
        bx += bw + 6;
        return r;
    };
    if (ui.button(next(), line.voice ? "Import again" : "Import", !line.audio.empty() && !busy() && voiceBuiltIn()))
        startImport(voices, files, stem, node.text);
    if (ui.button(next(), "Look again", !busy()))
        voices.line(files, stem, true);
    const bool written = !splitWords(node.text).empty();
    if (ui.button(next(), "Match to line", line.voice && written && VoiceImporter::differs(*line.voice, node.text)))
        voices.match(stem, node.text);
    if (ui.button(next(), "Use as line", line.voice && !written))
        conversation.setText(node_, line.voice->text);
    if (ui.button(next(), "Copy SRT", line.voice.has_value()))
        SDL_SetClipboardText(line.voice->toSrt().c_str());
    if (ui.button(next(), "Copy VTT", line.voice.has_value()))
        SDL_SetClipboardText(line.voice->toVtt().c_str());
    y += h + 8;

    if (!voiceBuiltIn())
        say("This build can't listen to recordings; use the desktop one.", ui.theme.textDim);
    if (!status_.empty())
        say(status_, busy() ? ui.theme.accent : ui.theme.textDim);
    for (const VoiceImporter::Problem& problem : voices.problems(stem, node.text))
        say(problem.text, problem.error ? ui.theme.bad : ui.theme.accent);
    if (!line.voice)
        return;
    const VoiceLine& voice = *line.voice;
    if (!written)
        say("Heard: " + voice.text, ui.theme.text);
    y += 6;

    // The words along the recording: green heard, red heard unsurely, gold timed by guess.
    const float flag = voices.settings().flagBelow;
    auto colour = [&](const VoiceWord& word) {
        return !word.matched ? ui.theme.accent : word.confidence < flag ? ui.theme.bad : ui.theme.good;
    };
    const double length = std::max(0.01, voice.words.empty() ? 0.0 : voice.words.back().end);
    const yh::Rect strip{x, y, w, 26};
    renderer.fillRect(strip, {18, 19, 24, 255});
    for (const VoiceWord& word : voice.words)
    {
        const float from = strip.x + static_cast<float>(word.start / length) * strip.w;
        const float to = strip.x + static_cast<float>(word.end / length) * strip.w;
        yh::Color c = colour(word);
        c.a = 150;
        renderer.fillRect({from + 1, strip.y + 3, std::max(2.0f, to - from - 2), strip.h - 6}, c);
    }
    y += strip.h + 4;
    ui.label({x, y}, voice.model + "  -  " + std::to_string(voice.words.size()) + " words, " + seconds(length) + " s", ui.theme.textDim);
    y += 26;

    // One row per word.
    const float columns[] = {x, x + w * 0.4f, x + w * 0.55f, x + w * 0.7f};
    ui.label({columns[0], y}, "Word", ui.theme.textDim);
    ui.label({columns[1], y}, "Start", ui.theme.textDim);
    ui.label({columns[2], y}, "End", ui.theme.textDim);
    ui.label({columns[3], y}, "Sure", ui.theme.textDim);
    y += 22;
    const float row = 22;
    const yh::Rect list{x, y, w, std::max(row, column.y + column.h - y - 8)};
    ui.beginScroll(list, static_cast<float>(voice.words.size()) * row, wordScroll_);
    float top = 0;
    for (const VoiceWord& word : voice.words)
    {
        const yh::Color c = colour(word);
        ui.label({0, top}, fit(ui, word.text, w * 0.38f), c);
        ui.label({columns[1] - x, top}, seconds(word.start), ui.theme.text);
        ui.label({columns[2] - x, top}, seconds(word.end), ui.theme.text);
        ui.label({columns[3] - x, top}, word.matched ? seconds(word.confidence) : std::string("guessed"), c);
        top += row;
    }
    ui.endScroll();
}
