#pragma once

#include <optional>
#include <string>
#include <string_view>
#include <vector>

// A recorded line as words with timings: `voice/<name>.voice.json` beside the recording. Made once
// in Create when the recording is imported; the game only reads it.

struct VoiceWord
{
    std::string text;      // as written, punctuation kept
    double start = 0;      // seconds into the recording
    double end = 0;
    float confidence = 1;  // lowest token probability whisper gave it; 1 for a word it didn't hear
    bool matched = true;   // its timing was heard; false = spread between its neighbours
};

struct VoiceLine
{
    static constexpr int format = 1;
    std::string audio;  // the recording's file name, beside this file
    std::string model;  // "base.en", "tiny.en"
    std::string text;   // the line the words make up
    std::vector<VoiceWord> words;

    // A newer format, or a missing or broken field, is refused with the reason.
    static std::optional<VoiceLine> fromJson(std::string_view json, std::string* error = nullptr);
    std::string toJson() const;

    // Made on demand, never stored.
    std::string toText() const;
    std::string toSrt() const;
    std::string toVtt() const;
    // Words below the cutoff, for the editor to flag.
    int lowConfidence(float cutoff) const;
};

// What whisper heard, one token at a time. Times are in seconds; `dtw` is negative when there is none.
struct HeardToken
{
    std::string text; // a leading space starts a new word
    float probability = 1;
    double start = 0;
    double end = 0;
    double dtw = -1;
};

// "Kharos," -> "kharos". Letters and digits only, lower case; apostrophes inside a word stay.
std::string normalizeWord(std::string_view word);
// Words as written, split on spaces, punctuation kept on them. Empty ones are dropped.
std::vector<std::string> splitWords(std::string_view text);

// Tokens of one segment into words. DTW puts a token about where it ends, so a word starts at the
// DTW time of the token before it (measured: a third of the error of its own token's, and without
// the long misses of whisper's older token times), else at its first token's start. It ends where
// the next word starts, the last one at `segmentEnd`. Its confidence is the lowest probability
// among its tokens.
std::vector<VoiceWord> joinTokens(const std::vector<HeardToken>& tokens, double segmentEnd);

// The written line wins: its words get the timings of the heard words they line up with, by edit
// distance over normalized words. A written word with no close heard word gets a time spread
// between its matched neighbours and `matched = false`. An empty `written` keeps the heard words
// as they are, as a suggestion. `length` is the recording's length in seconds.
std::vector<VoiceWord> matchWords(const std::vector<VoiceWord>& heard, std::string_view written, double length);
