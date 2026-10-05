#include "voice/VoiceLine.h"

#include <nlohmann/json.hpp>

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <limits>

namespace
{

using nlohmann::ordered_json;

double round3(double seconds)
{
    return std::round(seconds * 1000.0) / 1000.0;
}

// Letters changed, added or taken away to turn one word into the other.
size_t letterDistance(std::string_view a, std::string_view b)
{
    std::vector<size_t> row(b.size() + 1);
    for (size_t j = 0; j <= b.size(); j++)
        row[j] = j;
    for (size_t i = 1; i <= a.size(); i++)
    {
        size_t diagonal = row[0];
        row[0] = i;
        for (size_t j = 1; j <= b.size(); j++)
        {
            const size_t above = row[j];
            row[j] = std::min({row[j] + 1, row[j - 1] + 1, diagonal + (a[i - 1] == b[j - 1] ? 0 : 1)});
            diagonal = above;
        }
    }
    return row[b.size()];
}

// 0 for the same word, up to 1 for nothing alike.
double wordCost(std::string_view a, std::string_view b)
{
    const size_t longest = std::max(a.size(), b.size());
    return longest == 0 ? 0.0 : static_cast<double>(letterDistance(a, b)) / static_cast<double>(longest);
}

// Close enough that the heard word is the written one, misheard ("Carlos" for "Kharos").
bool alike(std::string_view a, std::string_view b)
{
    return letterDistance(a, b) * 2 <= std::max(a.size(), b.size());
}

std::string clock(double seconds, char comma)
{
    const long long ms = std::llround(std::max(0.0, seconds) * 1000.0);
    char text[32];
    std::snprintf(text, sizeof text, "%02lld:%02lld:%02lld%c%03lld", ms / 3600000, ms / 60000 % 60, ms / 1000 % 60, comma, ms % 1000);
    return text;
}

// Subtitle cues: at most 8 words or about 42 letters, broken after a sentence when one ends.
std::vector<std::pair<size_t, size_t>> cues(const std::vector<VoiceWord>& words)
{
    std::vector<std::pair<size_t, size_t>> out;
    size_t first = 0, letters = 0;
    for (size_t i = 0; i < words.size(); i++)
    {
        letters += words[i].text.size() + 1;
        const char last = words[i].text.empty() ? ' ' : words[i].text.back();
        const bool sentence = (last == '.' || last == '?' || last == '!') && i + 1 - first >= 3;
        if (i + 1 == words.size() || i + 1 - first >= 8 || letters >= 42 || sentence)
        {
            out.push_back({first, i + 1});
            first = i + 1;
            letters = 0;
        }
    }
    return out;
}

std::string cueText(const std::vector<VoiceWord>& words, size_t from, size_t to)
{
    std::string text;
    for (size_t i = from; i < to; i++)
        text += (i == from ? "" : " ") + words[i].text;
    return text;
}

}

std::string normalizeWord(std::string_view word)
{
    std::string out;
    for (const char c : word)
    {
        const unsigned char u = static_cast<unsigned char>(c);
        if ((u >= 'a' && u <= 'z') || (u >= '0' && u <= '9') || u >= 0x80)
            out += c;
        else if (u >= 'A' && u <= 'Z')
            out += static_cast<char>(u - 'A' + 'a');
        else if (c == '\'' && !out.empty())
            out += c;
    }
    while (!out.empty() && out.back() == '\'')
        out.pop_back();
    return out;
}

std::vector<std::string> splitWords(std::string_view text)
{
    std::vector<std::string> out;
    std::string word;
    for (const char c : text)
    {
        if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
        {
            if (!word.empty())
                out.push_back(std::move(word));
            word.clear();
        }
        else
            word += c;
    }
    if (!word.empty())
        out.push_back(std::move(word));
    return out;
}

std::vector<VoiceWord> joinTokens(const std::vector<HeardToken>& tokens, double segmentEnd)
{
    std::vector<VoiceWord> words;
    double before = -1; // DTW time of the token before this one
    for (const HeardToken& token : tokens)
    {
        const double previous = before;
        before = token.dtw;
        const bool starts = !token.text.empty() && token.text.front() == ' ';
        std::string_view text = token.text;
        while (!text.empty() && text.front() == ' ')
            text.remove_prefix(1);
        if (text.empty())
            continue;
        // A token of punctuation alone ("!", " -") belongs to the word before it.
        const bool joins = !words.empty() && (!starts || normalizeWord(text).empty());
        if (joins)
        {
            words.back().text += text;
            words.back().confidence = std::min(words.back().confidence, token.probability);
            continue;
        }
        VoiceWord word;
        word.text = std::string(text);
        word.start = previous >= 0 ? previous : token.start;
        word.confidence = token.probability;
        words.push_back(std::move(word));
    }
    for (size_t i = 0; i < words.size(); i++)
    {
        words[i].end = i + 1 < words.size() ? words[i + 1].start : segmentEnd;
        words[i].end = std::max(words[i].end, words[i].start);
    }
    return words;
}

std::vector<VoiceWord> matchWords(const std::vector<VoiceWord>& heard, std::string_view written, double length)
{
    const std::vector<std::string> wordsWritten = splitWords(written);
    if (wordsWritten.empty())
        return heard;
    const size_t n = wordsWritten.size(), m = heard.size();
    std::vector<std::string> a(n), b(m);
    for (size_t i = 0; i < n; i++)
        a[i] = normalizeWord(wordsWritten[i]);
    for (size_t j = 0; j < m; j++)
        b[j] = normalizeWord(heard[j].text);

    // cost[i][j]: the first i written words lined up with the first j heard ones. Besides the
    // usual steps, one written word may take two heard ones, for a name heard in pieces.
    enum Step : unsigned char { Pair, Pair2, Skip, Drop };
    const double inf = std::numeric_limits<double>::infinity();
    std::vector<std::vector<double>> cost(n + 1, std::vector<double>(m + 1, inf));
    std::vector<std::vector<Step>> step(n + 1, std::vector<Step>(m + 1, Pair));
    cost[0][0] = 0;
    for (size_t i = 0; i <= n; i++)
        for (size_t j = 0; j <= m; j++)
        {
            auto offer = [&](double value, Step how) {
                if (value < cost[i][j])
                {
                    cost[i][j] = value;
                    step[i][j] = how;
                }
            };
            // Skips are offered first so a tie leaves out the later words: a recording that stops
            // before the written line does pairs its last word with the earlier one of the same.
            if (i > 0)
                offer(cost[i - 1][j] + 1, Skip); // written, not heard
            if (j > 0)
                offer(cost[i][j - 1] + 1, Drop); // heard, not written
            if (i > 0 && j > 0)
                offer(cost[i - 1][j - 1] + wordCost(a[i - 1], b[j - 1]), Pair);
            // Only when neither piece is the word by itself, so "uh the" stays "uh" left out and "the".
            if (i > 0 && j > 1 && !alike(a[i - 1], b[j - 2]) && !alike(a[i - 1], b[j - 1]))
                offer(cost[i - 1][j - 2] + wordCost(a[i - 1], b[j - 2] + b[j - 1]) + 0.25, Pair2);
        }

    std::vector<VoiceWord> out(n);
    std::vector<bool> timed(n, false);
    for (size_t i = n, j = m; i > 0 || j > 0;)
    {
        switch (step[i][j])
        {
        case Pair:
            out[i - 1] = heard[j - 1];
            out[i - 1].matched = alike(a[i - 1], b[j - 1]);
            timed[i - 1] = true;
            i--, j--;
            break;
        case Pair2:
            out[i - 1] = heard[j - 2];
            out[i - 1].end = heard[j - 1].end;
            out[i - 1].confidence = std::min(heard[j - 2].confidence, heard[j - 1].confidence);
            out[i - 1].matched = alike(a[i - 1], b[j - 2] + b[j - 1]);
            timed[i - 1] = true;
            i--, j -= 2;
            break;
        case Skip:
            i--;
            break;
        case Drop:
            j--;
            break;
        }
    }

    // Words nobody heard share the time between the heard ones around them, by their length.
    const double first = m > 0 ? heard.front().start : 0.0;
    const double last = std::max({m > 0 ? heard.back().end : 0.0, first, length});
    for (size_t i = 0; i < n;)
    {
        if (timed[i])
        {
            out[i].text = wordsWritten[i];
            i++;
            continue;
        }
        size_t to = i;
        while (to < n && !timed[to])
            to++;
        const double from = i > 0 ? out[i - 1].end : first;
        const double until = std::max(from, to < n ? out[to].start : last);
        size_t letters = 0;
        for (size_t k = i; k < to; k++)
            letters += std::max<size_t>(1, a[k].size());
        double at = from;
        for (size_t k = i; k < to; k++)
        {
            const double share = (until - from) * static_cast<double>(std::max<size_t>(1, a[k].size())) / static_cast<double>(letters);
            out[k] = VoiceWord{wordsWritten[k], at, at + share, 1.0f, false};
            at += share;
        }
        i = to;
    }
    return out;
}

std::optional<VoiceLine> VoiceLine::fromJson(std::string_view json, std::string* error)
{
    auto fail = [&](const std::string& why) -> std::optional<VoiceLine> {
        if (error)
            *error = why;
        return std::nullopt;
    };
    const nlohmann::json j = nlohmann::json::parse(json, nullptr, false);
    if (j.is_discarded() || !j.is_object())
        return fail("not a JSON object");
    if (!j.contains("format") || !j["format"].is_number_integer())
        return fail("no \"format\" number");
    const int version = j["format"].get<int>();
    if (version > format)
        return fail("made by a newer Yorehold (format " + std::to_string(version) + ", this one reads " + std::to_string(format) + ")");
    if (version < 1)
        return fail("format " + std::to_string(version) + " is not one Yorehold made");
    VoiceLine line;
    auto text = [&](const nlohmann::json& from, const char* key, std::string& into) {
        if (!from.contains(key))
            return true;
        if (!from[key].is_string())
            return false;
        into = from[key].get<std::string>();
        return true;
    };
    if (!text(j, "audio", line.audio) || !text(j, "model", line.model) || !text(j, "text", line.text))
        return fail("\"audio\", \"model\" and \"text\" are strings");
    if (!j.contains("words") || !j["words"].is_array())
        return fail("no \"words\" list");
    for (const nlohmann::json& w : j["words"])
    {
        const std::string at = "word " + std::to_string(line.words.size() + 1);
        if (!w.is_object() || !w.contains("text") || !w["text"].is_string())
            return fail(at + " has no text");
        if (!w.contains("start") || !w["start"].is_number() || !w.contains("end") || !w["end"].is_number())
            return fail(at + " needs a start and an end in seconds");
        VoiceWord word;
        word.text = w["text"].get<std::string>();
        word.start = w["start"].get<double>();
        word.end = w["end"].get<double>();
        if (word.start < 0 || word.end < word.start)
            return fail(at + " ends before it starts");
        if (w.contains("confidence"))
        {
            if (!w["confidence"].is_number())
                return fail(at + ": confidence is a number from 0 to 1");
            word.confidence = w["confidence"].get<float>();
        }
        if (w.contains("matched"))
        {
            if (!w["matched"].is_boolean())
                return fail(at + ": matched is true or false");
            word.matched = w["matched"].get<bool>();
        }
        line.words.push_back(std::move(word));
    }
    return line;
}

std::string VoiceLine::toJson() const
{
    ordered_json j;
    j["format"] = format;
    j["audio"] = audio;
    j["model"] = model;
    j["text"] = text;
    j["words"] = ordered_json::array();
    for (const VoiceWord& word : words)
        j["words"].push_back(ordered_json{
            {"text", word.text},
            {"start", round3(word.start)},
            {"end", round3(word.end)},
            {"confidence", std::round(word.confidence * 100.0) / 100.0},
            {"matched", word.matched},
        });
    return j.dump(2) + "\n";
}

std::string VoiceLine::toText() const
{
    return cueText(words, 0, words.size());
}

std::string VoiceLine::toSrt() const
{
    std::string out;
    int number = 1;
    for (const auto& [from, to] : cues(words))
        out += std::to_string(number++) + "\n" + clock(words[from].start, ',') + " --> " + clock(words[to - 1].end, ',') + "\n"
            + cueText(words, from, to) + "\n\n";
    return out;
}

std::string VoiceLine::toVtt() const
{
    std::string out = "WEBVTT\n\n";
    for (const auto& [from, to] : cues(words))
        out += clock(words[from].start, '.') + " --> " + clock(words[to - 1].end, '.') + "\n" + cueText(words, from, to) + "\n\n";
    return out;
}

int VoiceLine::lowConfidence(float cutoff) const
{
    return static_cast<int>(std::count_if(words.begin(), words.end(), [&](const VoiceWord& w) { return w.matched && w.confidence < cutoff; }));
}
