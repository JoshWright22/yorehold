// yorehold-voice: voice lines without opening Create.
//   yorehold-voice <package folder> [--model <file>] [--again]
//       Imports the recordings in the package's voice/ folder that have no voice file yet (all of
//       them with --again), each matched to the written line of the node it is named after.
//   yorehold-voice measure <folder> [--model <file>]...
//       For every <name>.wav with a <name>.answer.json beside it (tests/voice/make-lines.ps1 makes
//       them): how many words each model hears right and how far its word starts are off.

#include "screens/VoiceImporter.h"
#include "voice/AudioDecode.h"
#include "voice/VoiceLine.h"
#include "voice/VoiceTranscriber.h"

#include <yorehold/framework/save/SaveFile.h>

#include <nlohmann/json.hpp>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <map>
#include <sstream>
#include <string>
#include <vector>

namespace fs = std::filesystem;

namespace
{

std::optional<std::string> readFile(const fs::path& path)
{
    std::ifstream in(path, std::ios::binary);
    if (!in)
        return std::nullopt;
    std::ostringstream text;
    text << in.rdbuf();
    return text.str();
}

// Words changed, added or left out to turn one list into the other.
size_t wordDistance(const std::vector<std::string>& a, const std::vector<std::string>& b)
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

std::vector<std::string> normalizedWords(const std::vector<std::string>& words)
{
    std::vector<std::string> out;
    for (const std::string& word : words)
        if (std::string n = normalizeWord(word); !n.empty())
            out.push_back(std::move(n));
    return out;
}

std::unique_ptr<VoiceTranscriber> load(const std::string& model)
{
    std::string error;
    std::unique_ptr<VoiceTranscriber> transcriber = loadWhisper(model.empty() ? findVoiceModel() : model, &error);
    if (!transcriber)
        std::fprintf(stderr, "%s\n", error.c_str());
    return transcriber;
}

// The written lines of every conversation in the package, by voice name ("wren.hello").
std::map<std::string, std::string> writtenLines(const fs::path& package)
{
    std::map<std::string, std::string> lines;
    std::error_code problem;
    for (auto it = fs::recursive_directory_iterator(package, problem); it != fs::recursive_directory_iterator(); it.increment(problem))
    {
        if (problem || it->path().extension() != ".json")
            continue;
        const std::optional<std::string> text = readFile(it->path());
        const nlohmann::json j = text ? nlohmann::json::parse(*text, nullptr, false) : nlohmann::json();
        if (!j.is_object() || !j.contains("id") || !j["id"].is_string() || !j.contains("nodes") || !j["nodes"].is_array())
            continue;
        for (const nlohmann::json& node : j["nodes"])
            if (node.is_object() && node.contains("id") && node["id"].is_string())
                lines[VoiceImporter::stem(j["id"].get<std::string>(), node["id"].get<std::string>())] = node.value("text", std::string());
    }
    return lines;
}

int importPackage(const fs::path& package, const std::string& model, bool again)
{
    const fs::path folder = package / "voice";
    std::error_code problem;
    if (!fs::is_directory(folder, problem))
    {
        std::fprintf(stderr, "%s has no voice folder\n", package.string().c_str());
        return 1;
    }
    std::unique_ptr<VoiceTranscriber> transcriber = load(model);
    if (!transcriber)
        return 1;
    const std::map<std::string, std::string> lines = writtenLines(package);
    yh::FileSystem files;
    files.mountFolder(package.string(), "package");
    const std::vector<std::string> vocabulary = VoiceImporter::vocabulary(files);
    const VoiceImporter::Settings settings;
    int failed = 0, done = 0;
    for (const fs::directory_entry& entry : fs::directory_iterator(folder, problem))
    {
        const std::string extension = entry.path().extension().string();
        if (extension.size() < 2 || std::find(settings.extensions.begin(), settings.extensions.end(), extension.substr(1)) == settings.extensions.end())
            continue;
        const std::string stem = entry.path().stem().string();
        const fs::path out = folder / (stem + ".voice.json");
        if (!again && fs::exists(out, problem))
            continue;
        const auto written = lines.find(stem);
        const std::optional<std::string> bytes = readFile(entry.path());
        std::string error;
        std::optional<VoiceLine> line;
        if (bytes)
            line = transcribeVoiceLine({reinterpret_cast<const unsigned char*>(bytes->data()), bytes->size()}, entry.path().filename().string(),
                written == lines.end() ? std::string() : written->second, vocabulary, *transcriber, &error);
        else
            error = "can't be read";
        if (!line || !yh::writeFileAtomically(out.string(), line->toJson(), false, &error))
        {
            std::fprintf(stderr, "%s: %s\n", stem.c_str(), error.c_str());
            failed++;
            continue;
        }
        const auto guessed = std::count_if(line->words.begin(), line->words.end(), [](const VoiceWord& w) { return !w.matched; });
        std::printf("%s: %zu words, %d unsure, %d guessed%s\n", stem.c_str(), line->words.size(), line->lowConfidence(settings.flagBelow),
            static_cast<int>(guessed), written == lines.end() ? " (no node by that name: kept what was heard)" : "");
        done++;
    }
    std::printf("%d imported, %d failed\n", done, failed);
    return failed == 0 ? 0 : 1;
}

struct Answer
{
    std::string name, text;
    std::vector<std::pair<std::string, double>> words; // as spoken, with the time each starts
    std::vector<unsigned char> recording;
};

int measure(const fs::path& folder, std::vector<std::string> models)
{
    std::vector<Answer> answers;
    std::error_code problem;
    for (const fs::directory_entry& entry : fs::directory_iterator(folder, problem))
    {
        if (entry.path().extension() != ".wav")
            continue;
        const std::optional<std::string> text = readFile(folder / (entry.path().stem().string() + ".answer.json"));
        const std::optional<std::string> bytes = readFile(entry.path());
        const nlohmann::json j = text ? nlohmann::json::parse(*text, nullptr, false) : nlohmann::json();
        if (!bytes || !j.is_object() || !j.contains("words"))
            continue;
        Answer answer{entry.path().stem().string(), j.value("text", std::string()), {}, {bytes->begin(), bytes->end()}};
        for (const nlohmann::json& w : j["words"])
            answer.words.push_back({w.value("text", std::string()), w.value("start", 0.0)});
        answers.push_back(std::move(answer));
    }
    if (answers.empty())
    {
        std::fprintf(stderr, "no <name>.wav with a <name>.answer.json in %s\n", folder.string().c_str());
        return 1;
    }
    std::sort(answers.begin(), answers.end(), [](const Answer& a, const Answer& b) { return a.name < b.name; });
    if (models.empty())
        models.push_back({});
    // The names in vocabulary.txt beside the lines, as an import would give them.
    std::string prompt;
    if (const std::optional<std::string> names = readFile(folder / "vocabulary.txt"))
    {
        yh::FileSystem files;
        files.mountFolder(folder.string(), "lines");
        for (const std::string& name : VoiceImporter::vocabulary(files))
            prompt += (prompt.empty() ? "" : ", ") + name;
        if (!prompt.empty())
            prompt += ".";
    }

    for (const std::string& model : models)
    {
        std::unique_ptr<VoiceTranscriber> transcriber = load(model);
        if (!transcriber)
            return 1;
        size_t words = 0, wrong = 0, guessed = 0;
        double audio = 0, spent = 0, late = 0;
        std::vector<double> errors, endErrors;
        for (const Answer& answer : answers)
        {
            const std::optional<std::vector<float>> samples = decodeVoiceAudio(answer.recording);
            if (!samples)
                continue;
            const double length = static_cast<double>(samples->size()) / voiceSampleRate;
            const auto began = std::chrono::steady_clock::now();
            std::string error;
            const std::optional<std::vector<VoiceWord>> heard = transcriber->transcribe(*samples, prompt, &error);
            spent += std::chrono::duration<double>(std::chrono::steady_clock::now() - began).count();
            audio += length;
            if (!heard)
            {
                std::fprintf(stderr, "%s: %s\n", answer.name.c_str(), error.c_str());
                continue;
            }
            std::vector<std::string> heardText;
            for (const VoiceWord& w : *heard)
                heardText.push_back(w.text);
            const std::vector<std::string> expected = normalizedWords(splitWords(answer.text));
            const size_t missed = wordDistance(expected, normalizedWords(heardText));
            words += expected.size();
            wrong += missed;
            // Timing: the written words, matched, against where the synthesizer said each one starts.
            const std::vector<VoiceWord> matched = matchWords(*heard, answer.text, length);
            double lineError = 0;
            int lineCount = 0;
            for (size_t i = 0; i < matched.size() && i < answer.words.size(); i++)
            {
                if (!matched[i].matched)
                {
                    guessed++;
                    continue;
                }
                if (answer.words[i].second < 0) // the synthesizer didn't say where this one starts
                    continue;
                late += matched[i].start - answer.words[i].second;
                const double off = std::abs(matched[i].start - answer.words[i].second);
                errors.push_back(off);
                lineError += off;
                lineCount++;
                // A word ends about where the next one starts.
                if (i + 1 < answer.words.size() && answer.words[i + 1].second >= 0)
                    endErrors.push_back(std::abs(matched[i].end - answer.words[i + 1].second));
            }
            std::printf("  %-16s %zu/%zu words heard right, start off by %.0f ms on average\n", answer.name.c_str(), expected.size() - std::min(missed, expected.size()),
                expected.size(), lineCount ? lineError / lineCount * 1000 : 0.0);
        }
        std::sort(errors.begin(), errors.end());
        std::sort(endErrors.begin(), endErrors.end());
        auto at = [](const std::vector<double>& sorted, double part) { return sorted.empty() ? 0.0 : sorted[std::min(sorted.size() - 1, static_cast<size_t>(part * static_cast<double>(sorted.size())))] * 1000; };
        double mean = 0;
        for (const double e : errors)
            mean += e;
        mean = errors.empty() ? 0 : mean / static_cast<double>(errors.size()) * 1000;
        std::printf("%s: %zu lines, %.1f s of speech\n", transcriber->model().c_str(), answers.size(), audio);
        std::printf("  word error rate %.1f%% (%zu of %zu words)\n", words ? 100.0 * static_cast<double>(wrong) / static_cast<double>(words) : 0.0, wrong, words);
        std::printf("  word start error: mean %.0f ms, median %.0f ms, 90%% under %.0f ms, worst %.0f ms (%zu words, %zu timed by guess)\n", mean,
            at(errors, 0.5), at(errors, 0.9), at(errors, 1.0), errors.size(), guessed);
        // Above zero: the model puts words later than they are, on average.
        std::printf("  starts late by %.0f ms on average\n", errors.empty() ? 0.0 : late / static_cast<double>(errors.size()) * 1000);
        std::printf("  word end error: median %.0f ms, 90%% under %.0f ms\n", at(endErrors, 0.5), at(endErrors, 0.9));
        std::printf("  %.2f s to listen, %.2f s per second of speech\n\n", spent, audio > 0 ? spent / audio : 0.0);
    }
    return 0;
}

}

int main(int argc, char** argv)
{
    std::vector<std::string> args(argv + 1, argv + argc);
    std::vector<std::string> models;
    bool again = false;
    std::vector<std::string> rest;
    for (size_t i = 0; i < args.size(); i++)
    {
        if (args[i] == "--model" && i + 1 < args.size())
            models.push_back(args[++i]);
        else if (args[i] == "--again")
            again = true;
        else
            rest.push_back(args[i]);
    }
    if (rest.size() == 2 && rest[0] == "measure")
        return measure(rest[1], models);
    if (rest.size() == 1 && models.size() <= 1)
        return importPackage(rest[0], models.empty() ? std::string() : models.front(), again);
    std::fprintf(stderr, "Usage: yorehold-voice <package folder> [--model <file>] [--again]\n"
        "       yorehold-voice measure <folder> [--model <file>]...\n");
    return 2;
}
