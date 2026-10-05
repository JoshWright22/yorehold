// Voice lines: the voice file, joining whisper's tokens into words, matching them to the written
// line, decoding a recording, the editor's commands and undo, and an import saved from Create.
// Whisper itself isn't run here; a stand-in hears fixed words.

#include "screens/CreateScreen.h"
#include "screens/VoiceImporter.h"
#include "voice/AudioDecode.h"
#include "voice/VoiceLine.h"
#include "voice/VoiceTranscriber.h"

#include <yorehold/framework/save/SaveFile.h>

#include <cmath>
#include <cstdint>
#include <filesystem>
#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;

VoiceWord heard(const char* text, double start, double end, float confidence = 0.9f)
{
    return VoiceWord{text, start, end, confidence, true};
}

bool near(double a, double b)
{
    return std::abs(a - b) < 0.0005;
}

// A WAV file: 16-bit samples of a 440 Hz tone.
std::vector<unsigned char> wav(int rate, int channels, double seconds)
{
    const uint32_t frames = static_cast<uint32_t>(rate * seconds);
    const uint32_t data = frames * static_cast<uint32_t>(channels) * 2;
    std::vector<unsigned char> out;
    auto text = [&](const char* s) { out.insert(out.end(), s, s + 4); };
    auto u32 = [&](uint32_t v) { for (int i = 0; i < 4; i++) out.push_back(static_cast<unsigned char>(v >> (8 * i))); };
    auto u16 = [&](uint16_t v) { out.push_back(static_cast<unsigned char>(v)); out.push_back(static_cast<unsigned char>(v >> 8)); };
    text("RIFF");
    u32(36 + data);
    text("WAVE");
    text("fmt ");
    u32(16);
    u16(1);
    u16(static_cast<uint16_t>(channels));
    u32(static_cast<uint32_t>(rate));
    u32(static_cast<uint32_t>(rate * channels * 2));
    u16(static_cast<uint16_t>(channels * 2));
    u16(16);
    text("data");
    u32(data);
    for (uint32_t f = 0; f < frames; f++)
    {
        const auto sample = static_cast<int16_t>(std::sin(f * 2 * 3.14159265 * 440 / rate) * 8000);
        for (int c = 0; c < channels; c++)
            u16(static_cast<uint16_t>(sample));
    }
    return out;
}

// Hears the same words in anything, and says how long what it was given is.
class FixedEars final : public VoiceTranscriber
{
public:
    explicit FixedEars(std::vector<VoiceWord> words) : words_(std::move(words)) {}
    std::string model() const override { return "fixed"; }
    std::optional<std::vector<VoiceWord>> transcribe(std::span<const float> samples, const std::string& prompt, std::string*) override
    {
        given = samples.size();
        prompted = prompt;
        return words_;
    }
    size_t given = 0;
    std::string prompted;

private:
    std::vector<VoiceWord> words_;
};

void words(const Check& check)
{
    check(normalizeWord("Kharos,") == "kharos" && normalizeWord("Don't!") == "don't" && normalizeWord("'Tis") == "tis" && normalizeWord("--").empty(),
        "Words are compared without case or punctuation");
    const std::vector<std::string> split = splitWords("  Halt!  Who goes\nthere? ");
    check(split.size() == 4 && split[0] == "Halt!" && split[3] == "there?", "A line splits on spaces and keeps its punctuation");

    const std::vector<VoiceWord> joined = joinTokens({{" Halt", 0.9f, 0.0, 0.3, 0.12}, {"!", 0.4f, 0.3, 0.35, 0.3}, {" Who", 0.95f, 0.5, 0.7, 0.62},
        {" go", 0.8f, 0.7, 0.8, -1}, {"es", 0.7f, 0.8, 0.9, -1}, {" -", 0.9f, 0.9, 0.95, -1}, {" there", 0.9f, 1.0, 1.3, 1.05}}, 1.6);
    check(joined.size() == 4 && joined[0].text == "Halt!" && joined[2].text == "goes-" && joined[3].text == "there",
        "Tokens join into words; punctuation goes with the word before it");
    check(joined.size() == 4 && near(joined[0].start, 0.0) && near(joined[1].start, 0.3) && near(joined[2].start, 0.62) && near(joined[3].start, 1.0),
        "A word starts at the DTW time of the token before it, else at its own first token's start");
    check(joined.size() == 4 && near(joined[0].end, 0.3) && near(joined[3].end, 1.6), "and ends where the next one starts");
    check(joined.size() == 4 && near(joined[0].confidence, 0.4) && near(joined[2].confidence, 0.7), "A word is as sure as its least sure token");
}

void matching(const Check& check)
{
    const std::vector<VoiceWord> same = matchWords({heard("halt", 0.1, 0.4), heard("who", 0.5, 0.7), heard("goes", 0.7, 0.9), heard("there", 0.9, 1.2)},
        "Halt! Who goes there?", 1.5);
    check(same.size() == 4 && same[0].text == "Halt!" && same[3].text == "there?" && near(same[1].start, 0.5) && same[3].matched,
        "The written words take the heard timings");

    const std::vector<VoiceWord> named = matchWords({heard("Carlos", 0.2, 0.6, 0.3f), heard("waits", 0.6, 0.9)}, "Kharos waits.", 1);
    check(named.size() == 2 && named[0].text == "Kharos" && named[0].matched && near(named[0].start, 0.2) && near(named[0].confidence, 0.3),
        "A misheard name keeps the written spelling and the heard time");

    const std::vector<VoiceWord> missing = matchWords({heard("the", 0.0, 0.2), heard("gate", 0.6, 0.9), heard("is", 0.9, 1.0), heard("open", 1.0, 1.4)},
        "The north gate is open", 1.5);
    check(missing.size() == 5 && !missing[1].matched && missing[1].text == "north" && near(missing[1].start, 0.2) && near(missing[1].end, 0.6),
        "A word not heard gets the time between its neighbours, flagged");
    check(missing.size() == 5 && missing[0].matched && missing[2].matched && near(missing[2].start, 0.6), "and the rest still match");

    const std::vector<VoiceWord> extra = matchWords({heard("uh", 0.0, 0.2), heard("the", 0.2, 0.4), heard("gate", 0.4, 0.8)}, "The gate.", 1);
    check(extra.size() == 2 && extra[0].matched && near(extra[0].start, 0.2) && extra[1].text == "gate.", "A heard word not written is left out");

    const std::vector<VoiceWord> pieces = matchWords({heard("car", 0.2, 0.4), heard("los", 0.4, 0.7), heard("waits", 0.7, 1.0)}, "Kharos waits", 1);
    check(pieces.size() == 2 && pieces[0].matched && near(pieces[0].start, 0.2) && near(pieces[0].end, 0.7), "A name heard in two pieces is one word");

    const std::vector<VoiceWord> other = matchWords({heard("banana", 0.2, 0.6)}, "Wren", 1);
    check(other.size() == 1 && !other[0].matched && near(other[0].start, 0.2), "A heard word nothing like the written one keeps its time but is flagged");

    const std::vector<VoiceWord> cut = matchWords({heard("my", 0.0, 0.2), heard("keep", 0.2, 0.5)}, "My keep. Well, the lord's keep.", 1.5);
    check(cut.size() == 6 && cut[1].matched && near(cut[1].start, 0.2) && !cut[5].matched,
        "A recording that stops early matches the first of two same words");
    check(cut.size() == 6 && near(cut[2].start, 0.5) && near(cut[5].end, 1.5), "and the rest share what is left of it");

    const std::vector<VoiceWord> suggestion = matchWords({heard("hello", 0.1, 0.5)}, "  ", 1);
    check(suggestion.size() == 1 && suggestion[0].text == "hello" && suggestion[0].matched, "With no written line the heard words are kept");

    const std::vector<VoiceWord> silent = matchWords({}, "Two words", 2);
    check(silent.size() == 2 && !silent[0].matched && near(silent[0].start, 0) && near(silent[1].end, 2), "Nothing heard spreads the line over the recording");
}

void file(const Check& check)
{
    VoiceLine line;
    line.audio = "wren.hello.wav";
    line.model = "base.en";
    line.text = "Halt! Who goes there?";
    line.words = matchWords({heard("halt", 0.1004, 0.4), heard("who", 0.5, 0.7, 0.42f), heard("goes", 0.7, 0.9), heard("there", 0.9, 1.25)}, line.text, 1.5);
    const std::string json = line.toJson();
    std::string error;
    const std::optional<VoiceLine> back = VoiceLine::fromJson(json, &error);
    check(back && back->audio == "wren.hello.wav" && back->model == "base.en" && back->words.size() == 4 && near(back->words[0].start, 0.1)
            && near(back->words[1].confidence, 0.42) && back->toJson() == json,
        "A voice file reads back the same, times in milliseconds");
    check(json.find("\"format\": 1") != std::string::npos, "It says its format");
    check(!VoiceLine::fromJson(R"({"format": 2, "words": []})", &error) && error.find("newer") != std::string::npos, "A newer format is refused, saying so");
    check(!VoiceLine::fromJson(R"({"audio": "a.wav", "words": []})", &error) && error.find("format") != std::string::npos, "One with no format is refused");
    check(!VoiceLine::fromJson(R"({"format": 1, "words": [{"text": "a", "start": 2, "end": 1}]})", &error) && error.find("word 1") != std::string::npos,
        "A word that ends before it starts is refused, naming it");
    check(VoiceLine::fromJson(R"({"format": 1, "words": [{"text": "a", "start": 0, "end": 1}]})").has_value(), "Confidence and matched may be left out");

    check(line.toText() == "Halt! Who goes there?", "Plain text is made from it");
    const std::string srt = line.toSrt();
    check(srt.starts_with("1\n00:00:00,100 --> 00:00:01,250\nHalt! Who goes there?\n"), "and SRT");
    check(line.toVtt().starts_with("WEBVTT\n\n00:00:00.100 --> 00:00:01.250\n"), "and VTT");
    check(line.lowConfidence(0.6f) == 1, "Words heard unsurely are counted against the cutoff");
}

void decoding(const Check& check)
{
    const std::vector<unsigned char> stereo = wav(44100, 2, 0.5);
    const std::optional<std::vector<float>> samples = decodeVoiceAudio(stereo);
    check(samples && samples->size() > 7900 && samples->size() < 8100, "A 44.1 kHz stereo WAV becomes 16 kHz mono");
    float loudest = 0;
    if (samples)
        for (const float s : *samples)
            loudest = std::max(loudest, std::abs(s));
    check(loudest > 0.2f && loudest < 0.3f, "at the same loudness");
    std::string error;
    const std::vector<unsigned char> junk{'n', 'o', 'p', 'e'};
    check(!decodeVoiceAudio(junk, &error) && !error.empty(), "Something that isn't a recording is refused");

    FixedEars ears({heard("Carlos", 0.1, 0.4), heard("waits", 0.4, 0.8)});
    const std::optional<VoiceLine> line = transcribeVoiceLine(wav(16000, 1, 1.0), "wren.hello.wav", "Kharos waits.", {"Kharos", "Wren"}, ears, &error);
    check(line && line->text == "Kharos waits." && line->model == "fixed" && line->audio == "wren.hello.wav" && line->words[0].text == "Kharos",
        "An import keeps the written line");
    check(ears.given == 16000 && ears.prompted == "Kharos, Wren.", "Whisper gets the samples and the vocabulary as its prompt");
    const std::optional<VoiceLine> suggested = transcribeVoiceLine(wav(16000, 1, 1.0), "a.wav", "", {}, ears, &error);
    check(suggested && suggested->text == "Carlos waits", "With no written line, what was heard becomes the suggestion");
    error.clear();
    check(!loadWhisper("", &error) && !error.empty(), "Without a model file whisper says why");
    check(voiceModelName("C:/x/ggml-base.en-q5_1.bin") == "base.en" && voiceModelName("ggml-tiny.en.bin") == "tiny.en", "Model names come from their files");
}

void editor(const Check& check, const std::filesystem::path& scratch)
{
    const std::filesystem::path package = scratch / "voice-package";
    std::filesystem::create_directories(package / "voice");
    const std::vector<unsigned char> recording = wav(16000, 1, 1.0);
    yh::writeFileAtomically((package / "voice/wren.hello.wav").string(), std::string(recording.begin(), recording.end()), false);
    yh::writeFileAtomically((package / "voice/vocabulary.txt").string(), "# names\nKharos\n  Tobb  \r\n\n", false);
    VoiceLine old;
    old.audio = "wren.bye.ogg";
    old.model = "base.en";
    old.text = "Go.";
    old.words = {heard("Go.", 0.1, 0.3)};
    yh::writeFileAtomically((package / "voice/wren.bye.voice.json").string(), old.toJson(), false);
    yh::writeFileAtomically((package / "voice/wren.broken.voice.json").string(), "{", false);
    yh::FileSystem files;
    files.mountFolder(package.string(), "package");

    check(VoiceImporter::vocabulary(files) == std::vector<std::string>{"Kharos", "Tobb"}, "The vocabulary skips blanks and # lines");
    std::string error;
    const std::optional<VoiceImporter::Settings> settings = VoiceImporter::Settings::fromJson(R"({"flagBelow": 0.5, "extensions": ["ogg"]})", &error);
    check(settings && near(settings->flagBelow, 0.5) && settings->extensions == std::vector<std::string>{"ogg"}, "Settings read from data");
    check(!VoiceImporter::Settings::fromJson(R"({"flagBelow": 3})", &error) && !error.empty(), "A cutoff over 1 is refused");

    yh::History history;
    VoiceImporter voices(history);
    check(VoiceImporter::stem("wren", "hello") == "wren.hello" && VoiceImporter::voicePath("wren.hello") == "voice/wren.hello.voice.json",
        "A node's voice is named after its conversation and node");
    const VoiceImporter::Line& hello = voices.line(files, "wren.hello");
    check(hello.audio == "voice/wren.hello.wav" && !hello.voice && hello.error.empty(), "A recording with no voice file yet is found");
    check(voices.problems("wren.hello", "Hello.").size() == 1, "and waits to be imported");
    const VoiceImporter::Line& bye = voices.line(files, "wren.bye");
    check(bye.audio.empty() && bye.voice && bye.voice->text == "Go.", "A voice file is read");
    check(voices.problems("wren.bye", "Go.").size() == 1, "and one with no recording beside it is pointed out");
    check(!voices.line(files, "wren.broken").error.empty() && voices.problems("wren.broken", "").front().error, "A broken voice file is an error");
    check(voices.changed().empty(), "Nothing is changed by reading");

    VoiceLine imported;
    imported.audio = "wren.hello.wav";
    imported.model = "base.en";
    imported.text = "Well met, Kharos.";
    imported.words = matchWords({heard("well", 0.1, 0.3), heard("met", 0.3, 0.5), heard("Carlos", 0.5, 0.9, 0.3f)}, imported.text, 1);
    check(voices.setVoice("wren.hello", imported) && voices.find("wren.hello")->voice->text == "Well met, Kharos.", "An import is set");
    check(voices.changed().size() == 1 && voices.changed()[0].first == "voice/wren.hello.voice.json", "and is a change to save");
    const std::vector<VoiceImporter::Problem> flagged = voices.problems("wren.hello", "Well met, Kharos.");
    check(flagged.size() == 1 && flagged[0].text.find("unsurely") != std::string::npos, "A word below the cutoff is flagged");
    history.undo();
    check(!voices.find("wren.hello")->voice && voices.changed().empty(), "Undo takes the import back");
    history.redo();
    check(voices.find("wren.hello")->voice.has_value(), "Redo puts it back");

    // The writer changes the line; the words follow without listening again.
    check(VoiceImporter::differs(*voices.find("wren.hello")->voice, "Well met, Kharos of the keep."), "A changed line is noticed");
    check(voices.match("wren.hello", "Well met, Kharos of the keep."), "Match lines up the words again");
    const VoiceLine& rematched = *voices.find("wren.hello")->voice;
    check(rematched.words.size() == 6 && rematched.words[2].text == "Kharos" && near(rematched.words[2].start, 0.5) && !rematched.words[4].matched,
        "Kept words keep their times; new ones are guessed");
    check(!voices.match("wren.hello", "Well met, Kharos of the keep."), "Matching again changes nothing");
    history.undo();
    check(voices.find("wren.hello")->voice->text == "Well met, Kharos.", "Undo takes the match back");

    // "Look again" doesn't drop unsaved work.
    check(voices.line(files, "wren.hello", true).voice.has_value(), "Reading again keeps an unsaved import");
    voices.markSaved("voice/wren.hello.voice.json");
    check(voices.changed().empty(), "Once saved there is nothing to save");

    // The panel imports off the main thread, here with a stand-in for whisper.
    VoicePanel panel;
    panel.setLoader([](std::string*) { return std::make_unique<FixedEars>(std::vector<VoiceWord>{heard("hello", 0.2, 0.6)}); });
    check(!panel.startImport(voices, files, "wren.nobody", "Hi.") && !panel.status().empty(), "No recording, no import, and it says why");
    check(panel.startImport(voices, files, "wren.hello", "Hello there.") && panel.busy(), "An import starts");
    check(panel.finish(voices, true) && !panel.busy(), "and finishes");
    const VoiceLine& done = *voices.find("wren.hello")->voice;
    check(done.model == "fixed" && done.text == "Hello there." && done.words.size() == 2 && done.words[0].matched && !done.words[1].matched,
        "Its result is the voice line, matched to the written one");
    history.undo();
    check(voices.find("wren.hello")->voice->text == "Well met, Kharos.", "An import from the panel undoes too");

    VoicePanel deaf;
    deaf.setLoader([](std::string* why) {
        *why = "no model";
        return std::unique_ptr<VoiceTranscriber>();
    });
    check(deaf.startImport(voices, files, "wren.hello", "x") && deaf.finish(voices, true) && deaf.status().find("no model") != std::string::npos,
        "A missing model is reported, not a crash");
}

void inCreate(const Check& check, const std::filesystem::path& scratch)
{
    yh::Ui ui;
    yh::Input input;
    yh::Font* title = nullptr;
    CreateScreen screen(ui, input, title);
    screen.table.stateDir = (scratch / "voice-state").generic_string() + "/";
    screen.newPackage();
    screen.newDialogue();
    DialogueEditor* conversation = screen.dialogueEditor();
    check(conversation && !conversation->nodes().empty(), "A new conversation to voice");
    if (!conversation)
        return;
    conversation->setText(0, "Who's there?");
    const std::string stem = VoiceImporter::stem(conversation->id(), conversation->nodes()[0].id);
    const std::filesystem::path root = screen.packagePath();
    std::filesystem::create_directories(root / "voice");
    const std::vector<unsigned char> recording = wav(22050, 1, 0.8);
    yh::writeFileAtomically((root / "voice" / (stem + ".wav")).string(), std::string(recording.begin(), recording.end()), false);

    VoiceImporter* voices = screen.voiceImporter();
    const yh::FileSystem* files = screen.packageFiles();
    check(voices && files && near(voices->settings().flagBelow, 0.6), "Create has voice lines, with the game's settings");
    if (!voices || !files)
        return;
    screen.voicePanel().setLoader([](std::string*) {
        return std::make_unique<FixedEars>(std::vector<VoiceWord>{heard("who's", 0.1, 0.3), heard("there", 0.3, 0.7)});
    });
    check(screen.voicePanel().startImport(*voices, *files, stem, conversation->nodes()[0].text) && screen.voicePanel().finish(*voices, true),
        "A recording in the package is imported");
    check(screen.save(), "Save");
    const std::optional<std::string> written = yh::readTextFile((root / VoiceImporter::voicePath(stem)).string());
    const std::optional<VoiceLine> read = written ? VoiceLine::fromJson(*written) : std::nullopt;
    check(read && read->text == "Who's there?" && read->audio == stem + ".wav" && read->words.size() == 2, "writes its voice file beside the recording");
    screen.undo();
    check(!voices->find(stem)->voice, "The import is on the shared history");
}

}

void voiceTests(const Check& check, const std::filesystem::path& scratch)
{
    words(check);
    matching(check);
    file(check);
    decoding(check);
    editor(check, scratch);
    inCreate(check, scratch);
}
