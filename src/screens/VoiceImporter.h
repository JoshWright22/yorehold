#pragma once

#include "screens/DialogueEditor.h"
#include "voice/VoiceLine.h"
#include "voice/VoiceTranscriber.h"

#include <yorehold/framework/assets/FileSystem.h>
#include <yorehold/framework/editor/History.h>
#include <yorehold/framework/graphics/Renderer.h>
#include <yorehold/framework/graphics/Types.h>
#include <yorehold/framework/input/Input.h>
#include <yorehold/framework/ui/Ui.h>

#include <functional>
#include <future>
#include <map>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

// Voice lines in Dialogue mode, in two parts like the other modes: VoiceImporter is the package's
// voice files and the commands that change them (no drawing, so tests and other layouts can use
// it), VoicePanel is the desktop layout over one conversation.
//
// A node's recording is voice/<conversation id>.<node id>.wav (or .ogg) in the package, and its
// words with timings go in voice/<conversation id>.<node id>.voice.json beside it.
class VoiceImporter
{
public:
    // From create/voice.json in the game's assets.
    struct Settings
    {
        float flagBelow = 0.6f;                         // words heard less surely than this are flagged
        std::vector<std::string> extensions{"wav", "ogg"}; // recordings looked for, in this order
        static std::optional<Settings> fromJson(std::string_view json, std::string* error = nullptr);
    };

    struct Line
    {
        std::string stem;              // "wren.hello"
        std::string audio;             // "voice/wren.hello.wav"; empty = no recording yet
        std::optional<VoiceLine> voice;
        std::string saved;             // its voice file as read or written; empty = not on disk
        std::string error;             // why its voice file can't be read
    };

    struct Problem
    {
        std::string text;
        bool error = false;
    };

    explicit VoiceImporter(yh::History& history, Settings settings = {}) : history_(history), settings_(std::move(settings)) {}
    VoiceImporter(const VoiceImporter&) = delete;
    VoiceImporter& operator=(const VoiceImporter&) = delete;

    const Settings& settings() const { return settings_; }
    static std::string stem(std::string_view conversation, std::string_view node);
    static std::string voicePath(std::string_view stem) { return "voice/" + std::string(stem) + ".voice.json"; }

    // A node's recording and voice file, read the first time. `again` reads them again, unless
    // there are changes to its voice file that aren't saved.
    const Line& line(const yh::FileSystem& files, const std::string& stem, bool again = false);
    const Line* find(std::string_view stem) const;

    // An import's result, as one undo step.
    bool setVoice(const std::string& stem, const VoiceLine& voice);
    // Lines the words up with the written line again, without listening again: for a line the
    // writer changed, or a name fixed ("Carlos" to "Kharos"). False if there is nothing to change.
    bool match(const std::string& stem, std::string_view written);

    // What is worth a look in one line against the node's written text.
    std::vector<Problem> problems(std::string_view stem, std::string_view written) const;
    // The written line and the voice's words differ.
    static bool differs(const VoiceLine& voice, std::string_view written);

    // Voice files changed since they were read or written: path, then text.
    std::vector<std::pair<std::string, std::string>> changed() const;
    void markSaved(std::string_view path);

    // The names in the package's voice/vocabulary.txt, one per line, given to whisper as a hint.
    static std::vector<std::string> vocabulary(const yh::FileSystem& files);

private:
    void put(const std::string& stem, std::string_view label, std::optional<VoiceLine> voice);

    yh::History& history_;
    Settings settings_;
    std::map<std::string, Line, std::less<>> lines_; // by stem; never removed, the history points at them
};

// The desktop layout: the conversation's nodes on the left with how far each one's voice is, the
// picked one's recording, buttons and words on the right. Imports run off the main thread.
class VoicePanel
{
public:
    // Makes the transcriber the first time one is needed; whisper with the model beside the exe
    // unless set otherwise. Runs off the main thread.
    using Loader = std::function<std::unique_ptr<VoiceTranscriber>(std::string* error)>;

    VoicePanel();
    ~VoicePanel();
    VoicePanel(const VoicePanel&) = delete;
    VoicePanel& operator=(const VoicePanel&) = delete;

    void setLoader(Loader loader);
    void draw(VoiceImporter& voices, DialogueEditor& conversation, const yh::FileSystem& files, yh::Ui& ui, const yh::Input& input,
        yh::Renderer& renderer, const yh::Rect& area);
    // An import is running.
    bool busy() const { return job_.valid(); }
    // Starts listening to a node's recording; false (and the reason in `status()`) if it can't.
    bool startImport(VoiceImporter& voices, const yh::FileSystem& files, const std::string& stem, const std::string& written);
    // Takes a finished import in; true once one was. `wait` blocks until it is done.
    bool finish(VoiceImporter& voices, bool wait = false);
    const std::string& status() const { return status_; }
    // Forget the picked node, for another file. A running import still finishes.
    void reset();

private:
    struct Result
    {
        std::optional<VoiceLine> line;
        std::string error;
    };
    // The transcriber, kept between imports so the model loads once.
    struct Slot
    {
        std::mutex mutex;
        Loader loader;
        std::unique_ptr<VoiceTranscriber> transcriber;
    };

    void drawNodes(VoiceImporter& voices, DialogueEditor& conversation, const yh::FileSystem& files, yh::Ui& ui, const yh::Rect& column);
    void drawLine(VoiceImporter& voices, DialogueEditor& conversation, const yh::FileSystem& files, yh::Ui& ui, yh::Renderer& renderer,
        const yh::Rect& column);

    std::shared_ptr<Slot> slot_;
    std::future<Result> job_;
    std::string jobStem_;
    size_t node_ = 0;
    float nodeScroll_ = 0;
    float wordScroll_ = 0;
    std::string status_;
};
