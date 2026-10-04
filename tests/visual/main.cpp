#include "YoreholdGame.h"

#include <yorehold/framework/testing/TestBrowser.h>

#include <SDL3/SDL.h>
#include <SDL3/SDL_main.h>

// Runs the whole game inside the browser, so client screens can be checked next to future gameplay scenes.
class TestSceneGame : public yh::TestScene
{
public:
    TestSceneGame() { game_.load(); }

    void update(double deltaSeconds) override { game_.update(deltaSeconds); }
    void draw(yh::Renderer& renderer) override { game_.draw(renderer); }
    bool handleEvent(const SDL_Event& event) override { return game_.handleEvent(event); }

private:
    YoreholdGame game_;
};

class ReactionGame : public YoreholdGame
{
public:
    void prepare()
    {
        load();
        SDL_Event enter{};
        enter.type = SDL_EVENT_KEY_DOWN;
        enter.key.key = SDLK_RETURN;
        handleEvent(enter);
        handleEvent(enter);
        newAdventure(7);
        setOptions({0, 0, true, true});
        if (!chapter_ || heroCount_ >= creatures_.size()) return;
        const size_t enemy = heroCount_;
        // An open strip of three squares, apart from the chapter's normal placements.
        yh::Cell at{1, 1};
        for (int y = 1; y < map().height() - 1; y++)
        {
            bool found = false;
            for (int x = 1; x < map().width() - 3; x++)
                if (walkable({x, y}) && walkable({x + 1, y}) && walkable({x + 2, y})
                    && !occupied({x, y}, 0) && !occupied({x + 1, y}, enemy) && !occupied({x + 2, y}, enemy))
                {
                    at = {x, y};
                    found = true;
                    break;
                }
            if (found) break;
        }
        creatures_[0].sheet.stats.setBase("dex", 1000);
        creatures_[enemy].sheet.stats.setBase("dex", 900);
        tokens_.tokens[0].position = grid_.center(at);
        tokens_.tokens[enemy].position = grid_.center({at.x + 1, at.y});
        startCombat(creatures_[enemy].group);
        while (currentCreature() && currentCreature() != enemy)
            endTurn();
        World::act("step", nlohmann::json{{"at", {at.x + 2, at.y}}}.dump());
    }
};

// Time is held so the two-second prompt can be inspected and clicked in the browser.
class TestSceneReactionPrompt : public yh::TestScene
{
public:
    TestSceneReactionPrompt() { game_.prepare(); }
    void update(double) override { game_.update(0); }
    void draw(yh::Renderer& renderer) override { game_.draw(renderer); }
    bool handleEvent(const SDL_Event& event) override { return game_.handleEvent(event); }
private:
    ReactionGame game_;
};

int main(int argc, char** argv)
{
    yh::TestBrowser browser;
    browser.add<TestSceneGame>("Game");
    browser.add<TestSceneReactionPrompt>("Reaction prompt");

    yh::HostSettings settings;
    settings.title = "yorehold tests";
    settings.feedbackDir = YH_FEEDBACK_DIR;
    settings.stateDir = YH_DEV_STATE_DIR;
    return browser.run(argc, argv, settings);
}
