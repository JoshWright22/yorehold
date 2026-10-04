#include "Hud.h"

#include <SDL3/SDL_timer.h>

#include <algorithm>
#include <cstdio>

const yh::ActionDefinition* hud::armedAction(const World& world, size_t creature, const std::string& armed)
{
    const yh::ActionDefinition* first = nullptr;
    for (const yh::ActionDefinition* action : world.actionsOf(creature))
    {
        if (action->target != yh::ActionDefinition::Target::Creature)
            continue;
        if (action->id == armed)
            return action;
        if (!first)
            first = action;
    }
    return first;
}

void hud::combatBar(Hud& hud)
{
    World& world = hud.world;
    yh::Ui& ui = hud.ui;
    const std::optional<size_t> current = world.currentCreature();
    if (!current)
        return;
    const yh::Encounter& encounter = *world.encounter();
    const yh::Rect screen = hud.renderer.bounds();
    const bool heroTurn = world.creatures()[*current].team == 0;
    const bool listing = heroTurn && world.mine(*current);

    // One button for each action the acting creature has, in rows filled from the bottom right,
    // clear of the text on the left. The bar grows upward when one row is not enough.
    const std::vector<const yh::ActionDefinition*> actions = listing ? world.actionsOf(*current) : std::vector<const yh::ActionDefinition*>{};
    const float barWidth = std::min(660.0f, screen.w - 490);
    const float space = std::max(128.0f, barWidth - 312), gap = 10, rowHeight = 46;
    auto widthOf = [](const yh::ActionDefinition& action) { return action.endsTurn ? 128.0f : 100.0f; };
    std::vector<std::pair<int, float>> places(actions.size()); // row (0 = bottom) and distance of the right edge from the bar's
    int row = 0;
    float used = 0;
    for (size_t i = actions.size(); i-- > 0;)
    {
        const float width = widthOf(*actions[i]);
        if (used > 0 && used + gap + width > space)
        {
            row++;
            used = 0;
        }
        used += (used > 0 ? gap : 0) + width;
        places[i] = {row, used};
    }

    const float height = 66 + rowHeight * static_cast<float>(row);
    const yh::Rect bar{10, screen.h - 10 - height, barWidth, height};
    ui.panel(bar);
    hud.panels.push_back(bar);

    const yh::Combatant& c = encounter.order()[encounter.currentIndex()];
    char text[160];
    if (!heroTurn)
    {
        std::snprintf(text, sizeof(text), "%s is taking their turn...", c.character->name.c_str());
        ui.label({bar.x + 16, bar.y + 22}, text, ui.theme.bad);
        return;
    }

    std::snprintf(text, sizeof(text), "%s: move %d sq   actions %d", c.character->name.c_str(), c.budget.movementLeft,
        c.budget.actions);
    ui.label({bar.x + 16, bar.y + 8}, text, ui.theme.accent);
    if (!listing)
    {
        ui.label({bar.x + 16, bar.y + 36}, world.seatName(*current) + " is taking this turn.", ui.theme.textDim);
        return;
    }
    // Clicking an enemy uses the armed action: the one picked on the bar, else the first that can be aimed.
    const yh::ActionDefinition* armed = armedAction(world, *current, hud.armed);
    const std::string aim = armed && armed->side == yh::ActionDefinition::Side::Ally ? ": click an ally."
        : armed && armed->side == yh::ActionDefinition::Side::Any ? ": click a creature." : ": click an enemy.";
    ui.label({bar.x + 16, bar.y + 36}, armed && world.canUse(*current, *armed) ? armed->name + aim : "Move on, or end your turn.",
        ui.theme.textDim);

    const bool walking = !world.tokens().tokens[*current].path.empty();
    for (size_t i = 0; i < actions.size(); i++)
    {
        const yh::ActionDefinition& action = *actions[i];
        const yh::Rect button{bar.x + bar.w - 12 - places[i].second, bar.y + bar.h - 53 - rowHeight * static_cast<float>(places[i].first),
            widthOf(action), 40};
        const bool usable = world.canUse(*current, action);
        if (action.target == yh::ActionDefinition::Target::Creature)
        {
            // Aimed actions wait for a click on the map; the button picks which one that click uses.
            if (!usable)
                ui.button(button, action.name, false);
            else if (ui.toggle(button, action.name, &action == armed))
                hud.armed = action.id;
        }
        else if (ui.button(button, action.name, usable && !walking))
            world.use(action.id);
    }
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
