#include "WorldFixture.h"

#include <algorithm>

WorldFixture::WorldFixture() : World(files)
{
    files.mountFolder(YH_FRAMEWORK_ASSETS, "framework");
    files.mountFolder(YH_GAME_ASSETS, "game");
    saves_ = false;
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
            ended = true;
    }
}
