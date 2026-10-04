#pragma once

#include "sim/World.h"

#include <yorehold/framework/assets/FileSystem.h>

#include <functional>
#include <string>
#include <string_view>
#include <vector>

// The files a WorldFixture reads; a base so they exist before the World that keeps a reference.
struct WorldFixtureFiles
{
    yh::FileSystem files;
};

// A World for unit tests, with no window: load a chapter, send intents, step time, read state.
// Reading goes through World's own accessors (creatures(), encounter(), flags(), ...).
class WorldFixture : private WorldFixtureFiles, public World
{
public:
    // Mounts the framework's and the game's files, as the game does.
    WorldFixture();

    // Loads the chapter in `folder` of those files and starts it with `seed`.
    bool load(const std::string& folder, uint64_t seed, std::string* error = nullptr);

    // Sends an intent as player 0, the way a click or a key would. False if the world refused it
    // (`refusal` then says why, when the world gives a reason).
    bool send(std::string_view type, const nlohmann::json& data = nlohmann::json::object());
    std::string refusal;

    // Moves time on in frames of 1/60 s, as the game would.
    void step(double seconds);
    // Steps until `done` holds or `limitSeconds` have passed; true if it held.
    bool stepUntil(const std::function<bool()>& done, double limitSeconds);

    // What the world has said so far, and whether it asked for its ending.
    std::vector<std::string> log;
    bool ended = false;
    bool said(std::string_view text) const;

private:
    void takeEventsIntoLog();
};
