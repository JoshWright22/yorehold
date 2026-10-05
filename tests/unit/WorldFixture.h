#pragma once

#include "sim/World.h"

#include <yorehold/framework/assets/FileSystem.h>

#include <filesystem>
#include <functional>
#include <map>
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
    ~WorldFixture() override;

    // Loads the chapter in `folder` of those files and starts it with `seed`.
    bool load(const std::string& folder, uint64_t seed, std::string* error = nullptr);
    // A chapter written in the test itself: `contents` maps paths (like "chapters/t/chapter.json")
    // to their text. They go in a scratch folder mounted over the game's files, so a test chapter
    // can use the game's creatures, classes and art.
    bool loadJson(const std::string& folder, const std::map<std::string, std::string>& contents, uint64_t seed, std::string* error = nullptr);

    // Sends an intent as player 0, the way a click or a key would. False if the world refused it
    // (`refusal` then says why, when the world gives a reason).
    bool send(std::string_view type, const nlohmann::json& data = nlohmann::json::object());
    std::string refusal;

    // Moves time on in frames of 1/60 s, as the game would.
    void step(double seconds);
    // Steps until `done` holds or `limitSeconds` have passed; true if it held.
    bool stepUntil(const std::function<bool()>& done, double limitSeconds);

    // Every rest can be taken anywhere and costs no supplies, for tests about what a rest does
    // rather than where it is taken.
    void freeRests()
    {
        for (yh::RestDefinition& rest : rules_.rests)
        {
            rest.campOnly = false;
            rest.supplyCost = 0;
        }
    }

    // A creature's sheet, to set a situation up (wounds, conditions) before sending intents at it.
    yh::Character& sheet(size_t creature) { return creatures_[creature].sheet; }

    // What the world has said so far, and whether it asked for its ending.
    std::vector<std::string> log;
    bool ended = false;
    bool said(std::string_view text) const;

private:
    void takeEventsIntoLog();
    std::filesystem::path scratch_; // loadJson's files
};
