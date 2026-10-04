#include "Hud.h"

#include <nlohmann/json.hpp>

#include <cctype>
#include <cstdio>

namespace
{

// "mainHand" -> "main hand"
std::string slotName(const std::string& slot)
{
    std::string name;
    for (const char c : slot)
    {
        if (std::isupper(static_cast<unsigned char>(c)))
            name += ' ';
        name += static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    }
    return name;
}

}

// What one hero carries (I): click an item to put it on or away. Between fights that is free; in
// a fight it is the hero's own turn and an Interact.
void hud::inventoryPanel(Hud& hud)
{
    World& world = hud.world;
    yh::Ui& ui = hud.ui;
    const yh::Rect screen = hud.renderer.bounds();
    // The hero whose turn it is, if it is one of this player's; otherwise the one selected.
    const std::optional<size_t> acting = world.fighting() ? world.currentCreature() : std::nullopt;
    const size_t hero = acting && *acting < world.heroCount() && world.mine(*acting) ? *acting : world.leaderIndex();
    if (hero >= world.heroCount())
        return;
    const yh::Character& sheet = world.creatures()[hero].sheet;
    const float line = ui.lineHeight();

    const yh::Rect area{screen.w / 2 - 280, screen.h * 0.12f, 560, screen.h * 0.72f};
    ui.panel(area);
    hud.panels.push_back(area);
    const float x = area.x + 20, w = area.w - 40;
    float y = area.y + 16;
    ui.label({x, y}, sheet.name + ": gear", ui.theme.accent);
    y += line + 4;

    std::string held;
    for (const yh::Item& item : sheet.inventory)
        if (item.equipped && yh::Character::held(item))
            held += (held.empty() ? "" : ", ") + item.name;
    ui.label({x, y}, "Hands (" + std::to_string(sheet.handsInUse()) + " of " + std::to_string(yh::Character::handCount) + "): "
        + (held.empty() ? std::string("empty") : held));
    y += line;
    char weight[64];
    std::snprintf(weight, sizeof(weight), "Carrying %.0f of %.0f lb", sheet.carriedWeight(), sheet.carryCapacity(world.rules()));
    ui.label({x, y}, weight, ui.theme.textDim);
    y += line + 8;

    if (sheet.inventory.empty())
        ui.label({x, y}, "Nothing.", ui.theme.textDim);
    for (size_t i = 0; i < sheet.inventory.size() && y + 40 < area.y + area.h - 70; i++)
    {
        const yh::Item& item = sheet.inventory[i];
        std::string text = item.name;
        if (item.quantity > 1)
            text += "  x" + std::to_string(item.quantity);
        if (!item.slot.empty())
            text += "   (" + slotName(item.slot) + (yh::Character::held(item) && item.hands > 1 ? ", " + std::to_string(item.hands) + " hands" : "") + ")";
        if (item.slot.empty())
            ui.label({x + 12, y + 8}, text, ui.theme.textDim);
        else if (ui.toggle({x, y, w, 36}, text, item.equipped))
            world.act("equip", nlohmann::json{{"hero", hero}, {"item", i}, {"on", !item.equipped}}.dump());
        y += 42;
    }

    if (world.fighting())
        ui.label({x, area.y + area.h - 62}, "In a fight, changing gear takes " + std::to_string(world.equipCost(hero))
            + (world.equipCost(hero) == 1 ? " action" : " actions") + " on " + sheet.name + "'s turn.", ui.theme.textDim);
    ui.label({x, area.y + area.h - 34}, "Click an item to put it on or away.   I or Esc: close", ui.theme.textDim);
}
