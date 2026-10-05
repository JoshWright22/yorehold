// The title menus (play, adventures, join, create, settings) and the pause menu over a game.

#include "YoreholdGame.h"

#include <yorehold/framework/graphics/Renderer.h>

#include <SDL3/SDL_events.h>
#include <SDL3/SDL_misc.h>
#include <SDL3/SDL_stdinc.h>
#include <SDL3/SDL_timer.h>

#include <algorithm>
#include <array>
#include <cstdio>
#include <filesystem>

void YoreholdGame::openMenu(Menu menu)
{
    if (menu == Menu::Settings)
        settingsBack_ = menu_ == Menu::Pause ? Menu::Pause : Menu::Main;
    menu_ = menu;
}

void YoreholdGame::startNew()
{
    if (!chapter_)
        return;
    releaseCharacters();
    setParty({});
    newAdventure(SDL_GetTicks());
    menu_ = Menu::None;
    notice_.clear();
}

void YoreholdGame::continueSaved()
{
    if (!chapter_)
        return;
    menu_ = Menu::None;
    notice_.clear();
    if (!loadAdventure())
        say("No save to continue. Starting a new adventure.");
}

void YoreholdGame::drawMenu(yh::Renderer& renderer)
{
    ui_.begin(renderer, input_);
    const yh::Rect screen = renderer.bounds();
    const bool paused = menu_ == Menu::Pause || (menu_ == Menu::Settings && settingsBack_ == Menu::Pause);
    renderer.fillRect(screen, paused ? yh::Color{4, 4, 10, 150} : yh::Color{4, 4, 10, 200});
    if (title_)
    {
        title_->drawCentered(renderer, {0, screen.h * 0.12f, screen.w, 80}, paused ? "Paused" : "Yorehold", {255, 214, 140, 255});
        const bool characterScreen = menu_ == Menu::Characters || menu_ == Menu::NewCharacter || menu_ == Menu::LevelUp || menu_ == Menu::Party;
        if (!paused && !characterScreen && ui_.theme.font)
        {
            const std::string subtitle = chapter_ ? chapter_->title : "Content couldn't be loaded";
            ui_.label({screen.w / 2 - ui_.theme.font->measure(subtitle) / 2, screen.h * 0.12f + 88}, subtitle, ui_.theme.textDim);
        }
    }
    if (!paused && !online_.status().empty() && ui_.theme.font)
        ui_.label({screen.w - 16 - ui_.theme.font->measure(online_.status()), screen.h - 30}, online_.status(), ui_.theme.textDim);

    const float w = menu_ == Menu::Adventures ? 460.0f : 280.0f, h = 48, gap = 14, x = screen.w / 2 - w / 2;
    float y = screen.h * 0.36f;
    auto button = [&](std::string_view text, bool enabled = true) {
        const bool clicked = ui_.button({x, y, w, h}, text, enabled);
        y += h + gap;
        return clicked;
    };
    auto quitGame = [] {
        SDL_Event quit{};
        quit.type = SDL_EVENT_QUIT;
        SDL_PushEvent(&quit);
    };

    switch (menu_)
    {
    case Menu::None:
        break;
    case Menu::Main:
        if (button("Play (Enter)", chapter_ != nullptr))
            openMenu(Menu::Play);
        if (button("Create"))
            openMenu(Menu::Create);
        if (button("Settings"))
            openMenu(Menu::Settings);
        if (button("Exit"))
            quitGame();
        break;
    case Menu::Play:
        if (hasSave_ && button("Continue (Enter)", chapter_ != nullptr))
            continueSaved();
        if (button("New adventure", chapter_ != nullptr))
            openParty();
        // The chapter's ready-made party, no questions asked.
        if (button(hasSave_ ? "Quick start" : "Quick start (Enter)", chapter_ != nullptr))
            startNew();
        if (button("Characters"))
            openCharacters();
        if (button("Join co-op", chapter_ != nullptr))
            openMenu(Menu::Join);
        if (button("Adventures (" + std::to_string(adventures_.size()) + ")"))
            openMenu(Menu::Adventures);
        if (button("Back (Esc)"))
            openMenu(Menu::Main);
        break;
    case Menu::Join:
    {
        // The host picks Host co-op in their pause menu; both need the same adventure selected.
        const yh::Rect panel{x - 110, y, w + 220, 214};
        ui_.panel(panel);
        ui_.label({panel.x + 20, panel.y + 16}, "Join a friend's game: " + (chapter_ ? chapter_->title : std::string()), ui_.theme.accent);
        ui_.label({panel.x + 20, panel.y + 58}, "Host address");
        ui_.textBox("join-address", {panel.x + 180, panel.y + 50, panel.w - 200, 40}, settings_.joinAddress, 253);
        ui_.label({panel.x + 20, panel.y + 112}, "Your name");
        ui_.textBox("join-name", {panel.x + 180, panel.y + 104, panel.w - 200, 40}, settings_.playerName, 32);
        ui_.label({panel.x + 20, panel.y + 164}, client_ ? "Connecting..." : "The host's port is " + std::to_string(coopPort()) + ". Same adventure on both sides.",
            ui_.theme.textDim);
        y += panel.h + gap;
        if (button(client_ ? "Cancel" : "Join", chapter_ != nullptr))
        {
            if (client_)
                endSession("Cancelled.");
            else
            {
                saveSettings();
                joinSession(settings_.joinAddress);
            }
        }
        if (button("Back (Esc)"))
        {
            if (client_)
                endSession("Cancelled.");
            openMenu(Menu::Play);
        }
        break;
    }
    case Menu::Adventures:
    {
        // Five to a page; the last button turns the page when there are more.
        const size_t perPage = 5, pages = (adventures_.size() + perPage - 1) / perPage;
        if (adventurePage_ >= pages)
            adventurePage_ = 0;
        std::optional<size_t> picked;
        for (size_t i = adventurePage_ * perPage; i < adventures_.size() && i < (adventurePage_ + 1) * perPage; i++)
        {
            const ContentLibrary::Adventure& a = adventures_[i];
            std::string text = a.title;
            if (!a.packageName.empty() && a.packageName != a.title)
                text += "  (" + a.packageName + ")";
            if (ui_.toggle({x, y, w, h}, text, i == adventure_ && chapter_ != nullptr))
                picked = i;
            y += h + gap;
        }
        if (pages > 1 && button("More (" + std::to_string(adventurePage_ + 1) + "/" + std::to_string(pages) + ")"))
            adventurePage_ = (adventurePage_ + 1) % pages;
        // Installed files with nothing to play (classes, items, creatures only).
        for (const ContentLibrary::Package& package : packages_)
            if (package.adventures.empty())
            {
                ui_.label({x, y}, package.name + ": " + std::to_string(package.classes) + " classes, " + std::to_string(package.items)
                    + " items, " + std::to_string(package.creatures) + " creatures", ui_.theme.textDim);
                y += 26;
            }
        ui_.label({x, y}, "Open a .yore file, or drop it on this window, to add it.", ui_.theme.textDim);
        y += 26 + gap;
        if (button("Back (Esc)"))
            openMenu(Menu::Play);
        if (picked)
        {
            selectAdventure(*picked);
            notice_.clear();
            if (chapter_)
                menu_ = Menu::Play;
            return; // the fonts were reloaded; draw the menu again next frame
        }
        break;
    }
    case Menu::Create:
    {
        if (create_.isOpen())
        {
            // Show the CreateScreen with its full UI
            create_.draw(renderer);
        }
        else
        {
            // Show the Create menu with options to open or create
            const yh::Rect panel{screen.w / 2 - 300, y, 600, 240};
            ui_.panel(panel);
            ui_.label({panel.x + 20, panel.y + 18}, "Create: design, build, playtest and share", ui_.theme.accent);
            ui_.label({panel.x + 20, panel.y + 50}, "Plan the game and UI before building its editing tools.");
            ui_.label({panel.x + 20, panel.y + 76}, "Maps, walls, lights, tokens, encounters and dialogue.");
            ui_.label({panel.x + 20, panel.y + 102}, "Chapter writers own placement, story and cutscenes.");
            ui_.label({panel.x + 20, panel.y + 134}, "New makes an adventure folder with an empty map to draw on.", ui_.theme.textDim);
            ui_.label({panel.x + 20, panel.y + 164}, "Compendium: " + std::to_string(compendium_.classes.size()) + " classes, "
                + std::to_string(compendium_.items.size()) + " items, " + std::to_string(compendium_.creatures.size())
                + " creatures (add more by opening .yore files)", ui_.theme.textDim);
            y += panel.h + gap;
            if (button("New"))
                openCreateScreen();
            if (button("Open (last: " + (settings_.lastCreatePackage.empty() ? "none" : settings_.lastCreatePackage) + ")"))
            {
                if (!settings_.lastCreatePackage.empty())
                {
                    openCreateScreen(settings_.lastCreatePackage);
                }
            }
            if (button("Back (Esc)"))
                openMenu(Menu::Main);
        }
        break;
    }
    case Menu::Characters:
        drawCharacters(screen);
        break;
    case Menu::NewCharacter:
    case Menu::LevelUp:
        drawDraft(screen);
        break;
    case Menu::Party:
        drawParty(screen);
        break;
    case Menu::Settings:
        y = screen.h * 0.25f + 20;
        drawSettings({screen.w / 2 - 260, y - 30, 520, 498});
        y += 468 + gap;
        if (button("Back (Esc)"))
            openMenu(settingsBack_);
        break;
    case Menu::Pause:
        if (button("Resume (Esc)"))
            openMenu(Menu::None);
        if (button("Settings"))
            openMenu(Menu::Settings);
        if (!inSession() && button("Host co-op (port " + std::to_string(coopPort()) + ")", !testRun_ || SDL_getenv("YOREHOLD_HOST")))
            hostSession();
        else if (host_ && button("Stop hosting"))
            endSession("The host stopped the game.");
        if (button(client_ ? "Leave and quit to title" : "Save and quit to title"))
        {
            saveAdventure();
            autoPlay_ = false;
            if (inSession())
                endSession(client_ ? "You left." : "The host left.");
            openMenu(Menu::Main);
        }
        break;
    }
    if (!notice_.empty() && !paused)
    {
        // The character screens fill the middle, so their notice is a slim line at the bottom.
        const bool characterScreen = menu_ == Menu::Characters || menu_ == Menu::NewCharacter || menu_ == Menu::LevelUp || menu_ == Menu::Party;
        const yh::Rect noticeArea = characterScreen ? yh::Rect{20, screen.h - 64, screen.w - 40, 32}
            : yh::Rect{20, screen.h - (chapterError_.empty() ? 108.0f : 180.0f), screen.w - 40, 64};
        ui_.panel(noticeArea);
        ui_.label({noticeArea.x + 12, noticeArea.y + (characterScreen ? 6.0f : 12.0f)}, notice_, noticeBad_ ? ui_.theme.bad : ui_.theme.good);
    }
    if (!chapterError_.empty())
    {
        const yh::Rect errorArea{20, screen.h - 108, screen.w - 40, 64};
        ui_.panel(errorArea);
        ui_.label({errorArea.x + 12, errorArea.y + 12}, chapterError_, ui_.theme.bad);
    }
    ui_.label({12, screen.h - 30}, "F12: screenshot + note    F3: frame times", ui_.theme.textDim);
}

void YoreholdGame::drawSettings(const yh::Rect& area)
{
    ui_.panel(area);
    const Settings before = settings_;
    const float x = area.x + 20, w = area.w - 40, h = 36;
    float y = area.y + 16;

    ui_.label({x, y + 10}, "Controls", ui_.theme.textDim);
    const float half = (w - 120 - 10) / 2;
    if (ui_.toggle({x + 120, y, half, h}, "BG3", settings_.controls == yh::ControlPreset::BG3))
        settings_.controls = yh::ControlPreset::BG3;
    if (ui_.toggle({x + 130 + half, y, half, h}, "Foundry", settings_.controls == yh::ControlPreset::Foundry))
        settings_.controls = yh::ControlPreset::Foundry;
    y += h + 12;
    ui_.checkbox({x, y, w, h}, "Zoom toward the cursor (off: screen centre)", settings_.zoomToCursor);
    y += h + 8;
    ui_.checkbox({x, y, w, h}, "Pan when the mouse touches a screen edge", settings_.edgeScroll);
    y += h + 8;
    ui_.checkbox({x, y, w, h}, "Camera follows the moving character", settings_.cameraFollows);
    y += h + 8;
    ui_.checkbox({x, y, w, h}, "Fullscreen", settings_.fullscreen);
    y += h + 8;
    ui_.checkbox({x, y, w, h}, "Shared party view (off: only the selected hero's)", settings_.sharedFog);
    y += h + 8;
    ui_.checkbox({x, y, w, h}, "Ask before taking a reaction (default: take it)", settings_.reactionPrompts);
    y += h + 12;
    ui_.label({x, y + 10}, "Lighting", ui_.theme.textDim);
    const std::array<const char*, 4> modes{"Map", "Off", "Mood", "Rules"};
    const float quarter = (w - 120 - 30) / 4;
    for (int i = 0; i < 4; i++)
        if (ui_.toggle({x + 120 + i * (quarter + 10), y, quarter, h}, modes[i], settings_.lighting == i))
            settings_.lighting = i;
    y += h + 8;
    ui_.label({x, y + 10}, "Time of day", ui_.theme.textDim);
    const std::array<const char*, 4> times{"Map", "Day", "Dusk", "Night"};
    for (int i = 0; i < 4; i++)
        if (ui_.toggle({x + 120 + i * (quarter + 10), y, quarter, h}, times[i], settings_.timeOfDay == i))
            settings_.timeOfDay = i;
    y += h + 8;
    // Skins: click to step through the ones in the skins folder.
    ui_.label({x, y + 10}, "Skin", ui_.theme.textDim);
    const float folderWidth = 110;
    if (ui_.button({x + 120, y, w - 120 - folderWidth - 10, h}, settings_.skin.empty() ? std::string("Default") : settings_.skin))
    {
        refreshSkins();
        const auto current = std::find(skins_.begin(), skins_.end(), settings_.skin);
        settings_.skin = settings_.skin.empty() ? (skins_.empty() ? std::string() : skins_.front())
                       : current == skins_.end() || current + 1 == skins_.end() ? std::string() : *(current + 1);
    }
    if (ui_.button({x + w - folderWidth, y, folderWidth, h}, "Folder", !skinsDir().empty()))
    {
        prepareSkinsFolder();
        SDL_OpenURL(("file:///" + std::filesystem::path(skinsDir()).generic_string()).c_str());
    }
    y += h + 14;
    char text[48];
    std::snprintf(text, sizeof(text), "Pan speed  %.0f", settings_.panSpeed);
    ui_.label({x, y}, text, ui_.theme.textDim);
    ui_.slider({x + 160, y + 2, w - 160, 18}, settings_.panSpeed, 200, 3000);

    const bool changed = before.controls != settings_.controls || before.zoomToCursor != settings_.zoomToCursor
        || before.edgeScroll != settings_.edgeScroll || before.cameraFollows != settings_.cameraFollows
        || before.fullscreen != settings_.fullscreen || before.panSpeed != settings_.panSpeed
        || before.lighting != settings_.lighting || before.sharedFog != settings_.sharedFog
        || before.timeOfDay != settings_.timeOfDay || before.skin != settings_.skin
        || before.reactionPrompts != settings_.reactionPrompts;
    if (before.skin != settings_.skin)
        skinChanged_ = true; // swapped at the next update, once this frame's text has been drawn
    if (changed)
    {
        applySettings();
        settingsDirty_ = true;
    }
    // Sliders change every frame while dragged; write the file once the mouse is let go.
    if (settingsDirty_ && !input_.buttonDown(yh::MouseButton::Left))
    {
        saveSettings();
        settingsDirty_ = false;
    }
}
