#pragma once

#include "sim/World.h"

#include <yorehold/framework/animation/Cutscene.h>
#include <yorehold/framework/graphics/Camera.h>
#include <yorehold/framework/graphics/Lighting.h>
#include <yorehold/framework/input/ControlScheme.h>
#include <yorehold/framework/map/CameraControls.h>
#include <yorehold/framework/ui/Ui.h>

#include <functional>
#include <optional>
#include <string>
#include <vector>

union SDL_Event;

// Playing an adventure: the map, the party and the HUD over the world. It reads the World to
// draw, turns clicks and keys into intents, and shows what the world reports (log lines, floating
// numbers, banners, the ending cutscene). Menus and saving belong to the game around it.
class PlayScreen
{
public:
    // `ui`, `input` and `title` belong to the game, which reloads fonts and skins under them.
    PlayScreen(World& world, yh::Ui& ui, yh::Input& input, yh::Font* const& title);

    // What the game around the screen tells it each frame.
    struct Table
    {
        bool inSession = false; // co-op
        bool guest = false;     // joined someone else's game: only the host starts it again
        std::string netStatus;
        yh::ControlPreset controls = yh::ControlPreset::BG3;
    };
    Table table;
    std::function<void()> finished;     // the ending cutscene is over or was skipped
    std::function<void(double)> walked; // this machine's heroes have taken their steps (co-op shares them)

    void tick(double deltaSeconds) { time_ += deltaSeconds; }
    // While the ending plays, every key and click goes to it (some skip it). False when none is playing.
    bool handleCutscene(const SDL_Event& event);
    // Keys during play. False for an Esc it has no use for, which opens the pause menu.
    bool handle(const SDL_Event& event);
    // Input, then the world's step. `menuOpen`: the co-op pause menu is over the game, which
    // carries on. False while the ending plays, when nothing else moves.
    bool update(double deltaSeconds, bool menuOpen);
    // The camera follows, after the world's events have been shown (they can move it).
    void updateCamera(double deltaSeconds);
    void show(World::Event& event);

    void drawWorld(yh::Renderer& renderer);
    void drawOverlay(yh::Renderer& renderer); // the ending, or the bars and HUD

    void stopCutscene() { cutscene_ = {}; }
    // Picks the aimed action the next click on the map uses, as its button on the action bar does.
    void arm(std::string action) { armed_ = std::move(action); }
    void banner(std::string text, double seconds);
    yh::Camera& camera() { return camera_; }
    yh::CameraControls& cameraControls() { return controls_; }

private:
    // Damage numbers and "Miss!" that float up from a token.
    struct Floater
    {
        yh::Vec2 world;
        std::string text;
        yh::Color color;
        float age = 0;
    };

    void heroInput(); // the player's own hero's turn in a fight: clicks and keys
    void attackWithArmed(size_t hero, size_t target); // a click on an enemy: the armed action, walking into range first
    void contextMenu(bool fighting, std::optional<size_t> current);
    std::optional<size_t> hoveredCreature() const;
    std::optional<size_t> hoveredTalker() const;
    bool overUi(yh::Vec2 screen) const;
    // The armed action's reach on the map: the ruler to the pointer and, for an area, its template.
    void drawAim(yh::Renderer& renderer, size_t hero, const yh::ActionDefinition& action);
    void drawBars(yh::Renderer& renderer);
    void drawHud(yh::Renderer& renderer);

    World& world_;
    yh::Ui& ui_;
    yh::Input& input_;
    yh::Font* const& title_;
    yh::Input noInput_; // fed to the token controller while the mouse is over the UI
    yh::Camera camera_;
    yh::CameraControls controls_;
    yh::Lighting lighting_;
    yh::Cutscene cutscene_;
    bool cutsceneDone_ = false; // set by the cutscene's "finished" event, handled after its update

    std::vector<std::string> log_;
    std::vector<Floater> floaters_;
    std::vector<yh::Rect> uiRects_; // last frame's panels, so clicks on them don't walk the party
    std::string banner_;
    double bannerTime_ = 0;
    double time_ = 0;
    bool cameraPlaced_ = false;
    bool journalOpen_ = false;
    bool inventoryOpen_ = false;
    std::optional<size_t> giving_;  // gear panel: the item being handed to someone
    std::optional<size_t> looting_; // the pile whose contents are open
    std::optional<size_t> trading_; // NPC index, only while beside a peaceful merchant
    size_t tradePage_ = 0;
    std::optional<std::pair<size_t, size_t>> consuming_;
    size_t inventoryPage_ = 0;
    bool spellsOpen_ = false;
    std::optional<std::string> casting_; // spell panel: the spell waiting for its target
    std::string armed_; // the aimed action picked on the action bar (see hud::armedAction)
};
