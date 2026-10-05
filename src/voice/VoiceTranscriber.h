#pragma once

#include "voice/VoiceLine.h"

#include <memory>
#include <optional>
#include <span>
#include <string>
#include <vector>

// Hears the words in a recording. Whisper in the desktop build; tests and the browser build use
// their own. One call at a time per object, from any thread.
class VoiceTranscriber
{
public:
    virtual ~VoiceTranscriber() = default;
    // "base.en", written into the voice file.
    virtual std::string model() const = 0;
    // `samples` are 16 kHz mono. `prompt` steers it towards spellings (the package's names).
    virtual std::optional<std::vector<VoiceWord>> transcribe(std::span<const float> samples, const std::string& prompt, std::string* error = nullptr) = 0;
};

// False in builds without whisper (the browser and phones).
bool voiceBuiltIn();
// Whisper with this model file. Null, and why, without whisper or when the file can't be loaded.
std::unique_ptr<VoiceTranscriber> loadWhisper(const std::string& modelFile, std::string* error = nullptr);
// The model beside the exe: the one the build picked, else any other. Empty when there is none.
std::string findVoiceModel();
// "ggml-base.en-q5_1.bin" -> "base.en"
std::string voiceModelName(const std::string& file);

// What an import does: decode, listen, then match to the written line (empty = keep what was
// heard, as a suggestion). Touches no editor, so it can run off the main thread.
std::optional<VoiceLine> transcribeVoiceLine(std::span<const unsigned char> recording, const std::string& audioName, const std::string& written,
    const std::vector<std::string>& vocabulary, VoiceTranscriber& transcriber, std::string* error = nullptr);
