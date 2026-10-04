#include "PlayScreen.h"

#include "hud/Hud.h"

#include <yorehold/framework/debug/Profiler.h>
#include <yorehold/framework/graphics/Renderer.h>

#include <SDL3/SDL_events.h>
#include <SDL3/SDL_keycode.h>

#include <algorithm>
#include <cmath>

namespace
{

constexpr float cell = GameMap::cellSize;

float distance(yh::Vec2 a, yh::Vec2 b)
{
    const yh::Vec2 d = a - b;
    return std::sqrt(d.x * d.x + d.y * d.y);
}

}

PlayScreen::PlayScreen(World& world, yh::Ui& ui, yh::Input& input, yh::Font* const& title)
    : world_(world), ui_(ui), input_(input), title_(title)
{
    // The UI-blocking input never sees a click, and its mouse sits far off the map.
    SDL_Event away{};
    away.type = SDL_EVENT_MOUSE_MOTION;
    away.motion.x = -100000;
    away.motion.y = -100000;
    noInput_.handle(away);
}

void PlayScreen::banner(std::string text, double seconds)
{
    banner_ = std::move(text);
    bannerTime_ = seconds;
}

// ---------------------------------------------------------------- input

bool PlayScreen::handleCutscene(const SDL_Event& event)
{
    if (!cutscene_.running())
        return false;
    // Space, Esc or a click skips; everything else is ignored while it plays.
    const bool skip = (event.type == SDL_EVENT_KEY_DOWN && !event.key.repeat
                          && (event.key.key == SDLK_SPACE || event.key.key == SDLK_ESCAPE || event.key.key == SDLK_RETURN))
        || event.type == SDL_EVENT_MOUSE_BUTTON_DOWN;
    if (skip)
    {
        cutscene_.skip(camera_);
        if (finished)
            finished();
    }
    return true;
}

bool PlayScreen::handle(const SDL_Event& event)
{
    const bool keyDown = event.type == SDL_EVENT_KEY_DOWN && !event.key.repeat;
    if (const auto& prompt = world_.reactionPrompt(); prompt && keyDown && world_.mine(prompt->creature)
        && (event.key.key == SDLK_RETURN || event.key.key == SDLK_X))
    {
        world_.react(event.key.key == SDLK_RETURN);
        return true;
    }
    if (world_.talk())
    {
        // 1-9 pick a reply, Esc walks away; the mouse still reaches the reply buttons.
        if (keyDown && event.key.key >= SDLK_1 && event.key.key <= SDLK_9)
            world_.act("reply", nlohmann::json{{"choice", event.key.key - SDLK_1}, {"hero", world_.leaderIndex()}}.dump());
        else if (keyDown && event.key.key == SDLK_ESCAPE)
            world_.act("leave");
        else
            input_.handle(event);
        return true;
    }
    if (keyDown && event.key.key == SDLK_J && world_.journal())
    {
        journalOpen_ = !journalOpen_;
        inventoryOpen_ = false;
        return true;
    }
    if (keyDown && event.key.key == SDLK_I)
    {
        inventoryOpen_ = !inventoryOpen_;
        journalOpen_ = false;
        return true;
    }
    if (keyDown && event.key.key == SDLK_ESCAPE)
    {
        if (!journalOpen_ && !inventoryOpen_)
            return false;
        journalOpen_ = inventoryOpen_ = false;
        return true;
    }
    if (keyDown && event.key.key == SDLK_C)
    {
        world_.act("sneak", nlohmann::json{{"on", !world_.sneakingMine()}}.dump());
        return true;
    }
    if (keyDown && event.key.key == SDLK_R && !world_.rules().rests.empty())
    {
        world_.act("rest", R"({"rest": 0})"); // R = the ruleset's first (usually shortest) rest
        return true;
    }
    input_.handle(event);
    return true;
}

// ---------------------------------------------------------------- update

bool PlayScreen::update(double deltaSeconds, bool menuOpen)
{
    if (cutscene_.running())
    {
        cutscene_.update(deltaSeconds, camera_, [this](std::string_view event) {
            if (event == "finished")
                cutsceneDone_ = true;
        });
        if ((cutsceneDone_ || !cutscene_.running()) && finished)
            finished();
        return false;
    }
    bannerTime_ = std::max(0.0, bannerTime_ - deltaSeconds);
    const bool fighting = world_.fighting();
    const std::optional<size_t> current = world_.currentCreature();
    const bool mouseOnUi = overUi(input_.mouse());

    // Exploring, each player walks their own heroes. In a fight, steps are commands (heroInput).
    const yh::TokenController::Passable passable = [this](yh::Cell c) { return world_.walkable(c); };
    const bool tokensListen = !menuOpen && !mouseOnUi && !world_.partyDown() && !world_.talk() && !fighting;
    const yh::Input& tokenInput = tokensListen ? input_ : noInput_;
    world_.tokens().handleInput(tokenInput, camera_, world_.grid(), passable);
    world_.walk(deltaSeconds);
    if (&tokenInput == &input_ && input_.clicked(yh::actions::moveTo))
        controls_.resumeFollowing();
    if (walked)
        walked(deltaSeconds);

    // Clicking someone to talk to walks the leader over; the conversation opens on arrival.
    if (tokensListen && (input_.clicked(yh::actions::moveTo) || input_.clicked(yh::actions::select)))
        if (const std::optional<size_t> who = hoveredTalker())
            world_.walkToTalk(*who);
    world_.arrive();
    contextMenu(fighting, current);
    world_.takeTurns(deltaSeconds, !menuOpen, [this] { heroInput(); });
    return true;
}

// Right-click menu. On an NPC or someone who surrendered: Talk walks over, Attack picks a fight with them.
void PlayScreen::contextMenu(bool fighting, std::optional<size_t> current)
{
    const std::optional<std::pair<size_t, std::string>>& choice = world_.tokens().contextChoice;
    const std::vector<World::Creature>& creatures = world_.creatures();
    const bool heroTurn = current && creatures[*current].team == 0;
    const std::optional<size_t> menuTalker = choice && choice->first < creatures.size() && world_.talkable(choice->first)
        ? std::optional<size_t>(choice->first) : std::nullopt;
    if (menuTalker && choice->second != "Inspect")
    {
        if (fighting)
            world_.say("Not in the middle of a fight.");
        else if (choice->second == "Attack")
            world_.act("provoke", nlohmann::json{{"creature", *menuTalker}}.dump());
        else
            world_.walkToTalk(*menuTalker);
    }
    else if (choice && choice->first < creatures.size())
    {
        const auto [index, action] = *choice;
        if (action == "Attack" && fighting && heroTurn && creatures[index].team == 1 && world_.mine(*current))
            attackWithArmed(*current, index);
        else if (action == "Attack" && !fighting && creatures[index].team == 1 && !creatures[index].awake && world_.sneakingMine())
            world_.act("ambush", nlohmann::json{{"creature", index}}.dump());
        else if (action == "Inspect" || action == "Attack")
        {
            const yh::Character& c = creatures[index].sheet;
            world_.say(c.name + ": " + c.characterClass + ", HP " + std::to_string(c.hp) + "/" + std::to_string(c.maxHp()) + ", AC " +
                std::to_string(world_.positionalArmorClass(index)));
        }
    }
}

void PlayScreen::heroInput()
{
    const size_t me = *world_.currentCreature();
    const yh::Token& token = world_.tokens().tokens[me];
    const bool walkClick = input_.clicked(yh::actions::moveTo), selectClick = input_.clicked(yh::actions::select);
    if (!overUi(input_.mouse()) && (walkClick || selectClick))
    {
        const yh::ActionDefinition* armed = hud::armedAction(world_, me, armed_);
        if (const std::optional<size_t> target = hoveredCreature(); target && armed
            && (armed->side == yh::ActionDefinition::Side::Any
                || (world_.creatures()[*target].team == world_.creatures()[me].team) == (armed->side == yh::ActionDefinition::Side::Ally)))
            attackWithArmed(me, *target);
        else if (walkClick && token.path.empty())
        {
            // Only onto squares its movement reaches.
            const yh::Cell to = world_.grid().cellAt(camera_.screenToWorld(input_.mouse()));
            if (to != world_.standing() && world_.reach().contains(to))
                world_.act("step", nlohmann::json{{"at", {to.x, to.y}}}.dump());
        }
        return;
    }
    if (input_.keyPressed(SDLK_SPACE) && token.path.empty())
        world_.use(World::endTurnAction);
}

void PlayScreen::attackWithArmed(size_t hero, size_t target)
{
    const yh::ActionDefinition* armed = hud::armedAction(world_, hero, armed_);
    world_.tryAttack(target, armed ? armed->id : World::strikeAction);
}

void PlayScreen::updateCamera(double deltaSeconds)
{
    std::optional<yh::Vec2> follow = world_.tokens().followTarget();
    if (const std::optional<size_t> now = world_.currentCreature(); now && world_.fighting())
        follow = world_.tokens().tokens[*now].position;
    controls_.update(camera_, input_, deltaSeconds, follow);

    for (Floater& floater : floaters_)
        floater.age += static_cast<float>(deltaSeconds);
    std::erase_if(floaters_, [](const Floater& f) { return f.age > 1.4f; });
}

void PlayScreen::show(World::Event& event)
{
    using Kind = World::Event::Kind;
    switch (event.kind)
    {
    case Kind::Reset:
        floaters_.clear();
        log_.clear();
        cutscene_ = {};
        cutsceneDone_ = false;
        journalOpen_ = inventoryOpen_ = false;
        cameraPlaced_ = false;
        break;
    case Kind::Resumed:
        log_.clear();
        bannerTime_ = 0;
        break;
    case Kind::Log:
        log_.push_back(std::move(event.text));
        if (log_.size() > 200)
            log_.erase(log_.begin(), log_.begin() + 50);
        break;
    case Kind::Floater:
    {
        using Float = World::FloatKind;
        const yh::Color color = event.floater == Float::Miss ? yh::Color{200, 200, 210, 255}
            : event.floater == Float::Hit ? yh::Color{255, 90, 70, 255}
            : event.floater == Float::Critical ? yh::Color{255, 200, 60, 255}
            : event.floater == Float::Heal ? ui_.theme.good : yh::Color{150, 200, 255, 255};
        floaters_.push_back({event.at, std::move(event.text), color});
        break;
    }
    case Kind::Banner:
        banner(std::move(event.text), event.seconds);
        break;
    case Kind::Camera:
        camera_.moveTo(event.at);
        break;
    case Kind::Follow:
        controls_.resumeFollowing();
        break;
    case Kind::Ending:
        if (std::optional<yh::Cutscene> ending = yh::Cutscene::fromJson(event.text))
        {
            cutscene_ = std::move(*ending);
            cutsceneDone_ = false;
            bannerTime_ = 0;
            controls_.resumeFollowing();
            cutscene_.start();
        }
        break;
    case Kind::Talk:
        journalOpen_ = inventoryOpen_ = false;
        break;
    case Kind::Save:
        break; // the game keeps the saves
    }
}

std::optional<size_t> PlayScreen::hoveredCreature() const
{
    const yh::Vec2 world = camera_.screenToWorld(input_.mouse());
    for (size_t i = world_.creatures().size(); i-- > 0;)
    {
        const yh::Token& token = world_.tokens().tokens[i];
        if ((token.floor == 0 || (token.floor == World::dead && world_.creatures()[i].team == 0))
            && distance(world, token.position) <= token.radius)
            return i;
    }
    return std::nullopt;
}

std::optional<size_t> PlayScreen::hoveredTalker() const
{
    if (!world_.chapter())
        return std::nullopt;
    const yh::Vec2 world = camera_.screenToWorld(input_.mouse());
    for (size_t i = world_.heroCount(); i < world_.creatures().size(); i++)
    {
        const yh::Token& token = world_.tokens().tokens[i];
        if (world_.talkable(i) && token.floor == 0 && distance(world, token.position) <= token.radius)
            return i;
    }
    return std::nullopt;
}

bool PlayScreen::overUi(yh::Vec2 screen) const
{
    return std::any_of(uiRects_.begin(), uiRects_.end(), [screen](const yh::Rect& r) { return r.contains(screen); });
}

// ---------------------------------------------------------------- drawing

void PlayScreen::drawWorld(yh::Renderer& renderer)
{
    GameMap& map = world_.map();
    const yh::Grid& grid = world_.grid();
    yh::TokenController& tokens = world_.tokens();
    const std::vector<World::Creature>& creatures = world_.creatures();
    map.bindTileset(renderer);
    camera_.setViewport(renderer.bounds().size());
    if (!cameraPlaced_)
    {
        // Needs the viewport size, so it waits for the first frame.
        camera_.jumpTo(tokens.tokens[0].position, 1.0f);
        cameraPlaced_ = true;
    }

    renderer.clear({6, 7, 12, 255});
    const yh::Rect view = camera_.visibleWorld();
    camera_.apply(renderer);
    map.map().draw(renderer, view, camera_.zoom(), 0);
    grid.draw(renderer, view.intersect(map.map().worldBounds()), camera_.zoom(), {0, 0, 0, 45});

    // Torch flames; the light itself comes from the lighting pass.
    for (size_t i = 0; i < map.lights().size(); i++)
    {
        const GameMap::Light& torch = map.lights()[i];
        if (!torch.flame)
            continue;
        const float flicker = 1 + 0.15f * std::sin(static_cast<float>(time_) * 11 + i * 1.7f);
        renderer.fillCircle(torch.position, 10 * flicker, {255, 140, 50, 255});
        renderer.fillCircle(torch.position, 5 * flicker, {255, 235, 170, 255});
    }

    // The fallen: a dark mark where they dropped.
    for (size_t i = 0; i < creatures.size(); i++)
    {
        const yh::Token& token = tokens.tokens[i];
        if (token.floor != World::dead || creatures[i].fled || world_.fog().state(world_.viewTeam(), 0, grid.cellAt(token.position)) == yh::FogState::Unexplored)
            continue;
        const float r = token.radius * 0.7f;
        renderer.fillCircle(token.position, token.radius, creatures[i].team == 0 ? yh::Color{90, 90, 100, 255} : yh::Color{70, 30, 25, 255});
        renderer.drawLine(token.position - yh::Vec2{r, r}, token.position + yh::Vec2{r, r}, {20, 10, 10, 255}, 5);
        renderer.drawLine(token.position + yh::Vec2{-r, r}, token.position + yh::Vec2{r, -r}, {20, 10, 10, 255}, 5);
    }
    tokens.draw(renderer, camera_, grid);
    renderer.pop();

    std::vector<yh::Light> lights;
    for (size_t i = 0; i < map.lights().size(); i++)
    {
        const GameMap::Light& torch = map.lights()[i];
        const float flicker = torch.flame ? 1 + 0.04f * std::sin(static_cast<float>(time_) * 9 + i * 2.3f) : 1.0f;
        lights.push_back({torch.position, torch.radius * flicker, torch.color});
    }
    for (size_t i = 0; i < world_.heroCount(); i++)
    {
        if (tokens.tokens[i].floor != World::dead && map.lighting().carried > 0 && !creatures[i].sneaking())
            lights.push_back({tokens.tokens[i].position, map.lighting().carried * cell, {255, 215, 160, 255}});
    }
    if (world_.lightingMode() != GameMap::LightingMode::Off)
    {
        const GameMap::Sky sky = map.sky(world_.timeOfDay());
        std::vector<yh::Shade> shaded;
        if (sky.differs)
            for (const yh::Rect& area : map.indoorAreas())
                shaded.push_back({area, sky.indoors});
        lighting_.ambient = sky.outdoors;
        lighting_.apply(renderer, camera_, lights, map.walls(), shaded);
    }

    camera_.apply(renderer);
    // While sneaking, show where each enemy that hasn't noticed the party is looking.
    if (!world_.fighting() && world_.sneakingMine())
    {
        const std::vector<yh::Watcher> watching = world_.watchers();
        for (size_t w = 0; w < watching.size(); w++)
        {
            if (watching[w].range <= 0 || tokens.tokens[world_.heroCount() + w].floor != 0)
                continue;
            const std::vector<yh::Vec2> cone = yh::visionCone(watching[w], map.walls(), 40);
            for (size_t p = 1; p < cone.size(); p++)
            {
                // The tint is a fan of wide lines; started a little way out so they don't pile up over the token.
                renderer.drawLine(cone[0] + (cone[p] - cone[0]) * 0.2f, cone[p], {230, 60, 50, 26}, 8);
                if (p > 1)
                    renderer.drawLine(cone[p - 1], cone[p], {230, 60, 50, 170}, 2);
            }
            renderer.drawLine(cone[0], cone[1], {230, 60, 50, 170}, 2);
            renderer.drawLine(cone.back(), cone[0], {230, 60, 50, 170}, 2);
        }
    }
    const std::optional<size_t> current = world_.currentCreature();
    if (current)
    {
        // Where the current hero can still move, and a ring on whoever's turn it is.
        const bool heroTurn = creatures[*current].team == 0;
        if (heroTurn && tokens.tokens[*current].path.empty())
        {
            for (const auto& [c, cost] : world_.reach())
            {
                if (c != world_.standing())
                    renderer.fillRect({c.x * cell + 2, c.y * cell + 2, cell - 4, cell - 4}, {90, 170, 255, 45});
            }
        }
        const yh::Cell here = grid.cellAt(tokens.tokens[*current].position);
        const float pulse = 0.5f + 0.5f * std::sin(static_cast<float>(time_) * 5);
        renderer.drawRect({here.x * cell + 1, here.y * cell + 1, cell - 2, cell - 2},
            {255, 210, 90, static_cast<uint8_t>(140 + 100 * pulse)}, 3);

        if (heroTurn && !overUi(input_.mouse()))
        {
            if (const std::optional<size_t> target = hoveredCreature(); target && creatures[*target].team == 1)
            {
                const yh::Cell t = world_.cellOf(*target);
                renderer.drawRect({t.x * cell + 1, t.y * cell + 1, cell - 2, cell - 2}, {255, 70, 50, 255}, 3);
            }
        }
    }
    world_.fog().draw(renderer, view, world_.viewTeam(), 0, {0, 0, 0, 255}, {4, 6, 14, 175});
    renderer.pop();
    tokens.drawOverlay(renderer, camera_, grid);
}

void PlayScreen::drawOverlay(yh::Renderer& renderer)
{
    if (cutscene_.running())
    {
        cutscene_.draw(renderer, ui_.theme.font, title_);
        return;
    }
    const yh::Token& leader = world_.tokens().tokens[0];
    yh::debug::value("camera x", camera_.position().x);
    yh::debug::value("camera y", camera_.position().y);
    yh::debug::value("camera follows", controls_.following() ? 1 : 0);
    yh::debug::value("camera zoom", camera_.zoom());
    yh::debug::value("party visibility", static_cast<int>(world_.fog().state(world_.viewTeam(), 0, world_.cellOf(0))));
    yh::debug::value("leader floor", leader.floor);
    yh::debug::value("party x", leader.position.x);
    yh::debug::value("party y", leader.position.y);
    yh::debug::value("pointer inside", input_.mouseInside() ? 1 : 0);
    drawBars(renderer);
    drawHud(renderer);
}

void PlayScreen::drawBars(yh::Renderer& renderer)
{
    const bool fighting = world_.fighting();
    for (size_t i = 0; i < world_.creatures().size(); i++)
    {
        const yh::Token& token = world_.tokens().tokens[i];
        const yh::Character& c = world_.creatures()[i].sheet;
        if (token.floor != 0 || (!fighting && c.hp == c.maxHp()))
            continue;
        const yh::Vec2 top = camera_.worldToScreen(token.position - yh::Vec2{0, token.radius + 10});
        const float w = 46 * std::clamp(camera_.zoom(), 0.6f, 1.5f);
        const yh::Rect back{top.x - w / 2, top.y - 6, w, 6};
        renderer.fillRect({back.x - 1, back.y - 1, back.w + 2, back.h + 2}, {0, 0, 0, 200});
        const float fraction = std::clamp(static_cast<float>(c.hp) / std::max(1, c.maxHp()), 0.0f, 1.0f);
        renderer.fillRect({back.x, back.y, back.w * fraction, back.h}, world_.creatures()[i].team == 0 ? ui_.theme.good : ui_.theme.bad);
        if (world_.fighting() && world_.fog().state(world_.viewTeam(), 0, world_.cellOf(i)) == yh::FogState::Visible)
        {
            std::string positioning = world_.isFlanked(i) ? "Flanked" : "";
            if (const auto current = world_.currentCreature(); current && world_.creatures()[*current].team != world_.creatures()[i].team)
                if (const auto* action = hud::armedAction(world_, *current, armed_); action && action->range > 1)
                {
                    const auto cover = world_.coverFrom(*current, i);
                    const char* name = cover == yh::Cover::Half ? "Half cover" : cover == yh::Cover::ThreeQuarters ? "3/4 cover"
                        : cover == yh::Cover::Full ? "Full cover" : "";
                    if (*name) positioning += (positioning.empty() ? "" : " / ") + std::string(name);
                }
            if (!positioning.empty() && ui_.theme.font)
            {
                const float width = ui_.theme.font->measure(positioning);
                ui_.theme.font->draw(renderer, {top.x - width / 2, top.y - 26}, positioning, ui_.theme.accent);
            }
        }
    }

    for (const Floater& floater : floaters_)
    {
        const yh::Vec2 at = camera_.worldToScreen(floater.world) - yh::Vec2{0, 30 + floater.age * 45};
        yh::Color color = floater.color;
        color.a = static_cast<uint8_t>(255 * std::clamp(1.4f - floater.age, 0.0f, 1.0f));
        if (ui_.theme.font)
        {
            const float width = ui_.theme.font->measure(floater.text);
            ui_.theme.font->draw(renderer, at + yh::Vec2{-width / 2 + 2, 2}, floater.text, {0, 0, 0, color.a});
            ui_.theme.font->draw(renderer, at - yh::Vec2{width / 2, 0}, floater.text, color);
        }
    }
}

void PlayScreen::drawHud(yh::Renderer& renderer)
{
    ui_.begin(renderer, input_);
    uiRects_.clear();
    const yh::Rect screen = renderer.bounds();
    Hud hud{world_, renderer, ui_, input_, uiRects_, armed_, table.inSession, table.guest};

    if (!table.netStatus.empty())
        ui_.label({screen.w / 2 - 160, screen.h - 58}, table.netStatus, ui_.theme.textDim);
    if (bannerTime_ > 0 && title_)
    {
        const float alpha = static_cast<float>(std::clamp(bannerTime_, 0.0, 1.0));
        const yh::Rect area{0, screen.h * 0.22f, screen.w, 80};
        renderer.fillRect({0, area.y - 10, screen.w, area.h + 20}, {0, 0, 0, static_cast<uint8_t>(120 * alpha)});
        title_->drawCentered(renderer, area, banner_, {255, 214, 140, static_cast<uint8_t>(255 * alpha)});
    }

    hud::partyCards(hud);
    if (world_.fighting())
    {
        hud::initiativeStrip(hud);
        hud::combatBar(hud);
    }

    // Adventure log, bottom right.
    const yh::Rect logArea{screen.w - 440, screen.h - 160, 430, 150};
    ui_.log(logArea, log_);
    uiRects_.push_back(logArea);

    if (world_.journal())
        hud::journalPanel(hud, journalOpen_, title_);
    if (inventoryOpen_ && !world_.talk())
        hud::inventoryPanel(hud);
    if (world_.talk())
    {
        hud::dialoguePanel(hud);
        return;
    }
    if (!world_.fighting())
    {
        std::string hint = table.controls == yh::ControlPreset::BG3
            ? "Left-click: walk / select / talk   Drag: box-select   WASD / edges: pan   Wheel: zoom   F6: Foundry controls"
            : "Right-click: walk / talk   Left: select / drag   Right-drag: pan   Wheel: zoom   F6: BG3 controls";
        hint += "   C: sneak   I: gear";
        if (world_.journal())
            hint += "   J: journal";
        ui_.label({12, screen.h - 30}, hint, ui_.theme.textDim);
    }
    hud::exploreBar(hud);
}
