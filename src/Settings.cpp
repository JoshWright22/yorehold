// The player's settings file, and skins: the look the game is drawn with.

#include "YoreholdGame.h"

#include <yorehold/framework/save/SaveFile.h>

#include <nlohmann/json.hpp>

#include <SDL3/SDL_stdinc.h>
#include <SDL3/SDL_video.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <filesystem>
#include <fstream>

// ---------------------------------------------------------------- skins

// A skin is a folder (or a .yoreskin zip of one) in the skins folder, laid out like the game's
// own assets: ui/button.png, ui/theme.json, fonts/... It only needs the files it changes;
// everything else comes from the defaults underneath.
std::string YoreholdGame::skinsDir() const
{
    const std::string dir = stateDir();
    return dir.empty() ? std::string() : dir + "skins";
}

void YoreholdGame::refreshSkins()
{
    skins_.clear();
    std::error_code error;
    const std::filesystem::path dir(skinsDir());
    if (skinsDir().empty() || !std::filesystem::is_directory(dir, error))
        return;
    for (const auto& entry : std::filesystem::directory_iterator(dir, error))
    {
        if (entry.is_directory(error) || entry.path().extension() == ".yoreskin")
            skins_.push_back(entry.path().filename().string());
    }
    std::sort(skins_.begin(), skins_.end());
}

// Puts the selected skin on top of everything else mounted, and drops cached art so it shows.
void YoreholdGame::mountSkin()
{
    releaseAssets();
    files_.unmount("skin");
    if (settings_.skin.empty() || skinsDir().empty())
        return;
    const std::filesystem::path path = std::filesystem::path(skinsDir()) / settings_.skin;
    std::error_code error;
    const bool mounted = std::filesystem::is_directory(path, error) ? files_.mountFolder(path.string(), "skin")
                       : std::filesystem::is_regular_file(path, error) && files_.mountZip(path.string(), "skin");
    if (!mounted)
        std::fprintf(stderr, "Skin \"%s\" couldn't be opened; using the default.\n", settings_.skin.c_str());
    // Looks and sounds only: a skin can't replace chapters, creatures or rules.
    files_.restrict("skin", {"ui", "fonts", "tokens", "particles", "sounds", "music"});
}

// The first time, the skins folder gets a copy of the default UI to start a skin from.
void YoreholdGame::prepareSkinsFolder()
{
    std::error_code error;
    const std::filesystem::path dir(skinsDir());
    if (skinsDir().empty() || std::filesystem::exists(dir, error))
        return;
    const std::filesystem::path copy = dir / "Default copy" / "ui";
    std::filesystem::create_directories(copy, error);
    for (const std::string& file : files_.list("ui"))
    {
        const auto bytes = files_.read(file);
        std::ofstream out(copy / std::filesystem::path(file).filename(), std::ios::binary);
        if (bytes && out)
            out.write(reinterpret_cast<const char*>(bytes->data()), static_cast<std::streamsize>(bytes->size()));
    }
    yh::writeFileAtomically((dir / "readme.txt").string(),
        "Yorehold skins\n"
        "==============\n\n"
        "Each folder in here is a skin (a .yoreskin file, which is a zip of such a folder, works too).\n"
        "Pick one in Settings > Skin. A skin only needs the files it changes; anything missing comes\n"
        "from the default look.\n\n"
        "\"Default copy\" is the default UI to start from: copy the folder, rename it, and edit the images.\n"
        "Changes show in the game as soon as you save a file.\n\n"
        "ui/panel.png                 panels and windows\n"
        "ui/button.png                buttons, plus -hover, -pressed and -disabled\n"
        "ui/button-selected.png       drawn over the chosen option in a row of options\n"
        "ui/checkbox-off.png, -on     the tick box at the left of a setting\n"
        "ui/textbox.png, -focus       text fields\n"
        "ui/bar-back.png, bar-fill    health bars and sliders (the fill is tinted, so draw it in white and greys)\n"
        "ui/slider-knob.png           the slider handle\n"
        "ui/theme.json                text colours, the drop shadow, and \"slice\": how many pixels at each\n"
        "                             image's edge are corners that don't stretch\n"
        "fonts/                       replace a font by giving a file the same name as the game's\n",
        false);
}

// ---------------------------------------------------------------- settings

void YoreholdGame::applySettings()
{
    applyScheme(settings_.controls);
    yh::CameraControls& controls = play_.cameraControls();
    controls.settings.zoomToCursor = settings_.zoomToCursor;
    controls.settings.edgeScroll = settings_.edgeScroll;
    controls.settings.followSelection = settings_.cameraFollows;
    controls.settings.keyPanSpeed = settings_.panSpeed;
    controls.settings.edgeScrollSpeed = settings_.panSpeed;
    setOptions({settings_.lighting, settings_.timeOfDay, settings_.sharedFog});
    int count = 0;
    if (SDL_Window** windows = SDL_GetWindows(&count))
    {
        if (count > 0)
            SDL_SetWindowFullscreen(windows[0], settings_.fullscreen);
        SDL_free(windows);
    }
}

void YoreholdGame::saveSettings() const
{
    if (testRun_ || stateDir().empty())
        return;
    const nlohmann::json j{
        {"controls", settings_.controls == yh::ControlPreset::Foundry ? "foundry" : "bg3"},
        {"zoomToCursor", settings_.zoomToCursor},
        {"edgeScroll", settings_.edgeScroll},
        {"cameraFollows", settings_.cameraFollows},
        {"panSpeed", settings_.panSpeed},
        {"fullscreen", settings_.fullscreen},
        {"lighting", std::array<const char*, 4>{"map", "off", "mood", "rules"}[std::clamp(settings_.lighting, 0, 3)]},
        {"timeOfDay", std::array<const char*, 4>{"map", "day", "dusk", "night"}[std::clamp(settings_.timeOfDay, 0, 3)]},
        {"sharedFog", settings_.sharedFog},
        {"playerName", settings_.playerName},
        {"joinAddress", settings_.joinAddress},
        {"lastPackage", settings_.lastPackage},
        {"lastFolder", settings_.lastFolder},
        {"skin", settings_.skin},
        {"server", settings_.server},
        {"serverKey", settings_.serverKey},
        {"deviceId", settings_.deviceId},
    };
    yh::writeFileAtomically(stateDir() + "settings.json", j.dump(2), false);
}

void YoreholdGame::loadSettings()
{
    const std::optional<std::string> text = yh::readTextFile(stateDir() + "settings.json");
    if (!text)
        return;
    // Unknown or broken values keep their defaults; settings never block the game from starting.
    const nlohmann::json j = nlohmann::json::parse(*text, nullptr, false);
    if (!j.is_object())
        return;
    try
    {
        Settings s;
        s.controls = j.value("controls", std::string("bg3")) == "foundry" ? yh::ControlPreset::Foundry : yh::ControlPreset::BG3;
        s.zoomToCursor = j.value("zoomToCursor", s.zoomToCursor);
        s.edgeScroll = j.value("edgeScroll", s.edgeScroll);
        s.cameraFollows = j.value("cameraFollows", s.cameraFollows);
        s.panSpeed = j.value("panSpeed", s.panSpeed);
        s.panSpeed = std::isfinite(s.panSpeed) ? std::clamp(s.panSpeed, 200.0f, 3000.0f) : Settings{}.panSpeed;
        s.fullscreen = j.value("fullscreen", s.fullscreen);
        const std::string lighting = j.value("lighting", std::string("map"));
        s.lighting = lighting == "off" ? 1 : lighting == "mood" ? 2 : lighting == "rules" ? 3 : 0;
        const std::string time = j.value("timeOfDay", std::string("map"));
        s.timeOfDay = time == "day" ? 1 : time == "dusk" ? 2 : time == "night" ? 3 : 0;
        s.sharedFog = j.value("sharedFog", s.sharedFog);
        s.playerName = j.value("playerName", s.playerName).substr(0, 32);
        s.joinAddress = j.value("joinAddress", s.joinAddress).substr(0, 253);
        s.lastPackage = j.value("lastPackage", s.lastPackage);
        s.lastFolder = j.value("lastFolder", s.lastFolder);
        s.skin = j.value("skin", s.skin).substr(0, 200);
        s.server = j.value("server", s.server).substr(0, 253);
        s.serverKey = j.value("serverKey", s.serverKey).substr(0, 128);
        s.deviceId = j.value("deviceId", s.deviceId).substr(0, 128);
        settings_ = s;
    }
    catch (const nlohmann::json::exception&)
    {
        // A value of the wrong type: keep the defaults.
    }
}
