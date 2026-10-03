#include "FileAssociation.h"
#include "YoreholdGame.h"

#include <SDL3/SDL_main.h>
#include <SDL3/SDL_stdinc.h>

#include <string>
#include <vector>

int main(int argc, char** argv)
{
    yh::HostSettings settings;
    settings.feedbackDir = YH_FEEDBACK_DIR;
    settings.stateDir = YH_DEV_STATE_DIR;
    settings = yh::parseHostArgs(argc, argv, settings);

    // Double-clicking a .yore starts the game with its path; it's added to the library.
    std::vector<std::string> openFiles;
    for (int i = 1; i < argc; i++)
        if (std::string(argv[i]).ends_with(".yore"))
            openFiles.emplace_back(argv[i]);
    // Scripted and redirected runs leave the player's file types alone.
    if (!settings.hidden && !SDL_getenv("YOREHOLD_SEED") && !SDL_getenv("YOREHOLD_SAVE_DIR"))
        registerYoreFiles();

    YoreholdGame game(std::move(openFiles));
    return yh::run(game, settings);
}
