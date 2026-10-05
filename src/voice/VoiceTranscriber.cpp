#include "voice/VoiceTranscriber.h"
#include "voice/AudioDecode.h"

#include <SDL3/SDL_filesystem.h>

#include <algorithm>
#include <filesystem>
#include <thread>

#if YH_VOICE_WHISPER
#include <whisper.h>
#endif

namespace fs = std::filesystem;

namespace
{

#if YH_VOICE_WHISPER

class WhisperTranscriber final : public VoiceTranscriber
{
public:
    WhisperTranscriber(whisper_context* context, std::string model) : context_(context), model_(std::move(model)) {}
    ~WhisperTranscriber() override { whisper_free(context_); }

    std::string model() const override { return model_; }

    std::optional<std::vector<VoiceWord>> transcribe(std::span<const float> samples, const std::string& prompt, std::string* error) override
    {
        whisper_full_params params = whisper_full_default_params(WHISPER_SAMPLING_GREEDY);
        params.n_threads = static_cast<int>(std::clamp(std::thread::hardware_concurrency(), 1u, 8u));
        params.language = "en";
        params.no_context = true;
        params.print_progress = false;
        params.print_realtime = false;
        params.print_special = false;
        params.print_timestamps = false;
        params.token_timestamps = true;
        params.initial_prompt = prompt.empty() ? nullptr : prompt.c_str();
        if (whisper_full(context_, params, samples.data(), static_cast<int>(samples.size())) != 0)
        {
            if (error)
                *error = "whisper couldn't read the recording";
            return std::nullopt;
        }
        // Whisper counts in hundredths of a second.
        const whisper_token eot = whisper_token_eot(context_);
        std::vector<VoiceWord> words;
        for (int s = 0; s < whisper_full_n_segments(context_); s++)
        {
            std::vector<HeardToken> tokens;
            for (int t = 0; t < whisper_full_n_tokens(context_, s); t++)
            {
                const whisper_token_data data = whisper_full_get_token_data(context_, s, t);
                if (data.id >= eot) // start, end and timestamp tokens
                    continue;
                tokens.push_back({whisper_full_get_token_text(context_, s, t), data.p, data.t0 / 100.0, data.t1 / 100.0,
                    data.t_dtw >= 0 ? data.t_dtw / 100.0 : -1.0});
            }
            const std::vector<VoiceWord> segment = joinTokens(tokens, whisper_full_get_segment_t1(context_, s) / 100.0);
            words.insert(words.end(), segment.begin(), segment.end());
        }
        return words;
    }

private:
    whisper_context* context_;
    std::string model_;
};

void quiet(ggml_log_level, const char*, void*)
{
}

#endif

}

bool voiceBuiltIn()
{
#if YH_VOICE_WHISPER
    return true;
#else
    return false;
#endif
}

std::string voiceModelName(const std::string& file)
{
    std::string name = fs::path(file).filename().string();
    if (name.starts_with("ggml-"))
        name = name.substr(5);
    if (name.ends_with(".bin"))
        name.resize(name.size() - 4);
    if (const size_t quantized = name.rfind("-q"); quantized != std::string::npos)
        name.resize(quantized);
    return name;
}

std::string findVoiceModel()
{
    const char* base = SDL_GetBasePath();
    if (!base)
        return {};
    const fs::path folder(std::u8string_view(reinterpret_cast<const char8_t*>(base)));
    std::error_code problem;
#ifdef YH_VOICE_MODEL
    if (const fs::path picked = folder / ("ggml-" + std::string(YH_VOICE_MODEL) + "-q5_1.bin"); fs::is_regular_file(picked, problem))
        return picked.string();
#endif
    for (const fs::directory_entry& entry : fs::directory_iterator(folder, problem))
    {
        const std::string name = entry.path().filename().string();
        if (name.starts_with("ggml-") && name.ends_with(".bin"))
            return entry.path().string();
    }
    return {};
}

std::unique_ptr<VoiceTranscriber> loadWhisper(const std::string& modelFile, std::string* error)
{
#if YH_VOICE_WHISPER
    if (modelFile.empty())
    {
        if (error)
            *error = "there is no speech model beside the game (ggml-base.en-q5_1.bin)";
        return nullptr;
    }
    whisper_log_set(quiet, nullptr);
    whisper_context_params params = whisper_context_default_params();
    params.use_gpu = false;
    // DTW reads the attention weights, which flash attention doesn't keep.
    params.flash_attn = false;
    params.dtw_token_timestamps = true;
    const std::string name = voiceModelName(modelFile);
    params.dtw_aheads_preset = name == "tiny.en" ? WHISPER_AHEADS_TINY_EN
        : name == "base.en"                      ? WHISPER_AHEADS_BASE_EN
        : name == "tiny"                         ? WHISPER_AHEADS_TINY
        : name == "base"                         ? WHISPER_AHEADS_BASE
        : name == "small.en"                     ? WHISPER_AHEADS_SMALL_EN
                                                 : WHISPER_AHEADS_N_TOP_MOST;
    whisper_context* context = whisper_init_from_file_with_params(modelFile.c_str(), params);
    if (!context)
    {
        if (error)
            *error = "the speech model " + fs::path(modelFile).filename().string() + " couldn't be loaded";
        return nullptr;
    }
    return std::make_unique<WhisperTranscriber>(context, name);
#else
    (void)modelFile;
    if (error)
        *error = "this build has no speech recognition";
    return nullptr;
#endif
}

std::optional<VoiceLine> transcribeVoiceLine(std::span<const unsigned char> recording, const std::string& audioName, const std::string& written,
    const std::vector<std::string>& vocabulary, VoiceTranscriber& transcriber, std::string* error)
{
    const std::optional<std::vector<float>> samples = decodeVoiceAudio(recording, error);
    if (!samples)
        return std::nullopt;
    std::string prompt;
    for (const std::string& word : vocabulary)
        prompt += (prompt.empty() ? "" : ", ") + word;
    if (!prompt.empty())
        prompt += ".";
    const std::optional<std::vector<VoiceWord>> heard = transcriber.transcribe(*samples, prompt, error);
    if (!heard)
        return std::nullopt;
    VoiceLine line;
    line.audio = audioName;
    line.model = transcriber.model();
    const double length = static_cast<double>(samples->size()) / voiceSampleRate;
    line.words = matchWords(*heard, written, length);
    line.text = splitWords(written).empty() ? line.toText() : written;
    return line;
}
