#include "Hud.h"

#include <algorithm>
#include <cstdio>

void hud::partyCards(Hud& hud)
{
    World& world = hud.world;
    yh::Ui& ui = hud.ui;
    const std::optional<size_t> current = world.currentCreature();
    const bool fighting = world.fighting();
    char text[96];
    for (size_t i = 0; i < world.heroCount(); i++)
    {
        const yh::Character& c = world.creatures()[i].sheet;
        const yh::Rect area{10, 10 + i * 66.0f, 280, 58};
        hud.panels.push_back(area);
        ui.panel(area);
        if (current && *current == i)
            hud.renderer.drawRect(area, ui.theme.accent, 3);
        else if (fighting && world.canChooseTurn(i))
            hud.renderer.drawRect(area, ui.theme.good, 2);
        else if (world.tokens().tokens[i].selected)
            hud.renderer.drawRect(area, {200, 180, 140, 255}, 3);
        // Portrait slot: a dark square with the hero's colour inside.
        const yh::Rect portrait{area.x + 8, area.y + 9, 40, 40};
        hud.renderer.fillRect(portrait, ui.theme.panelBorder);
        hud.renderer.fillRect({portrait.x + 3, portrait.y + 3, 34, 34}, c.down() ? yh::Color{80, 80, 90, 255} : world.tokens().tokens[i].color);

        std::snprintf(text, sizeof(text), "%s  Lv %d %s", c.name.c_str(), c.level, c.characterClass.c_str());
        ui.label({area.x + 58, area.y + 6}, hud.inSession ? c.name + "  (" + world.seatName(i) + ")" : std::string(text),
            c.down() ? ui.theme.textDim : world.mine(i) ? ui.theme.text : ui.theme.textDim);
        const float fraction = std::clamp(static_cast<float>(c.hp) / std::max(1, c.maxHp()), 0.0f, 1.0f);
        ui.bar({area.x + 58, area.y + 32, 110, 16}, fraction, fraction > 0.5f ? ui.theme.good : ui.theme.bad);
        std::snprintf(text, sizeof(text), c.down() ? "Down" : "%d/%d  AC %d", c.hp, c.maxHp(), world.positionalArmorClass(i));
        ui.label({area.x + 176, area.y + 30}, text, ui.theme.textDim);

        if (fighting && world.mine(i) && world.canChooseTurn(i) && ui.hovered(area) && hud.input.buttonClicked(yh::MouseButton::Left))
            world.act("turn", nlohmann::json{{"creature", i}}.dump());
        // Clicking a portrait while exploring makes that hero the leader.
        if (!fighting && !c.down() && world.mine(i) && ui.hovered(area) && hud.input.buttonClicked(yh::MouseButton::Left))
        {
            std::vector<yh::Token>& tokens = world.tokens().tokens;
            for (size_t j = 0; j < tokens.size(); j++)
                tokens[j].selected = j == i;
        }
    }
}
