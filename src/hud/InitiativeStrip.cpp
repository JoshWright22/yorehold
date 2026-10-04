#include "Hud.h"

#include <algorithm>
#include <cstdio>

void hud::initiativeStrip(Hud& hud)
{
    const yh::Encounter& encounter = *hud.world.encounter();
    yh::Ui& ui = hud.ui;
    const auto& order = encounter.order();
    const float gap = 6;
    const float available = hud.renderer.bounds().w - 326;
    const float boxWidth = std::min(120.0f, available / std::max<size_t>(1, order.size()) - gap);
    const float total = order.size() * (boxWidth + gap) - gap;
    float x = std::max(310.0f, hud.renderer.bounds().w / 2 - total / 2);
    const yh::Rect strip{x - 8, 8, total + 16, 64};
    ui.panel(strip);
    hud.panels.push_back(strip);

    char text[64];
    std::snprintf(text, sizeof(text), "Round %d", encounter.round());
    ui.label({strip.x + 8, strip.y + strip.h + 4}, text, ui.theme.accent);
    if (hud.world.rules().sharedTurns)
        ui.label({strip.x + 110, strip.y + strip.h + 4}, "Shared turn: click an unfinished ally", ui.theme.textDim);
    for (size_t i = 0; i < order.size(); i++, x += boxWidth + gap)
    {
        const yh::Combatant& c = order[i];
        const yh::Rect box{x, 14, boxWidth, 52};
        const bool now = i == encounter.currentIndex();
        const bool active = encounter.canSelectTurn(i);
        hud.renderer.fillRect(box, now ? yh::Color{92, 76, 116, 255} : yh::Color{46, 40, 54, 255});
        hud.renderer.fillRect({box.x, box.y, box.w, 4}, c.team == 0 ? ui.theme.good : ui.theme.bad); // team stripe
        hud.renderer.drawRect(box, now ? ui.theme.accent : active ? ui.theme.good : ui.theme.panelBorder, 3);
        const yh::Color color = c.character->down() || (hud.world.rules().sharedTurns && c.turnDone) ? ui.theme.textDim : ui.theme.text;
        std::snprintf(text, sizeof(text), "%d  %s", c.initiative, c.character->name.c_str());
        ui.label({box.x + 7, box.y + 6}, text, color);
        const float fraction = std::clamp(static_cast<float>(c.character->hp) / std::max(1, c.character->maxHp()), 0.0f, 1.0f);
        ui.bar({box.x + 6, box.y + 32, box.w - 12, 10}, fraction, c.team == 0 ? ui.theme.good : ui.theme.bad);
        for (size_t creature = 0; creature < hud.world.creatures().size(); creature++)
            if (&hud.world.creatures()[creature].sheet == c.character && hud.world.mine(creature) && hud.world.canChooseTurn(creature)
                && ui.hovered(box) && hud.input.buttonClicked(yh::MouseButton::Left))
                hud.world.act("turn", nlohmann::json{{"creature", creature}}.dump());
    }
}
