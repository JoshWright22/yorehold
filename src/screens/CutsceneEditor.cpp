// Cutscene mode of the Create screen: the commands that change one cutscene file, where a chapter
// plays its cutscenes, and the desktop layout over them.

#include "CutsceneEditor.h"

#include "content/GameMap.h"

#include <yorehold/framework/animation/Tween.h>
#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/graphics/Font.h>

#include <nlohmann/json.hpp>
#include <SDL3/SDL_keycode.h>

#include <algorithm>
#include <charconv>
#include <cmath>
#include <iterator>
#include <memory>
#include <set>
#include <sstream>

namespace
{

// Ordered, so a hand-written file keeps its fields where the writer put them.
using nlohmann::ordered_json;

constexpr size_t maxLine = 2000, maxName = 64;
constexpr double maxSeconds = 600;
constexpr float barsPerSecond = 2.5f; // as yh::Cutscene slides its bars

// The fields of `entry` that aren't in `known`, as a JSON object; empty if there are none.
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
    // Kept in a local: items() of a temporary would outlive it.
    const ordered_json rest = ordered_json::parse(extra);
    for (const auto& [key, value] : rest.items())
        entry[key] = value;
}

// Three decimals at most, and whole numbers without ".0", so a float read as 1.3 isn't written
// back as 1.2999999523.
ordered_json number(double value)
{
    const double rounded = std::round(value * 1000) / 1000;
    if (rounded == std::floor(rounded) && std::abs(rounded) < 1e9)
        return static_cast<long long>(rounded);
    return rounded;
}

ordered_json stepJson(const CutsceneEditor::Step& step)
{
    using Kind = CutsceneEditor::Kind;
    ordered_json s;
    switch (step.kind)
    {
    case Kind::Pause:
        s["pause"] = number(step.seconds);
        break;
    case Kind::Camera:
        s["camera"] = {number(step.camera.x), number(step.camera.y)};
        if (step.zoom > 0)
            s["zoom"] = number(step.zoom);
        break;
    case Kind::Caption:
        s["caption"] = step.text;
        break;
    case Kind::Title:
        s["title"] = step.text;
        break;
    case Kind::Fade:
        s["fade"] = {step.color.r, step.color.g, step.color.b, step.color.a};
        break;
    case Kind::Bars:
        s["bars"] = step.on;
        break;
    case Kind::Event:
        s["event"] = step.text;
        break;
    }
    // A caption without seconds shows for 3, so its time is always written.
    if (step.kind == Kind::Caption || step.kind == Kind::Title || (step.kind != Kind::Pause && step.seconds > 0))
        s["seconds"] = number(step.seconds);
    if (!step.ease.empty())
        s["ease"] = step.ease;
    if (!step.wait)
        s["wait"] = false;
    addExtra(s, step.extra);
    return s;
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

yh::Color mix(yh::Color a, yh::Color b, float t)
{
    auto channel = [t](uint8_t x, uint8_t y) { return static_cast<uint8_t>(std::lround(x + (y - x) * t)); };
    return {channel(a.r, b.r), channel(a.g, b.g), channel(a.b, b.b), channel(a.a, b.a)};
}

yh::Ease curveOf(const CutsceneEditor::Step& step)
{
    return step.ease.empty() ? yh::Ease::InOutCubic : yh::easeFromName(step.ease);
}

}

// ---------------------------------------------------------------- the cutscene and its commands

bool CutsceneEditor::load(std::string_view text, std::string* error)
{
    if (error)
        error->clear();
    // The game's own reader first, so nothing it refuses opens here.
    if (!yh::Cutscene::fromJson(text, error))
        return false;
    try
    {
        const ordered_json j = ordered_json::parse(text);
        State state;
        state.extra = extraOf(j, {"steps"});
        for (const ordered_json& s : j.at("steps"))
        {
            Step step;
            step.wait = s.value("wait", true);
            step.ease = s.value("ease", std::string());
            step.seconds = s.value("seconds", 0.0);
            std::vector<std::string_view> known;
            if (s.contains("camera"))
            {
                const auto at = s.at("camera").get<std::vector<float>>();
                step.kind = Kind::Camera;
                step.camera = {at[0], at[1]};
                step.zoom = s.value("zoom", 0.0f);
                known = {"camera", "zoom", "seconds", "ease", "wait"};
            }
            else if (s.contains("title") || s.contains("caption"))
            {
                const bool title = s.contains("title");
                step.kind = title ? Kind::Title : Kind::Caption;
                step.text = s.at(title ? "title" : "caption").get<std::string>();
                step.seconds = s.value("seconds", 3.0);
                known = {title ? "title" : "caption", "seconds", "ease", "wait"};
            }
            else if (s.contains("fade"))
            {
                const auto c = s.at("fade").get<std::vector<int>>();
                step.kind = Kind::Fade;
                step.color = {static_cast<uint8_t>(c[0]), static_cast<uint8_t>(c[1]), static_cast<uint8_t>(c[2]), static_cast<uint8_t>(c[3])};
                known = {"fade", "seconds", "ease", "wait"};
            }
            else if (s.contains("bars"))
            {
                step.kind = Kind::Bars;
                step.on = s.at("bars").get<bool>();
                known = {"bars", "seconds", "ease", "wait"};
            }
            else if (s.contains("event"))
            {
                step.kind = Kind::Event;
                step.text = s.at("event").get<std::string>();
                known = {"event", "seconds", "ease", "wait"};
            }
            else
            {
                step.kind = Kind::Pause;
                step.seconds = s.at("pause").get<double>();
                // The game reads a pause's time from "pause" only.
                known = {"pause", "seconds", "ease", "wait"};
            }
            step.extra = extraOf(s, known);
            state.steps.push_back(std::move(step));
        }
        state_ = std::move(state);
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

void CutsceneEditor::create()
{
    state_ = {};
    Step bars;
    bars.kind = Kind::Bars;
    bars.seconds = 0;
    state_.steps.push_back(bars);
    Step caption;
    caption.kind = Kind::Caption;
    caption.seconds = 3;
    state_.steps.push_back(caption);
    bars.on = false;
    state_.steps.push_back(bars);
    loaded_ = true;
}

std::string CutsceneEditor::toJson() const
{
    if (!loaded_)
        return "{}";
    ordered_json j = ordered_json::object();
    addExtra(j, state_.extra);
    ordered_json steps = ordered_json::array();
    for (const Step& step : state_.steps)
        steps.push_back(stepJson(step));
    j["steps"] = std::move(steps);
    return j.dump(2);
}

std::optional<yh::Cutscene> CutsceneEditor::cutscene(std::string* error) const
{
    if (!loaded_)
    {
        if (error)
            *error = "nothing is open";
        return std::nullopt;
    }
    return yh::Cutscene::fromJson(toJson(), error);
}

std::vector<CutsceneEditor::Span> CutsceneEditor::spans() const
{
    // The way yh::Cutscene::update runs them: the next step starts once nothing is running, or
    // straight away while the last one started doesn't make it wait.
    const std::vector<Step>& steps = state_.steps;
    std::vector<Span> out(steps.size());
    std::vector<size_t> running;
    size_t next = 0;
    double now = 0;
    while (true)
    {
        while (next < steps.size() && (running.empty() || !steps[running.back()].wait))
        {
            out[next] = {now, now + std::max(0.0, steps[next].seconds)};
            running.push_back(next++);
        }
        if (running.empty())
            break;
        double soonest = out[running.front()].end;
        for (const size_t r : running)
            soonest = std::min(soonest, out[r].end);
        now = soonest;
        std::erase_if(running, [&](size_t r) { return out[r].end <= now; });
    }
    return out;
}

double CutsceneEditor::length() const
{
    double end = 0;
    for (const Span& span : spans())
        end = std::max(end, span.end);
    return end;
}

CutsceneEditor::Frame CutsceneEditor::frameAt(double seconds, yh::Vec2 camera, float zoom) const
{
    const std::vector<Step>& steps = state_.steps;
    const std::vector<Span> times = spans();
    auto progress = [&](size_t i, double at) {
        return times[i].end > times[i].start ? static_cast<float>(std::clamp((at - times[i].start) / (times[i].end - times[i].start), 0.0, 1.0)) : 1.0f;
    };

    // Each camera move and fade starts from wherever the earlier ones had got to when it began.
    std::vector<yh::Vec2> fromCamera(steps.size());
    std::vector<float> fromZoom(steps.size());
    std::vector<yh::Color> fromFade(steps.size());
    auto viewAt = [&](double at, size_t before, yh::Vec2& position, float& z) {
        position = camera;
        z = zoom;
        for (size_t i = 0; i < before; i++)
            if (steps[i].kind == Kind::Camera && times[i].start <= at)
            {
                const float e = yh::ease(curveOf(steps[i]), progress(i, at));
                position = fromCamera[i] + (steps[i].camera - fromCamera[i]) * e;
                z = steps[i].zoom > 0 ? fromZoom[i] + (steps[i].zoom - fromZoom[i]) * e : fromZoom[i];
            }
    };
    auto fadeAt = [&](double at, size_t before) {
        yh::Color color{0, 0, 0, 0};
        for (size_t i = 0; i < before; i++)
            if (steps[i].kind == Kind::Fade && times[i].start <= at)
                color = mix(fromFade[i], steps[i].color, progress(i, at));
        return color;
    };
    for (size_t i = 0; i < steps.size(); i++)
    {
        viewAt(times[i].start, i, fromCamera[i], fromZoom[i]);
        fromFade[i] = fadeAt(times[i].start, i);
    }

    Frame frame;
    viewAt(seconds, steps.size(), frame.camera, frame.zoom);
    frame.fade = fadeAt(seconds, steps.size());

    // The bars slide toward the last setting at a fixed speed.
    double last = 0;
    bool on = false;
    for (size_t i = 0; i < steps.size(); i++)
        if (steps[i].kind == Kind::Bars && times[i].start <= seconds)
        {
            const float move = barsPerSecond * static_cast<float>(times[i].start - last);
            frame.bars = std::clamp(frame.bars + (on ? move : -move), 0.0f, 1.0f);
            last = times[i].start;
            on = steps[i].on;
        }
    const float move = barsPerSecond * static_cast<float>(seconds - last);
    frame.bars = std::clamp(frame.bars + (on ? move : -move), 0.0f, 1.0f);

    // Captions and titles fade in and out over a third of their time, 0.6 s at most each way.
    for (size_t i = 0; i < steps.size(); i++)
    {
        if ((steps[i].kind != Kind::Caption && steps[i].kind != Kind::Title) || seconds < times[i].start || seconds >= times[i].end)
            continue;
        const double elapsed = seconds - times[i].start, total = steps[i].seconds;
        const double edge = std::min(0.6, total / 3);
        const double alpha = edge <= 0 ? 1 : std::clamp(std::min(elapsed, total - elapsed) / edge, 0.0, 1.0);
        frame.lines.push_back({steps[i].text, steps[i].kind == Kind::Title, static_cast<float>(alpha)});
    }
    return frame;
}

template <typename Change>
void CutsceneEditor::edit(std::string_view label, Change change, std::string_view mergeKey)
{
    const auto before = std::make_shared<const State>(state_);
    change();
    const auto after = std::make_shared<const State>(state_);
    history_.record(label, [this, after] { state_ = *after; }, [this, before] { state_ = *before; }, mergeKey);
}

std::optional<size_t> CutsceneEditor::addStep(Kind kind, std::optional<size_t> after)
{
    if (!loaded_ || state_.steps.size() >= 1000 || (after && *after >= state_.steps.size()))
        return std::nullopt;
    const size_t at = after ? *after + 1 : state_.steps.size();
    Step step;
    step.kind = kind;
    switch (kind)
    {
    case Kind::Pause:
        step.seconds = 1;
        break;
    case Kind::Camera:
    {
        // From where the camera was last sent, or the middle of the map.
        step.seconds = 2;
        step.camera = bounds_ ? yh::Vec2{bounds_->x + bounds_->w / 2, bounds_->y + bounds_->h / 2} : yh::Vec2{};
        for (size_t i = at; i-- > 0;)
            if (state_.steps[i].kind == Kind::Camera)
            {
                step.camera = state_.steps[i].camera;
                break;
            }
        break;
    }
    case Kind::Caption:
    case Kind::Title:
        step.seconds = 3;
        break;
    case Kind::Fade:
        step.seconds = 1;
        break;
    case Kind::Bars:
    {
        // The other way from how the bars stand by then.
        step.seconds = 0;
        for (size_t i = at; i-- > 0;)
            if (state_.steps[i].kind == Kind::Bars)
            {
                step.on = !state_.steps[i].on;
                break;
            }
        break;
    }
    case Kind::Event:
        step.seconds = 0;
        step.text = std::string(events().front());
        break;
    }
    edit("Add " + std::string(kindName(kind)) + " step", [&] {
        state_.steps.insert(state_.steps.begin() + static_cast<std::ptrdiff_t>(at), std::move(step));
    });
    return at;
}

bool CutsceneEditor::removeStep(size_t step)
{
    if (!loaded_ || step >= state_.steps.size())
        return false;
    edit("Remove " + std::string(kindName(state_.steps[step].kind)) + " step",
        [&] { state_.steps.erase(state_.steps.begin() + static_cast<std::ptrdiff_t>(step)); });
    return true;
}

bool CutsceneEditor::moveStep(size_t step, int by)
{
    if (!loaded_ || step >= state_.steps.size() || (by != -1 && by != 1))
        return false;
    const size_t to = step + static_cast<size_t>(by);
    if (by < 0 ? step == 0 : to >= state_.steps.size())
        return false;
    edit("Move step", [&] { std::swap(state_.steps[step], state_.steps[to]); });
    return true;
}

std::optional<size_t> CutsceneEditor::copyStep(size_t step)
{
    if (!loaded_ || step >= state_.steps.size() || state_.steps.size() >= 1000)
        return std::nullopt;
    edit("Copy step", [&] {
        const Step copy = state_.steps[step];
        state_.steps.insert(state_.steps.begin() + static_cast<std::ptrdiff_t>(step) + 1, copy);
    });
    return step + 1;
}

bool CutsceneEditor::setStep(size_t step, const Step& changed, std::string_view field)
{
    if (!loaded_ || step >= state_.steps.size())
        return false;
    const std::vector<std::string_view> names = eases();
    if (!changed.ease.empty() && changed.ease != state_.steps[step].ease && std::find(names.begin(), names.end(), changed.ease) == names.end())
        return false;
    if (changed.kind == Kind::Caption || changed.kind == Kind::Title ? changed.text.size() > maxLine : changed.text.size() > maxName)
        return false;
    if (!std::isfinite(changed.camera.x) || !std::isfinite(changed.camera.y))
        return false;
    // The game's reader decides the rest (times, zoom, a blank event).
    ordered_json one{{"steps", ordered_json::array({stepJson(changed)})}};
    if (!yh::Cutscene::fromJson(one.dump()))
        return false;
    if (stepJson(changed) == stepJson(state_.steps[step]))
        return false;
    edit("Change " + std::string(kindName(changed.kind)) + " step", [&] { state_.steps[step] = changed; },
        "cutscene-step-" + std::to_string(step) + "-" + std::string(field));
    return true;
}

std::vector<CutsceneEditor::Problem> CutsceneEditor::problems() const
{
    std::vector<Problem> out;
    if (!loaded_)
        return out;
    std::string why;
    if (!cutscene(&why))
        out.push_back({"the game would refuse it: " + why});
    if (state_.steps.empty())
        out.push_back({"it has no steps, so it ends as soon as it starts", false});

    const std::vector<std::string_view> names = eases(), known = events();
    for (size_t i = 0; i < state_.steps.size(); i++)
    {
        const Step& step = state_.steps[i];
        const std::string where = "step " + std::to_string(i + 1);
        if ((step.kind == Kind::Caption || step.kind == Kind::Title) && step.text.empty())
            out.push_back({where + ": the " + std::string(kindName(step.kind)) + " has no line", false});
        if ((step.kind == Kind::Caption || step.kind == Kind::Title) && step.seconds <= 0)
            out.push_back({where + ": the " + std::string(kindName(step.kind)) + " shows for no time", false});
        if (!step.ease.empty() && std::find(names.begin(), names.end(), step.ease) == names.end())
            out.push_back({where + ": the game doesn't know the ease " + step.ease + " and moves at an even speed", false});
        if (step.kind == Kind::Camera && bounds_
            && (step.camera.x < bounds_->x || step.camera.y < bounds_->y || step.camera.x > bounds_->x + bounds_->w || step.camera.y > bounds_->y + bounds_->h))
            out.push_back({where + ": the camera aims outside the map, the game stops it at the edge", false});
        if (step.kind == Kind::Event && std::find(known.begin(), known.end(), step.text) == known.end())
            out.push_back({where + ": the game does nothing with the event " + step.text, false});
    }
    return out;
}

std::string_view CutsceneEditor::kindName(Kind kind)
{
    switch (kind)
    {
    case Kind::Pause: return "pause";
    case Kind::Camera: return "camera";
    case Kind::Caption: return "caption";
    case Kind::Title: return "title";
    case Kind::Fade: return "fade";
    case Kind::Bars: return "bars";
    case Kind::Event: return "event";
    }
    return "step";
}

std::vector<std::string_view> CutsceneEditor::eases()
{
    return {"linear", "inQuad", "outQuad", "inOutQuad", "outCubic", "inOutCubic", "outBack", "outElastic", "outBounce"};
}

std::vector<std::string_view> CutsceneEditor::events()
{
    return {"finished"};
}

// ---------------------------------------------------------------- where a chapter plays them

std::optional<CutsceneHooks::State> CutsceneHooks::read(std::string_view chapterJson, std::string* error)
{
    try
    {
        const ordered_json j = ordered_json::parse(chapterJson);
        if (!j.is_object())
            throw std::invalid_argument("a chapter is a JSON object");
        State state;
        const ordered_json triggers = j.value("triggers", ordered_json::array());
        if (!triggers.is_array())
            throw std::invalid_argument("triggers must be an array");
        for (const ordered_json& t : triggers)
        {
            Trigger trigger;
            trigger.id = t.at("id").get<std::string>();
            trigger.when = t.value("when", std::vector<std::string>{});
            trigger.dialogue = t.value("dialogue", std::string());
            trigger.cutscene = t.value("cutscene", std::string());
            trigger.extra = extraOf(t, {"id", "when", "dialogue", "cutscene"});
            state.triggers.push_back(std::move(trigger));
        }
        auto inside = [&](const char* object, const char* key) {
            if (!j.contains(object) || !j.at(object).is_object())
                return std::string();
            return j.at(object).value(key, std::string());
        };
        state.cleared = inside("endings", "cleared");
        state.wipe = inside("onWipe", "cutscene");
        state.hasWin = j.contains("winCondition") && j.at("winCondition").is_object();
        state.win = inside("winCondition", "cutscene");
        return state;
    }
    catch (const std::exception& e)
    {
        if (error)
            *error = e.what();
        return std::nullopt;
    }
}

bool CutsceneHooks::load(std::string_view chapterJson, const std::string& folder, Exists exists, std::string* error)
{
    if (error)
        error->clear();
    std::optional<State> state = read(chapterJson, error);
    if (!state)
        return false;
    state_ = saved_ = std::move(*state);
    folder_ = folder;
    exists_ = std::move(exists);
    loaded_ = true;
    return true;
}

std::string CutsceneHooks::applyTo(std::string_view chapterJson) const
{
    const std::optional<State> there = read(chapterJson, nullptr);
    if (!loaded_ || !there || *there == state_)
        return std::string(chapterJson);
    ordered_json j = ordered_json::parse(chapterJson);
    if (there->triggers != state_.triggers)
    {
        ordered_json triggers = ordered_json::array();
        for (const Trigger& trigger : state_.triggers)
        {
            ordered_json t;
            t["id"] = trigger.id;
            if (!trigger.when.empty())
                t["when"] = trigger.when;
            if (!trigger.dialogue.empty())
                t["dialogue"] = trigger.dialogue;
            if (!trigger.cutscene.empty())
                t["cutscene"] = trigger.cutscene;
            addExtra(t, trigger.extra);
            triggers.push_back(std::move(t));
        }
        if (triggers.empty())
            j.erase("triggers");
        else
            j["triggers"] = std::move(triggers);
    }
    // One key inside an object: set, or taken out along with an object left empty.
    auto put = [&](const char* object, const char* key, const std::string& was, const std::string& now) {
        if (was == now)
            return;
        if (!now.empty())
        {
            if (!j.contains(object) || !j[object].is_object())
                j[object] = ordered_json::object();
            j[object][key] = now;
        }
        else if (j.contains(object) && j[object].is_object())
        {
            j[object].erase(key);
            if (j[object].empty())
                j.erase(object);
        }
    };
    put("endings", "cleared", there->cleared, state_.cleared);
    put("onWipe", "cutscene", there->wipe, state_.wipe);
    if (there->hasWin)
        put("winCondition", "cutscene", there->win, state_.win);
    return j.dump(2);
}

bool CutsceneHooks::changed() const
{
    return loaded_ && !(state_ == saved_);
}

void CutsceneHooks::markSaved()
{
    saved_ = state_;
}

std::string CutsceneHooks::resolve(const std::string& named) const
{
    if (named.empty() || yh::FileSystem::normalize(named) != named)
        return named;
    const std::string local = folder_ + "/" + named;
    return exists_ && exists_(local) ? local : named;
}

std::string CutsceneHooks::nameFor(const std::string& path) const
{
    return path.starts_with(folder_ + "/") ? path.substr(folder_.size() + 1) : path;
}

std::vector<std::string> CutsceneHooks::named() const
{
    std::vector<std::string> out;
    auto add = [&](const std::string& name) {
        if (const std::string path = resolve(name); !name.empty() && std::find(out.begin(), out.end(), path) == out.end())
            out.push_back(path);
    };
    add(state_.cleared);
    add(state_.wipe);
    add(state_.win);
    for (const Trigger& trigger : state_.triggers)
        add(trigger.cutscene);
    return out;
}

template <typename Change>
void CutsceneHooks::edit(std::string_view label, Change change, std::string_view mergeKey)
{
    const auto before = std::make_shared<const State>(state_);
    change();
    const auto after = std::make_shared<const State>(state_);
    history_.record(label, [this, after] { state_ = *after; }, [this, before] { state_ = *before; }, mergeKey);
}

std::optional<size_t> CutsceneHooks::addTrigger(const std::string& path, const std::vector<std::string>& when)
{
    if (!loaded_ || path.empty() || !validFlags(when) || state_.triggers.size() >= 1000)
        return std::nullopt;
    std::string id;
    for (size_t n = 1; id.empty(); n++)
        if (const std::string free = "cutscene-" + std::to_string(n);
            std::none_of(state_.triggers.begin(), state_.triggers.end(), [&](const Trigger& t) { return t.id == free; }))
            id = free;
    edit("Add trigger " + id, [&] {
        Trigger trigger;
        trigger.id = id;
        trigger.when = when;
        trigger.cutscene = nameFor(path);
        state_.triggers.push_back(std::move(trigger));
    });
    return state_.triggers.size() - 1;
}

bool CutsceneHooks::removeTrigger(size_t trigger)
{
    if (!loaded_ || trigger >= state_.triggers.size())
        return false;
    edit("Remove trigger " + state_.triggers[trigger].id, [&] {
        if (!state_.triggers[trigger].dialogue.empty())
            state_.triggers[trigger].cutscene.clear();
        else
            state_.triggers.erase(state_.triggers.begin() + static_cast<std::ptrdiff_t>(trigger));
    });
    return true;
}

bool CutsceneHooks::setTriggerId(size_t trigger, const std::string& id)
{
    if (!loaded_ || trigger >= state_.triggers.size() || !validId(id)
        || std::any_of(state_.triggers.begin(), state_.triggers.end(), [&](const Trigger& t) { return t.id == id; }))
        return false;
    edit("Rename trigger", [&] { state_.triggers[trigger].id = id; }, "trigger-id-" + std::to_string(trigger));
    return true;
}

bool CutsceneHooks::setTriggerWhen(size_t trigger, const std::vector<std::string>& when)
{
    if (!loaded_ || trigger >= state_.triggers.size() || !validFlags(when) || state_.triggers[trigger].when == when)
        return false;
    edit("Change trigger flags", [&] { state_.triggers[trigger].when = when; }, "trigger-when-" + std::to_string(trigger));
    return true;
}

bool CutsceneHooks::setCleared(const std::string& path)
{
    const std::string name = path.empty() ? std::string() : nameFor(path);
    if (!loaded_ || state_.cleared == name)
        return false;
    edit(name.empty() ? "No cutscene when cleared" : "Play " + name + " when cleared", [&] { state_.cleared = name; });
    return true;
}

bool CutsceneHooks::setWipe(const std::string& path)
{
    const std::string name = path.empty() ? std::string() : nameFor(path);
    if (!loaded_ || state_.wipe == name)
        return false;
    edit(name.empty() ? "No cutscene on a wipe" : "Play " + name + " on a wipe", [&] { state_.wipe = name; });
    return true;
}

bool CutsceneHooks::setWin(const std::string& path)
{
    const std::string name = path.empty() ? std::string() : nameFor(path);
    if (!loaded_ || !state_.hasWin || state_.win == name)
        return false;
    edit(name.empty() ? "No cutscene on a win" : "Play " + name + " on a win", [&] { state_.win = name; });
    return true;
}

std::vector<CutsceneHooks::Problem> CutsceneHooks::problems() const
{
    std::vector<Problem> out;
    if (!loaded_)
        return out;
    std::set<std::string> ids;
    for (const Trigger& trigger : state_.triggers)
    {
        if (!validId(trigger.id))
            out.push_back({"trigger " + trigger.id + ": ids use a-z, 0-9, - and _"});
        else if (!ids.insert(trigger.id).second)
            out.push_back({"two triggers are called " + trigger.id + ", so only one of them is remembered as fired", false});
        if (trigger.dialogue.empty() && trigger.cutscene.empty())
            out.push_back({"trigger " + trigger.id + " plays nothing"});
        if (!validFlags(trigger.when))
            out.push_back({"trigger " + trigger.id + ": flags are 1 to 64 characters, each once"});
    }
    // A path the game can't find stops the chapter loading.
    auto missing = [&](const std::string& name, const std::string& what) {
        if (name.empty())
            return;
        if (yh::FileSystem::normalize(name) != name)
            out.push_back({what + ": " + name + " has to be a path inside the package"});
        else if (exists_ && !exists_(resolve(name)))
            out.push_back({what + ": there is no " + name});
    };
    missing(state_.cleared, "endings.cleared");
    missing(state_.wipe, "onWipe");
    missing(state_.win, "winCondition");
    for (const Trigger& trigger : state_.triggers)
        missing(trigger.cutscene, "trigger " + trigger.id);
    return out;
}

// ---------------------------------------------------------------- the desktop layout

namespace
{

constexpr float stepColumn = 250, fieldColumn = 300;

const char* const textBoxes[] = {"cut-seconds", "cut-x", "cut-y", "cut-zoom", "cut-line", "cut-r", "cut-g", "cut-b", "cut-a", "cut-event",
    "cut-trigger-id", "cut-when"};

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

std::optional<double> numberFrom(const std::string& text)
{
    double value = 0;
    const auto [end, problem] = std::from_chars(text.data(), text.data() + text.size(), value);
    return problem == std::errc() && end == text.data() + text.size() && !text.empty() && std::isfinite(value) ? std::optional<double>(value) : std::nullopt;
}

// 1.5 not 1.500000, and 2 not 2.0.
std::string shown(double value)
{
    std::ostringstream out;
    out << std::round(value * 1000) / 1000;
    return out.str();
}

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

yh::Color colorOf(CutsceneEditor::Kind kind)
{
    using Kind = CutsceneEditor::Kind;
    switch (kind)
    {
    case Kind::Pause: return {90, 92, 104, 255};
    case Kind::Camera: return {70, 130, 190, 255};
    case Kind::Caption: return {190, 160, 90, 255};
    case Kind::Title: return {210, 120, 70, 255};
    case Kind::Fade: return {120, 90, 160, 255};
    case Kind::Bars: return {60, 60, 66, 255};
    case Kind::Event: return {90, 170, 110, 255};
    }
    return {120, 120, 120, 255};
}

// What the list says about a step.
std::string summary(const CutsceneEditor::Step& step)
{
    using Kind = CutsceneEditor::Kind;
    switch (step.kind)
    {
    case Kind::Pause: return "pause " + shown(step.seconds) + " s";
    case Kind::Camera: return "camera " + shown(step.camera.x) + ", " + shown(step.camera.y) + (step.zoom > 0 ? " x" + shown(step.zoom) : std::string());
    case Kind::Caption: return step.text.empty() ? std::string("caption (no line)") : "\"" + step.text + "\"";
    case Kind::Title: return step.text.empty() ? std::string("title (no line)") : "title \"" + step.text + "\"";
    case Kind::Fade: return step.color.a == 0 ? std::string("fade in") : "fade to " + std::to_string(step.color.r) + " " + std::to_string(step.color.g) + " " + std::to_string(step.color.b);
    case Kind::Bars: return step.on ? std::string("bars in") : std::string("bars out");
    case Kind::Event: return "event " + step.text;
    }
    return {};
}

constexpr CutsceneEditor::Kind allKinds[] = {CutsceneEditor::Kind::Camera, CutsceneEditor::Kind::Caption, CutsceneEditor::Kind::Title,
    CutsceneEditor::Kind::Pause, CutsceneEditor::Kind::Fade, CutsceneEditor::Kind::Bars, CutsceneEditor::Kind::Event};

}

bool CutscenePanel::typing(const yh::Ui& ui)
{
    return std::any_of(std::begin(textBoxes), std::end(textBoxes), [&](const char* id) { return ui.editing(id); });
}

void CutscenePanel::update(const CutsceneEditor& editor, double deltaSeconds)
{
    if (!playing_)
        return;
    time_ += deltaSeconds;
    if (time_ >= editor.length())
    {
        time_ = editor.length();
        playing_ = false;
    }
}

void CutscenePanel::draw(CutsceneEditor& editor, CutsceneHooks* hooks, const std::string& path, GameMap* map, yh::Ui& ui, const yh::Input& input,
    yh::Renderer& renderer, const yh::Rect& area)
{
    if (!editor.loaded())
        return;
    // The preview's view starts where the party does, or in the middle of the map.
    if (!started_)
    {
        started_ = true;
        if (map)
        {
            const yh::Rect bounds = map->map().worldBounds();
            start_ = {bounds.x + bounds.w / 2, bounds.y + bounds.h / 2};
        }
    }
    // An undo can take away the step that was picked.
    if (step_ >= editor.steps().size())
        step_ = editor.steps().empty() ? 0 : editor.steps().size() - 1;
    time_ = std::clamp(time_, 0.0, editor.length());

    const bool nowTyping = typing(ui);
    if (wasTyping_ && !nowTyping)
    {
        editor.endTyping();
        if (hooks)
            hooks->endTyping();
    }
    wasTyping_ = nowTyping;
    if (!nowTyping && input.keyPressed(SDLK_SPACE))
    {
        if (!playing_ && time_ >= editor.length())
            time_ = 0;
        playing_ = !playing_;
    }
    if (!nowTyping && input.keyPressed(SDLK_DELETE) && !editor.steps().empty())
        editor.removeStep(step_);

    const yh::Rect left{area.x, area.y, stepColumn, area.h};
    const yh::Rect right{area.x + area.w - fieldColumn, area.y, fieldColumn, area.h};
    const yh::Rect middle{left.x + left.w, area.y, area.w - left.w - right.w, area.h};
    ui.panel(left);
    drawSteps(editor, ui, left);

    renderer.fillRect(middle, {26, 27, 34, 255});
    const float pad = 12;
    float previewW = middle.w - 2 * pad;
    float previewH = previewW * screenHeight / screenWidth;
    const float most = middle.h * 0.5f;
    if (previewH > most)
    {
        previewH = most;
        previewW = previewH * screenWidth / screenHeight;
    }
    const yh::Rect preview{middle.x + (middle.w - previewW) / 2, middle.y + 40, previewW, previewH};
    drawPreview(editor, map, ui, input, renderer, preview);
    const yh::Rect timeline{middle.x + pad, preview.y + preview.h + 46, middle.w - 2 * pad, 64};
    drawTimeline(editor, ui, input, renderer, timeline);
    if (hooks)
        drawHooks(*hooks, path, ui, {middle.x + pad, timeline.y + timeline.h + 12, middle.w - 2 * pad, middle.y + middle.h - timeline.y - timeline.h - 16});

    ui.panel(right);
    drawStep(editor, ui, right);
    if (!hint_.empty())
        ui.label({right.x + 8, right.y + right.h - 26}, fit(ui, hint_, right.w - 16), ui.theme.bad);
}

void CutscenePanel::drawSteps(CutsceneEditor& editor, yh::Ui& ui, const yh::Rect& column)
{
    const float x = column.x + 8, w = column.w - 16, h = 26;
    float y = column.y + 8;
    const std::vector<CutsceneEditor::Step>& steps = editor.steps();
    const std::vector<CutsceneEditor::Span> spans = editor.spans();
    ui.label({x, y}, "Steps", ui.theme.accent);
    ui.label({x + 60, y}, std::to_string(steps.size()) + (steps.size() == 1 ? " step, " : " steps, ") + shown(editor.length()) + " s", ui.theme.textDim);
    y += 26;

    const float row = h + 3;
    const float buttons = 4 * (h + 4) + 26;
    const yh::Rect list{x, y, w, std::max(row, column.y + column.h - buttons - y - 8)};
    ui.beginScroll(list, static_cast<float>(steps.size()) * row, scroll_);
    float top = 0;
    for (size_t i = 0; i < steps.size(); i++)
    {
        // Steps that start with the one before are pulled in, so it reads as "at the same time".
        const bool together = i > 0 && !steps[i - 1].wait;
        const float indent = together ? 14.0f : 0.0f;
        if (ui.toggle({indent, top, w - indent, h}, fit(ui, shown(spans[i].start) + "  " + summary(steps[i]), w - indent - 16), i == step_))
        {
            step_ = i;
            time_ = spans[i].start;
            playing_ = false;
            hint_.clear();
        }
        top += row;
    }
    ui.endScroll();

    y = list.y + list.h + 8;
    ui.label({x, y}, "Add after the picked one", ui.theme.textDim);
    y += 22;
    const std::optional<size_t> after = steps.empty() ? std::nullopt : std::optional<size_t>(step_);
    const float quarter = (w - 12) / 4;
    for (size_t k = 0; k < std::size(allKinds); k++)
    {
        const float bx = x + static_cast<float>(k % 4) * (quarter + 4), by = y + static_cast<float>(k / 4) * (h + 4);
        std::string name(CutsceneEditor::kindName(allKinds[k]));
        name[0] = static_cast<char>(name[0] - 'a' + 'A');
        if (ui.button({bx, by, quarter, h}, name))
            if (const std::optional<size_t> added = editor.addStep(allKinds[k], after))
            {
                step_ = *added;
                time_ = editor.spans()[step_].start;
                hint_.clear();
            }
    }
    y += 2 * (h + 4) + 4;
    const bool any = !steps.empty();
    if (ui.button({x, y, quarter, h}, "Up", any && step_ > 0) && editor.moveStep(step_, -1))
        step_--;
    if (ui.button({x + quarter + 4, y, quarter, h}, "Down", any && step_ + 1 < steps.size()) && editor.moveStep(step_, 1))
        step_++;
    if (ui.button({x + 2 * (quarter + 4), y, quarter, h}, "Copy", any))
        if (const std::optional<size_t> copied = editor.copyStep(step_))
            step_ = *copied;
    if (ui.button({x + 3 * (quarter + 4), y, quarter, h}, "Remove", any))
        editor.removeStep(step_);
}

void CutscenePanel::drawPreview(CutsceneEditor& editor, GameMap* map, yh::Ui& ui, const yh::Input& input, yh::Renderer& renderer, const yh::Rect& box)
{
    const CutsceneEditor::Frame frame = editor.frameAt(time_, start_, startZoom_);
    const float px = box.w / screenWidth; // one game screen pixel in the preview

    ui.label({box.x, box.y - 30}, overview_ ? "Whole map, the box is what the camera sees" : "What the players see", ui.theme.textDim);
    if (ui.toggle({box.x + box.w - 120, box.y - 34, 120, 28}, "Whole map", overview_))
        overview_ = !overview_;

    // Which part of the world fills the box: the camera's view, or the whole map.
    const yh::Rect bounds = map ? map->map().worldBounds() : yh::Rect{0, 0, screenWidth, screenHeight};
    float scale = box.w * frame.zoom / screenWidth;
    yh::Vec2 corner = frame.camera - yh::Vec2{box.w / 2, box.h / 2} / scale;
    if (overview_)
    {
        scale = std::min(box.w / std::max(bounds.w, 1.0f), box.h / std::max(bounds.h, 1.0f));
        corner = yh::Vec2{bounds.x + bounds.w / 2, bounds.y + bounds.h / 2} - yh::Vec2{box.w / 2, box.h / 2} / scale;
    }

    renderer.fillRect(box, {10, 11, 16, 255});
    renderer.pushViewport(box);
    renderer.pushTransform({-corner.x * scale, -corner.y * scale}, scale);
    const yh::Rect visible{corner.x, corner.y, box.w / scale, box.h / scale};
    if (map)
    {
        map->bindTileset(renderer);
        renderer.fillRect(bounds, {0, 0, 0, 255});
        map->map().draw(renderer, visible, scale, 0);
        map->objects().draw(renderer, 0);
    }
    if (overview_)
    {
        // The camera's frame, and every camera step's target.
        const yh::Vec2 seen{screenWidth / frame.zoom, screenHeight / frame.zoom};
        renderer.drawRect({frame.camera.x - seen.x / 2, frame.camera.y - seen.y / 2, seen.x, seen.y}, ui.theme.accent, 2 / scale);
        for (size_t i = 0; i < editor.steps().size(); i++)
            if (editor.steps()[i].kind == CutsceneEditor::Kind::Camera)
                renderer.fillCircle(editor.steps()[i].camera, (i == step_ ? 7 : 4) / scale, i == step_ ? ui.theme.accent : yh::Color{200, 200, 210, 200}, 16);
    }
    renderer.pop();
    renderer.pop();

    if (!overview_)
    {
        // The overlay, drawn the way yh::Cutscene draws it on a screen of the preview's size.
        if (frame.fade.a > 0)
            renderer.fillRect(box, frame.fade);
        const float bar = box.h * 0.11f * yh::ease(yh::Ease::InOutQuad, frame.bars);
        if (bar > 0)
        {
            renderer.fillRect({box.x, box.y, box.w, bar}, {0, 0, 0, 255});
            renderer.fillRect({box.x, box.y + box.h - bar, box.w, bar}, {0, 0, 0, 255});
        }
        for (const CutsceneEditor::Frame::Line& line : frame.lines)
        {
            const uint8_t alpha = static_cast<uint8_t>(255 * line.alpha);
            const yh::Color color = line.title ? yh::Color{255, 214, 140, alpha} : yh::Color{240, 236, 226, alpha};
            const yh::Rect where = line.title ? yh::Rect{box.x, box.y + box.h * 0.5f - 60 * px, box.w, 120 * px}
                                              : yh::Rect{box.x + box.w * 0.1f, box.y + box.h - std::max(bar, box.h * 0.08f) - 70 * px, box.w * 0.8f, 60 * px};
            const std::string text = fit(ui, line.text, where.w);
            const float tw = widthOf(ui, text);
            ui.label({where.x + (where.w - tw) / 2, where.y + where.h / 2 - 10}, text, color);
        }
    }
    renderer.drawRect(box, ui.theme.panelBorder, 1);

    // A click on the map aims the picked camera step there.
    if (box.contains(input.mouse()) && input.buttonPressed(yh::MouseButton::Left) && step_ < editor.steps().size()
        && editor.steps()[step_].kind == CutsceneEditor::Kind::Camera)
    {
        CutsceneEditor::Step changed = editor.steps()[step_];
        const yh::Vec2 world = corner + (input.mouse() - yh::Vec2{box.x, box.y}) / scale;
        changed.camera = {std::round(world.x), std::round(world.y)};
        editor.endTyping();
        editor.setStep(step_, changed, "aim");
        editor.endTyping();
        // Show where it ends up.
        const std::vector<CutsceneEditor::Span> spans = editor.spans();
        time_ = spans[step_].end;
    }
}

void CutscenePanel::drawTimeline(CutsceneEditor& editor, yh::Ui& ui, const yh::Input& input, yh::Renderer& renderer, const yh::Rect& box)
{
    const double total = std::max(editor.length(), 1.0);
    // Play, back to the start, and the time.
    const float bx = box.x, by = box.y - 38;
    if (ui.button({bx, by, 80, 30}, playing_ ? "Stop" : "Play"))
    {
        if (!playing_ && time_ >= editor.length())
            time_ = 0;
        playing_ = !playing_;
    }
    if (ui.button({bx + 84, by, 40, 30}, "|<"))
    {
        time_ = 0;
        playing_ = false;
    }
    ui.label({bx + 134, by + 5}, shown(time_) + " / " + shown(editor.length()) + " s", ui.theme.text);
    ui.label({bx + 260, by + 5}, "space plays, click the bar to jump", ui.theme.textDim);

    renderer.fillRect(box, {18, 19, 24, 255});
    renderer.drawRect(box, ui.theme.panelBorder, 1);
    const std::vector<CutsceneEditor::Step>& steps = editor.steps();
    const std::vector<CutsceneEditor::Span> spans = editor.spans();
    const auto xOf = [&](double t) { return box.x + 4 + static_cast<float>(t / total) * (box.w - 8); };

    // Steps that run at once go in rows under each other.
    constexpr size_t rows = 3;
    std::vector<double> rowEnd(rows, -1);
    const float rowH = (box.h - 8) / rows;
    std::optional<size_t> clicked;
    for (size_t i = 0; i < steps.size(); i++)
    {
        size_t r = 0;
        while (r + 1 < rows && rowEnd[r] > spans[i].start + 1e-9)
            r++;
        rowEnd[r] = std::max(spans[i].end, spans[i].start + total * 0.01);
        const float x0 = xOf(spans[i].start), x1 = std::max(xOf(spans[i].end), x0 + 4);
        const yh::Rect block{x0, box.y + 4 + static_cast<float>(r) * rowH, x1 - x0 - 1, rowH - 2};
        renderer.fillRect(block, colorOf(steps[i].kind));
        if (i == step_)
            renderer.drawRect(block, ui.theme.accent, 2);
        const std::string name(CutsceneEditor::kindName(steps[i].kind));
        if (block.w > widthOf(ui, name) + 8)
            ui.label({block.x + 4, block.y + block.h / 2 - 10}, name, {240, 240, 240, 255});
        if (block.contains(input.mouse()) && input.buttonPressed(yh::MouseButton::Left))
            clicked = i;
    }

    if (input.buttonPressed(yh::MouseButton::Left) && box.contains(input.mouse()))
    {
        scrubbing_ = !clicked;
        playing_ = false;
        if (clicked)
        {
            step_ = *clicked;
            time_ = spans[step_].start;
            hint_.clear();
        }
    }
    if (scrubbing_ && input.buttonDown(yh::MouseButton::Left))
        time_ = std::clamp((input.mouse().x - box.x - 4) / (box.w - 8), 0.0f, 1.0f) * total;
    else
        scrubbing_ = false;

    const float at = xOf(std::min(time_, total));
    renderer.drawLine({at, box.y}, {at, box.y + box.h}, {255, 255, 255, 230}, 2);
}

void CutscenePanel::drawHooks(CutsceneHooks& hooks, const std::string& path, yh::Ui& ui, const yh::Rect& box)
{
    const float x = box.x, w = box.w, h = 26;
    float y = box.y;
    ui.label({x, y}, "Plays", ui.theme.accent);
    ui.label({x + 56, y}, "triggers and endings in chapter.json", ui.theme.textDim);
    y += 24;

    // The chapter's own moments, as switches.
    const float third = (w - 8) / 3;
    auto moment = [&](float at, const char* text, const std::string& now, bool enabled, const std::function<bool(const std::string&)>& set) {
        const bool on = hooks.plays(now, path);
        if (!enabled)
        {
            static_cast<void>(ui.button({at, y, third, h}, text, false));
            return;
        }
        if (ui.toggle({at, y, third, h}, text, on))
        {
            hooks.endTyping();
            set(on ? std::string() : path);
        }
    };
    moment(x, "Chapter cleared", hooks.cleared(), true, [&](const std::string& p) { return hooks.setCleared(p); });
    moment(x + third + 4, "Party wiped", hooks.wipe(), true, [&](const std::string& p) { return hooks.setWipe(p); });
    moment(x + 2 * (third + 4), "Chapter won", hooks.win(), hooks.hasWin(), [&](const std::string& p) { return hooks.setWin(p); });
    y += h + 4;
    // Another file taking a moment over is worth saying.
    std::string others;
    if (!hooks.cleared().empty() && !hooks.plays(hooks.cleared(), path))
        others += "cleared plays " + hooks.cleared() + "  ";
    if (!hooks.wipe().empty() && !hooks.plays(hooks.wipe(), path))
        others += "wiped plays " + hooks.wipe() + "  ";
    if (!hooks.win().empty() && !hooks.plays(hooks.win(), path))
        others += "won plays " + hooks.win();
    if (!others.empty())
    {
        ui.label({x, y}, fit(ui, "Now " + others, w), ui.theme.textDim);
        y += 22;
    }

    // Triggers that play this file.
    std::vector<size_t> mine;
    for (size_t i = 0; i < hooks.triggers().size(); i++)
        if (hooks.plays(hooks.triggers()[i].cutscene, path))
            mine.push_back(i);
    if (trigger_ && std::find(mine.begin(), mine.end(), *trigger_) == mine.end())
        trigger_.reset();
    const float bottom = box.y + box.h;
    for (const size_t i : mine)
    {
        if (y + h > bottom - (trigger_ ? 2 * (h + 6) : h + 4))
            break;
        const CutsceneHooks::Trigger& t = hooks.triggers()[i];
        const std::string when = t.when.empty() ? std::string("when the chapter starts") : "once " + joined(t.when) + (t.when.size() == 1 ? " is set" : " are set");
        if (ui.toggle({x, y, w, h}, fit(ui, "Trigger " + t.id + ": " + when + (t.dialogue.empty() ? "" : ", after " + t.dialogue), w - 16), trigger_ == i))
            trigger_ = trigger_ == i ? std::nullopt : std::optional<size_t>(i);
        y += h + 3;
    }

    y = std::max(y, bottom - (trigger_ ? 2 * (h + 6) : h + 4));
    if (trigger_)
    {
        const CutsceneHooks::Trigger t = hooks.triggers()[*trigger_];
        const float idW = 160;
        if (field(ui, "cut-trigger-id", {x, y, idW, h}, triggerIdText_, t.id, maxName) && !hooks.setTriggerId(*trigger_, triggerIdText_))
            hint_ = "A trigger id is a-z, 0-9, - and _, and not used yet";
        if (field(ui, "cut-when", {x + idW + 6, y, w - idW - 6, h}, whenText_, joined(t.when), 400) && listFrom(whenText_) != t.when
            && !hooks.setTriggerWhen(*trigger_, listFrom(whenText_)))
            hint_ = "Flags are 1 to 64 characters, each once";
        y += h + 6;
    }
    const float half = w / 2 - 2;
    if (ui.button({x, y, half, h}, "Add trigger"))
    {
        hooks.endTyping();
        trigger_ = hooks.addTrigger(path);
    }
    if (ui.button({x + half + 4, y, half, h}, "Remove trigger", trigger_.has_value()))
    {
        hooks.endTyping();
        hooks.removeTrigger(*trigger_);
        trigger_.reset();
    }
}

void CutscenePanel::drawStep(CutsceneEditor& editor, yh::Ui& ui, const yh::Rect& column)
{
    using Kind = CutsceneEditor::Kind;
    const float x = column.x + 8, w = column.w - 16, h = 28;
    float y = column.y + 8;
    if (editor.steps().empty())
    {
        ui.label({x, y}, "No steps yet. Add one on the left.", ui.theme.textDim);
        return;
    }
    // A copy: the commands below change the one in the editor.
    const CutsceneEditor::Step step = editor.steps()[step_];
    const CutsceneEditor::Span span = editor.spans()[step_];
    std::string name(CutsceneEditor::kindName(step.kind));
    name[0] = static_cast<char>(name[0] - 'a' + 'A');
    ui.label({x, y}, "Step " + std::to_string(step_ + 1) + ": " + name, ui.theme.accent);
    y += 22;
    ui.label({x, y}, "from " + shown(span.start) + " s to " + shown(span.end) + " s", ui.theme.textDim);
    y += 28;

    auto change = [&](const CutsceneEditor::Step& changed, std::string_view what, const char* why) {
        if (!editor.setStep(step_, changed, what))
            hint_ = why;
        else
            hint_.clear();
    };
    // A button press is its own undo step, apart from any typing before or after it.
    auto click = [&](const CutsceneEditor::Step& changed) {
        editor.endTyping();
        editor.setStep(step_, changed, "click");
        editor.endTyping();
    };

    const float label = 90;
    ui.label({x, y + 5}, step.kind == Kind::Pause ? "Waits (s)" : "Seconds", ui.theme.textDim);
    if (field(ui, "cut-seconds", {x + label, y, 90, h}, secondsText_, shown(step.seconds), 12))
    {
        const std::optional<double> s = numberFrom(secondsText_);
        CutsceneEditor::Step changed = step;
        changed.seconds = s.value_or(-1);
        if (!s || std::abs(*s - step.seconds) > 1e-9)
            change(changed, "seconds", "Seconds are a number from 0 to 600");
    }
    y += h + 6;
    bool waits = step.wait;
    if (ui.checkbox({x, y, w, h}, "The next step waits for it", waits))
    {
        CutsceneEditor::Step changed = step;
        changed.wait = waits;
        click(changed);
    }
    y += h + 10;

    switch (step.kind)
    {
    case Kind::Camera:
    {
        const float half = (w - label) / 2 - 3;
        ui.label({x, y + 5}, "Looks at", ui.theme.textDim);
        const bool typedX = field(ui, "cut-x", {x + label, y, half, h}, xText_, shown(step.camera.x), 12);
        const bool typedY = field(ui, "cut-y", {x + label + half + 6, y, half, h}, yText_, shown(step.camera.y), 12);
        if (typedX || typedY)
        {
            const std::optional<double> nx = numberFrom(xText_), ny = numberFrom(yText_);
            if (!nx || !ny)
                hint_ = "The camera's place is two numbers, in world units";
            else if (std::abs(*nx - step.camera.x) > 1e-3 || std::abs(*ny - step.camera.y) > 1e-3)
            {
                CutsceneEditor::Step changed = step;
                changed.camera = {static_cast<float>(*nx), static_cast<float>(*ny)};
                change(changed, "camera", "The camera's place is two numbers, in world units");
            }
        }
        y += h + 6;
        ui.label({x, y + 5}, "Zoom", ui.theme.textDim);
        if (field(ui, "cut-zoom", {x + label, y, 90, h}, zoomText_, shown(step.zoom), 12))
        {
            const std::optional<double> z = numberFrom(zoomText_);
            CutsceneEditor::Step changed = step;
            changed.zoom = static_cast<float>(z.value_or(-1));
            if (!z || std::abs(*z - step.zoom) > 1e-6)
                change(changed, "zoom", "Zoom is 0 to 64; 0 keeps the zoom it had");
        }
        ui.label({x + label + 98, y + 5}, "0 keeps it", ui.theme.textDim);
        y += h + 6;
        ui.label({x, y + 5}, "Ease", ui.theme.textDim);
        std::vector<std::string> names{std::string()};
        for (const std::string_view e : CutsceneEditor::eases())
            if (e != "inOutCubic")
                names.emplace_back(e);
        if (const int by = stepper(ui, {x + label, y, w - label, h}, step.ease.empty() ? std::string("inOutCubic") : step.ease))
        {
            const auto at = std::find(names.begin(), names.end(), step.ease);
            const size_t index = at == names.end() ? 0 : static_cast<size_t>(at - names.begin());
            CutsceneEditor::Step changed = step;
            changed.ease = names[(index + names.size() + static_cast<size_t>(by + static_cast<int>(names.size()))) % names.size()];
            click(changed);
        }
        y += h + 10;
        ui.label({x, y}, "Click the preview to aim it there.", ui.theme.textDim);
        break;
    }
    case Kind::Caption:
    case Kind::Title:
    {
        ui.label({x, y}, step.kind == Kind::Title ? "Title" : "Line", ui.theme.textDim);
        y += 20;
        if (field(ui, "cut-line", {x, y, w, h}, lineText_, step.text, maxLine))
        {
            CutsceneEditor::Step changed = step;
            changed.text = lineText_;
            change(changed, "line", "A line is at most 2000 characters");
        }
        y += h + 8;
        const float half = w / 2 - 2;
        if (ui.button({x, y, half, h}, step.kind == Kind::Title ? "Make caption" : "Make title"))
        {
            CutsceneEditor::Step changed = step;
            changed.kind = step.kind == Kind::Title ? Kind::Caption : Kind::Title;
            click(changed);
        }
        break;
    }
    case Kind::Fade:
    {
        ui.label({x, y}, "Fades to colour (r g b, then how solid)", ui.theme.textDim);
        y += 22;
        const float quarter = (w - 12) / 4;
        const uint8_t now[4] = {step.color.r, step.color.g, step.color.b, step.color.a};
        const char* ids[4] = {"cut-r", "cut-g", "cut-b", "cut-a"};
        for (int c = 0; c < 4; c++)
            if (field(ui, ids[c], {x + static_cast<float>(c) * (quarter + 4), y, quarter, h}, colorText_[c], std::to_string(now[c]), 3))
            {
                const std::optional<double> v = numberFrom(colorText_[c]);
                if (!v || *v < 0 || *v > 255 || *v != std::floor(*v))
                    hint_ = "Each part is a whole number from 0 to 255";
                else if (static_cast<uint8_t>(*v) != now[c])
                {
                    CutsceneEditor::Step changed = step;
                    uint8_t* parts[4] = {&changed.color.r, &changed.color.g, &changed.color.b, &changed.color.a};
                    *parts[c] = static_cast<uint8_t>(*v);
                    change(changed, ids[c], "");
                }
            }
        y += h + 8;
        const float half = w / 2 - 2;
        if (ui.button({x, y, half, h}, "To black"))
        {
            CutsceneEditor::Step changed = step;
            changed.color = {0, 0, 0, 255};
            click(changed);
        }
        if (ui.button({x + half + 4, y, half, h}, "Back in"))
        {
            CutsceneEditor::Step changed = step;
            changed.color.a = 0;
            click(changed);
        }
        break;
    }
    case Kind::Bars:
    {
        const float half = w / 2 - 2;
        CutsceneEditor::Step changed = step;
        if (ui.toggle({x, y, half, h}, "Bars in", step.on) && !step.on)
        {
            changed.on = true;
            click(changed);
        }
        if (ui.toggle({x + half + 4, y, half, h}, "Bars out", !step.on) && step.on)
        {
            changed.on = false;
            click(changed);
        }
        break;
    }
    case Kind::Event:
    {
        ui.label({x, y}, "Event name", ui.theme.textDim);
        y += 20;
        if (field(ui, "cut-event", {x, y, w, h}, eventText_, step.text, maxName))
        {
            CutsceneEditor::Step changed = step;
            changed.text = eventText_;
            change(changed, "event", "An event needs a name");
        }
        y += h + 6;
        std::string known;
        for (const std::string_view e : CutsceneEditor::events())
            known += (known.empty() ? "" : ", ") + std::string(e);
        ui.label({x, y}, fit(ui, "The game knows: " + known, w), ui.theme.textDim);
        break;
    }
    case Kind::Pause:
        ui.label({x, y}, "Nothing happens for that long.", ui.theme.textDim);
        break;
    }
}
