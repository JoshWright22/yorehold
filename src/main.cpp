#include "YoreholdGame.h"

#include <SDL3/SDL_main.h>

int main(int argc, char** argv)
{
    yh::HostSettings settings;
    settings.feedbackDir = YH_FEEDBACK_DIR;
    settings.stateDir = YH_DEV_STATE_DIR;

    YoreholdGame game;
    return yh::run(game, yh::parseHostArgs(argc, argv, settings));
}
