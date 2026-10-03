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

int main(int argc, char** argv)
{
    yh::TestBrowser browser;
    browser.add<TestSceneGame>("Game");

    yh::HostSettings settings;
    settings.title = "yorehold tests";
    settings.feedbackDir = YH_FEEDBACK_DIR;
    settings.stateDir = YH_DEV_STATE_DIR;
    return browser.run(argc, argv, settings);
}
