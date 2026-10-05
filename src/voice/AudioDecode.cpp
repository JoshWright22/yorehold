// miniaudio's decoders and resampler, with stb_vorbis for OGG. Its own file, built without our
// warning level, so their macros stay out of everything else.

#include "voice/AudioDecode.h"

#define STB_VORBIS_HEADER_ONLY
#include "extras/stb_vorbis.c"

#define MA_NO_DEVICE_IO
#define MA_NO_ENGINE
#define MA_NO_NODE_GRAPH
#define MA_NO_RESOURCE_MANAGER
#define MA_NO_GENERATION
#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#undef STB_VORBIS_HEADER_ONLY
#include "extras/stb_vorbis.c"

std::optional<std::vector<float>> decodeVoiceAudio(std::span<const unsigned char> bytes, std::string* error)
{
    ma_decoder_config config = ma_decoder_config_init(ma_format_f32, 1, voiceSampleRate);
    ma_decoder decoder;
    if (bytes.empty() || ma_decoder_init_memory(bytes.data(), bytes.size(), &config, &decoder) != MA_SUCCESS)
    {
        if (error)
            *error = "not a recording miniaudio can read (WAV, OGG, MP3 or FLAC)";
        return std::nullopt;
    }
    std::vector<float> samples;
    float chunk[4096];
    for (;;)
    {
        ma_uint64 read = 0;
        const ma_result result = ma_decoder_read_pcm_frames(&decoder, chunk, 4096, &read);
        samples.insert(samples.end(), chunk, chunk + read);
        if (result != MA_SUCCESS || read == 0)
            break;
    }
    ma_decoder_uninit(&decoder);
    if (samples.empty())
    {
        if (error)
            *error = "the recording is empty";
        return std::nullopt;
    }
    return samples;
}
