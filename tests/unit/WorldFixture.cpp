#include "WorldFixture.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <fstream>

WorldFixture::WorldFixture() : World(files)
{
    files.mountFolder(YH_FRAMEWORK_ASSETS, "framework");
    files.mountFolder(YH_GAME_ASSETS, "game");
    saves_ = false;
}

WorldFixture::~WorldFixture()
{
    files.unmount("fixture");
    std::error_code ignored;
    if (!scratch_.empty())
        std::filesystem::remove_all(scratch_, ignored);
}

bool WorldFixture::loadJson(const std::string& folder, const std::map<std::string, std::string>& contents, uint64_t seed, std::string* error)
{
    static std::atomic<int> made = 0;
    files.unmount("fixture");
    std::error_code problem;
    if (!scratch_.empty())
        std::filesystem::remove_all(scratch_, problem);
    scratch_ = std::filesystem::temp_directory_path() / ("yorehold-world-fixture-"
        + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()) + "-" + std::to_string(++made));
    for (const auto& [path, text] : contents)
    {
        const std::filesystem::path file = scratch_ / path;
        std::filesystem::create_directories(file.parent_path(), problem);
        std::ofstream out(file, std::ios::binary);
        out << text;
        if (!out)
        {
            if (error)
                *error = "couldn't write " + file.string();
            return false;
        }
    }
    if (!files.mountFolder(scratch_.string(), "fixture"))
    {
        if (error)
            *error = "couldn't mount " + scratch_.string();
        return false;
    }
    return load(folder, seed, error);
}

bool WorldFixture::load(const std::string& folder, uint64_t seed, std::string* error)
{
    log.clear();
    ended = false;
    if (!loadChapter(folder, error))
        return false;
    newAdventure(seed);
    takeEventsIntoLog();
    return true;
}

bool WorldFixture::send(std::string_view type, const nlohmann::json& data)
{
    refusal.clear();
    std::optional<std::string> accepted = validate(0, type, data.dump(), refusal);
    if (accepted)
        apply({0, 0, std::string(type), std::move(*accepted)});
    takeEventsIntoLog();
    return accepted.has_value();
}

void WorldFixture::step(double seconds)
{
    constexpr double frame = 1.0 / 60;
    for (double t = 0; t < seconds - 1e-9; t += frame)
    {
        update(frame);
        takeEventsIntoLog();
    }
}

bool WorldFixture::stepUntil(const std::function<bool()>& done, double limitSeconds)
{
    constexpr double frame = 1.0 / 60;
    for (double t = 0; t < limitSeconds; t += frame)
    {
        if (done())
            return true;
        step(frame);
    }
    return done();
}

bool WorldFixture::said(std::string_view text) const
{
    return std::any_of(log.begin(), log.end(), [&](const std::string& line) { return line.find(text) != std::string::npos; });
}

void WorldFixture::takeEventsIntoLog()
{
    for (Event& event : takeEvents())
    {
        if (event.kind == Event::Kind::Log)
            log.push_back(std::move(event.text));
        else if (event.kind == Event::Kind::Ending)
            ended |= !wiping();
    }
}
