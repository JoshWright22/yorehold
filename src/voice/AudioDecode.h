#pragma once

#include <optional>
#include <span>
#include <string>
#include <vector>

// What whisper listens to: one channel at 16 kHz.
constexpr int voiceSampleRate = 16000;

// A recording (WAV, OGG Vorbis, MP3 or FLAC) mixed to mono and resampled to 16 kHz floats. The
// file itself is left as it is; it is what the game plays.
std::optional<std::vector<float>> decodeVoiceAudio(std::span<const unsigned char> bytes, std::string* error = nullptr);
