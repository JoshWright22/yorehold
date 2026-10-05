#pragma once

#include <yorehold/framework/animation/Cutscene.h>
#include <yorehold/framework/editor/History.h>
#include <yorehold/framework/graphics/Renderer.h>
#include <yorehold/framework/graphics/Types.h>
#include <yorehold/framework/input/Input.h>
#include <yorehold/framework/ui/Ui.h>

#include <functional>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

class GameMap;

// Cutscene mode of the Create screen, in two parts like the other modes: CutsceneEditor is one
// cutscene file and the commands that change it (no drawing, so tests and other layouts can use
// it), CutscenePanel is the desktop layout. CutsceneHooks is the part of a chapter.json that says
// when its cutscenes play.
//
// It reads and writes the framework's cutscene format (yh::Cutscene). Fields it has no tool for
// are written back as they were. Every command goes on the history it was given, which the whole
// Create screen shares.
class CutsceneEditor
{
public:
    enum class Kind { Pause, Camera, Caption, Title, Fade, Bars, Event };

    struct Step
    {
        Kind kind = Kind::Pause;
        double seconds = 1;
        bool wait = true;          // false = the next step starts with this one
        yh::Vec2 camera;           // Camera, in world units
        float zoom = 0;            // Camera; 0 keeps the zoom it had
        std::string ease;          // a name from eases(); empty = the game's default
        std::string text;          // Caption and Title line, Event name
        yh::Color color{0, 0, 0, 255}; // Fade target; alpha 0 fades back in
        bool on = true;            // Bars
        std::string extra;         // fields the editor has no tool for, as a JSON object; empty = none
    };

    // When a step runs, in seconds from the start.
    struct Span
    {
        double start = 0, end = 0;
    };

    // What the screen shows at one moment, worked out the way yh::Cutscene plays it.
    struct Frame
    {
        yh::Vec2 camera;
        float zoom = 1;
        yh::Color fade{0, 0, 0, 0};
        float bars = 0; // 0..1, how far in the letterbox bars are
        struct Line
        {
            std::string text;
            bool title = false;
            float alpha = 1;
        };
        std::vector<Line> lines;
    };

    struct Problem
    {
        std::string text;
        bool error = true; // false = worth a look, but the file still saves and plays
    };

    explicit CutsceneEditor(yh::History& history) : history_(history) {}
    CutsceneEditor(const CutsceneEditor&) = delete;
    CutsceneEditor& operator=(const CutsceneEditor&) = delete;

    // Only a file the game would load: what it refuses is refused here too, with its reason.
    bool load(std::string_view json, std::string* error = nullptr);
    // A new cutscene: bars in, a caption, bars out.
    void create();
    bool loaded() const { return loaded_; }
    std::string toJson() const;
    // As the game reads it; nullopt (and why) while something in it would be refused.
    std::optional<yh::Cutscene> cutscene(std::string* error = nullptr) const;

    const std::vector<Step>& steps() const { return state_.steps; }
    // The map's size in world units, for the camera warnings; none = not checked.
    void setBounds(std::optional<yh::Rect> bounds) { bounds_ = bounds; }

    // Timing: when each step starts and ends, and how long the whole thing runs.
    std::vector<Span> spans() const;
    double length() const;
    // `camera` and `zoom` are where the view is when it starts (the game starts from the party).
    Frame frameAt(double seconds, yh::Vec2 camera, float zoom) const;

    // Steps. A new one goes after `after` (or at the end) with its kind's defaults.
    std::optional<size_t> addStep(Kind kind, std::optional<size_t> after = std::nullopt);
    bool removeStep(size_t step);
    // One place up (-1) or down (1).
    bool moveStep(size_t step, int by);
    std::optional<size_t> copyStep(size_t step);
    // Any change to one step: refused (false) if the game wouldn't read it. Typing in a box is one
    // undo step until endTyping().
    // `field` keeps typing in two boxes of one step apart on the history.
    bool setStep(size_t step, const Step& changed, std::string_view field = {});
    void endTyping() { history_.breakMerge(); }

    std::vector<Problem> problems() const;

    static std::string_view kindName(Kind kind);
    static std::vector<std::string_view> eases();
    // The events Yorehold does something with (PlayScreen).
    static std::vector<std::string_view> events();

private:
    // What an undo step puts back.
    struct State
    {
        std::vector<Step> steps;
        std::string extra;
    };
    template <typename Change> void edit(std::string_view label, Change change, std::string_view mergeKey = {});

    yh::History& history_;
    bool loaded_ = false;
    State state_;
    std::optional<yh::Rect> bounds_;
};

// Where a chapter plays its cutscenes: its triggers (onEnter and onFlag), `endings.cleared`,
// `onWipe.cutscene` and `winCondition.cutscene`. It changes only those in the chapter.json it is
// given at save, so it can sit on top of what Encounters mode writes.
class CutsceneHooks
{
public:
    struct Trigger
    {
        std::string id;
        std::vector<std::string> when; // empty = when the chapter starts
        std::string dialogue;          // as written in the file
        std::string cutscene;
        std::string extra;
        bool operator==(const Trigger&) const = default;
    };

    struct Problem
    {
        std::string text;
        bool error = true;
    };

    // Is there a file at this package path (saved or only open)? Used to resolve the paths a
    // chapter names the way the game does: in the chapter folder first, then from the root.
    using Exists = std::function<bool(const std::string&)>;

    explicit CutsceneHooks(yh::History& history) : history_(history) {}
    CutsceneHooks(const CutsceneHooks&) = delete;
    CutsceneHooks& operator=(const CutsceneHooks&) = delete;

    bool load(std::string_view chapterJson, const std::string& folder, Exists exists = {}, std::string* error = nullptr);
    bool loaded() const { return loaded_; }
    // `chapterJson` with these fields as they are here; a field this doesn't change keeps its text.
    std::string applyTo(std::string_view chapterJson) const;
    // Something here differs from the last load or markSaved().
    bool changed() const;
    void markSaved();

    const std::string& folder() const { return folder_; }
    const std::vector<Trigger>& triggers() const { return state_.triggers; }
    const std::string& cleared() const { return state_.cleared; }
    const std::string& wipe() const { return state_.wipe; }
    bool hasWin() const { return state_.hasWin; }
    const std::string& win() const { return state_.win; }

    // The package path a name in chapter.json stands for, and the name to write for a package path.
    std::string resolve(const std::string& named) const;
    std::string nameFor(const std::string& path) const;
    bool plays(const std::string& named, const std::string& path) const { return !named.empty() && resolve(named) == path; }
    // Every cutscene the chapter names, as package paths.
    std::vector<std::string> named() const;

    // A trigger that plays `path`, with a free id ("cutscene-N").
    std::optional<size_t> addTrigger(const std::string& path, const std::vector<std::string>& when = {});
    // One that also opens a conversation keeps it and only stops playing the cutscene.
    bool removeTrigger(size_t trigger);
    // Ids use a-z, 0-9, - and _ and are unique in the chapter.
    bool setTriggerId(size_t trigger, const std::string& id);
    bool setTriggerWhen(size_t trigger, const std::vector<std::string>& when);
    // A package path, or "" for none. The win one only while the chapter has a winCondition.
    bool setCleared(const std::string& path);
    bool setWipe(const std::string& path);
    bool setWin(const std::string& path);
    void endTyping() { history_.breakMerge(); }

    std::vector<Problem> problems() const;

private:
    struct State
    {
        std::vector<Trigger> triggers;
        std::string cleared, wipe, win;
        bool hasWin = false;
        bool operator==(const State&) const = default;
    };
    static std::optional<State> read(std::string_view chapterJson, std::string* error);
    template <typename Change> void edit(std::string_view label, Change change, std::string_view mergeKey = {});

    yh::History& history_;
    bool loaded_ = false;
    std::string folder_;
    Exists exists_;
    State state_;
    State saved_;
};

// The desktop layout over a CutsceneEditor: the steps on the left, the preview, timeline and where
// the cutscene plays in the middle, the picked step on the right. Space plays and stops the preview.
class CutscenePanel
{
public:
    // `map` is the chapter's, drawn under the preview; null draws an empty stage. `hooks` and
    // `path` say when this file plays; null leaves that part out.
    void draw(CutsceneEditor& editor, CutsceneHooks* hooks, const std::string& path, GameMap* map, yh::Ui& ui, const yh::Input& input,
        yh::Renderer& renderer, const yh::Rect& area);
    // Moves the preview on while it plays.
    void update(const CutsceneEditor& editor, double deltaSeconds);
    // A text box of this mode is being typed in, so shortcuts and the Delete key are its own.
    static bool typing(const yh::Ui& ui);
    // Forget the picked step and the preview, for another file.
    void reset() { *this = CutscenePanel(); }

    // The screen size the preview frames, as the game's window is by default.
    static constexpr float screenWidth = 1280, screenHeight = 720;

private:
    void drawSteps(CutsceneEditor& editor, yh::Ui& ui, const yh::Rect& column);
    void drawPreview(CutsceneEditor& editor, GameMap* map, yh::Ui& ui, const yh::Input& input, yh::Renderer& renderer, const yh::Rect& box);
    void drawTimeline(CutsceneEditor& editor, yh::Ui& ui, const yh::Input& input, yh::Renderer& renderer, const yh::Rect& box);
    void drawHooks(CutsceneHooks& hooks, const std::string& path, yh::Ui& ui, const yh::Rect& box);
    void drawStep(CutsceneEditor& editor, yh::Ui& ui, const yh::Rect& column);

    size_t step_ = 0;
    std::optional<size_t> trigger_;
    double time_ = 0;
    bool playing_ = false;
    bool overview_ = false; // the whole map with the camera's frame on it, not the camera's view
    bool scrubbing_ = false;
    float scroll_ = 0;
    bool wasTyping_ = false;
    yh::Vec2 start_;      // where the preview's view starts
    float startZoom_ = 1;
    bool started_ = false;
    std::string hint_;
    // What the text boxes show while they are typed in.
    std::string secondsText_, xText_, yText_, zoomText_, lineText_, colorText_[4], eventText_, triggerIdText_, whenText_;
};
