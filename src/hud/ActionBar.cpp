#include "Hud.h"

#include <SDL3/SDL_timer.h>

#include <algorithm>
#include <cstdio>

void hud::combatBar(Hud& hud)
{
    World& world = hud.world;
    yh::Ui& ui = hud.ui;
    const std::optional<size_t> current = world.currentCreature();
    if (!current)
        return;
    const yh::Encounter& encounter = *world.encounter();
    const yh::Rect screen = hud.renderer.bounds();
    const yh::Rect bar{10, screen.h - 76, std::min(660.0f, screen.w - 490), 66};
    ui.panel(bar);
    hud.panels.push_back(bar);

    const yh::Combatant& c = encounter.order()[encounter.currentIndex()];
    char text[160];
    if (world.creatures()[*current].team != 0)
    {
        std::snprintf(text, sizeof(text), "%s is taking their turn...", c.character->name.c_str());
        ui.label({bar.x + 16, bar.y + 22}, text, ui.theme.bad);
        return;
    }

    std::snprintf(text, sizeof(text), "%s: move %d sq   actions %d", c.character->name.c_str(), c.budget.movementLeft,
        c.budget.actions);
    ui.label({bar.x + 16, bar.y + 8}, text, ui.theme.accent);
    if (!world.mine(*current))
    {
        ui.label({bar.x + 16, bar.y + 36}, world.seatName(*current) + " is taking this turn.", ui.theme.textDim);
        return;
    }
    ui.label({bar.x + 16, bar.y + 36}, encounter.canStrike() ? "Click an enemy to attack." : "Move on, or end your turn.",
        ui.theme.textDim);

    const bool walking = !world.tokens().tokens[*current].path.empty();
    if (ui.button({bar.x + bar.w - 250, bar.y + 13, 100, 40}, "Dash", encounter.canAct() && !walking))
        world.act("dash");
    if (ui.button({bar.x + bar.w - 140, bar.y + 13, 128, 40}, "End turn", !walking))
        world.act("end");
}

void hud::exploreBar(Hud& hud)
{
    World& world = hud.world;
    yh::Ui& ui = hud.ui;
    const yh::Rect screen = hud.renderer.bounds();
    if (world.partyDown() || world.chapterCleared())
    {
        const yh::Rect button{screen.w / 2 - 110, screen.h * 0.22f + 110, 220, 44};
        if (ui.button(button, hud.guest ? "Waiting for the host" : world.partyDown() ? "Try again" : "Play again", !hud.guest))
            world.act("restart", nlohmann::json{{"seed", SDL_GetTicks()}}.dump());
        hud.panels.push_back(button);
        return;
    }
    if (world.fighting())
        return;
    // One button per rest the ruleset offers, under the party cards.
    const std::vector<World::Creature>& creatures = world.creatures();
    const bool hurt = std::any_of(creatures.begin(), creatures.begin() + world.heroCount(),
        [](const World::Creature& c) { return c.sheet.hp < c.sheet.maxHp(); });
    float y = 10 + world.heroCount() * 66.0f + 4;
    const auto& rests = world.rules().rests;
    for (size_t i = 0; i < rests.size(); i++, y += 46)
    {
        const yh::RestDefinition& r = rests[i];
        const int left = world.restsLeft(r);
        std::string text = r.name.empty() ? r.id : r.name;
        if (i == 0)
            text += " (R)";
        if (left >= 0)
            text += "  " + std::to_string(left) + " left";
        const yh::Rect button{10, y, 280, 40};
        if (ui.button(button, text, hurt && left != 0))
            world.act("rest", nlohmann::json{{"rest", i}}.dump());
        hud.panels.push_back(button);
    }
    const yh::Rect sneak{10, y, 280, 40};
    if (ui.button(sneak, world.sneakingMine() ? "Stop sneaking (C)" : "Sneak (C)"))
        world.act("sneak", nlohmann::json{{"on", !world.sneakingMine()}}.dump());
    hud.panels.push_back(sneak);
}
