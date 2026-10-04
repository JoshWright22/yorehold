#include "Hud.h"

#include <nlohmann/json.hpp>

#include <cctype>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <limits>

namespace
{

constexpr size_t coinsPicked = std::numeric_limits<size_t>::max(); // Hud::giving when it is the coins being handed over

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

std::string itemLabel(const yh::Item& item)
{
    std::string text = item.name;
    if (item.quantity > 1)
        text += "  x" + std::to_string(item.quantity);
    if (item.magic)
        text += "  *magic*";
    if (!item.slot.empty())
        text += "   (" + slotName(item.slot) + (yh::Character::held(item) && item.hands > 1 ? ", " + std::to_string(item.hands) + " hands" : "") + ")";
    return text;
}

}

// What one hero carries (I): click an item to put it on or away. Between fights that is free, and
// items and coins can be handed to another hero; in a fight it is the hero's own turn and an Interact.
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
    const bool calm = !world.fighting();
    if (hud.consuming && (hud.consuming->first != hero || hud.consuming->second >= sheet.inventory.size()
        || !sheet.inventory[hud.consuming->second].use)) hud.consuming.reset();
    if (!calm || (hud.giving && *hud.giving != coinsPicked && *hud.giving >= sheet.inventory.size()))
        hud.giving.reset();

    const yh::Rect area{screen.w / 2 - 280, screen.h * 0.12f, 560, screen.h * 0.72f};
    ui.panel(area);
    hud.panels.push_back(area);
    const float x = area.x + 20, w = area.w - 40, giveWidth = 76;
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
    const int weighed = sheet.encumbrance(world.rules());
    std::string carrying = std::string(weight) + (weighed == 2 ? ": can't move" : weighed == 1 ? ": slowed" : "");
    if (world.rules().magicItemLimit > 0 && sheet.magicItems() > 0)
        carrying += "     Magic items " + std::to_string(sheet.magicItems()) + " of " + std::to_string(world.rules().magicItemLimit);
    ui.label({x, y + 6}, carrying + "     Coins: " + World::coinText(sheet.coins), weighed ? ui.theme.bad : ui.theme.textDim);
    if (calm && sheet.coins > 0 && ui.button({x + w - giveWidth, y, giveWidth, 30}, "Give"))
        hud.giving = coinsPicked;
    y += line + 14;

    if (hud.consuming)
    {
        const size_t item = hud.consuming->second;
        ui.label({x, y}, "Use " + sheet.inventory[item].name + ": choose a target", ui.theme.accent);
        y += line + 8;
        std::vector<size_t> targets;
        for (size_t target = 0; target < world.creatures().size(); target++)
            if (world.canConsume(hero, item, target)) targets.push_back(target);
        const size_t rows = static_cast<size_t>(std::max(1.0f, std::floor((area.y + area.h - 64 - y) / 42)));
        const size_t pages = std::max<size_t>(1, (targets.size() + rows - 1) / rows);
        hud.inventoryPage = std::min(hud.inventoryPage, pages - 1);
        if (targets.empty()) ui.label({x, y}, "No valid target in reach, or not enough actions.", ui.theme.textDim);
        for (size_t i = hud.inventoryPage * rows; i < targets.size() && i < (hud.inventoryPage + 1) * rows; i++, y += 42)
        {
            const size_t target = targets[i];
            const auto& who = world.creatures()[target].sheet;
            if (ui.button({x, y, w, 36}, who.name + "  HP " + std::to_string(who.hp) + "/" + std::to_string(who.maxHp()), world.mine(hero)))
            {
                world.act("consume", nlohmann::json{{"hero", hero}, {"item", item}, {"target", target}}.dump());
                hud.consuming.reset();
                hud.inventoryPage = 0;
                return;
            }
        }
        const float bottom = area.y + area.h - 50;
        if (ui.button({x, bottom, 110, 36}, "Previous", hud.inventoryPage > 0)) --hud.inventoryPage;
        if (ui.button({x + 118, bottom, 90, 36}, "Next", hud.inventoryPage + 1 < pages)) ++hud.inventoryPage;
        if (ui.button({x + w - 100, bottom, 100, 36}, "Cancel")) { hud.consuming.reset(); hud.inventoryPage = 0; }
        return;
    }

    if (sheet.inventory.empty())
        ui.label({x, y}, "Nothing.", ui.theme.textDim);
    const size_t rows = static_cast<size_t>(std::max(1.0f, std::floor((area.y + area.h - 150 - y) / 42)));
    const size_t pages = std::max<size_t>(1, (sheet.inventory.size() + rows - 1) / rows);
    hud.inventoryPage = std::min(hud.inventoryPage, pages - 1);
    for (size_t i = hud.inventoryPage * rows; i < sheet.inventory.size() && i < (hud.inventoryPage + 1) * rows; i++)
    {
        const yh::Item& item = sheet.inventory[i];
        const float rowWidth = calm ? w - giveWidth - 8 : w;
        if (item.use)
        {
            if (ui.button({x, y, rowWidth, 36}, "Use: " + itemLabel(item) + " (" + std::to_string(item.use->cost) + (item.use->cost == 1 ? " action)" : " actions)"), world.mine(hero)))
            {
                hud.giving.reset();
                hud.consuming = std::pair(hero, i);
                hud.inventoryPage = 0;
                return;
            }
        }
        else if (item.slot.empty())
            ui.label({x + 12, y + 8}, itemLabel(item), ui.theme.textDim);
        else if (ui.toggle({x, y, rowWidth, 36}, itemLabel(item), item.equipped))
            world.act("equip", nlohmann::json{{"hero", hero}, {"item", i}, {"on", !item.equipped}}.dump());
        if (calm && ui.button({x + w - giveWidth, y, giveWidth, 36}, "Give"))
        {
            hud.giving = i;
            hud.consuming.reset();
        }
        y += 42;
    }

    // Handing something over: pick who gets it.
    const float bottom = area.y + area.h;
    if (pages > 1)
    {
        if (ui.button({x, bottom - 148, 110, 32}, "Previous", hud.inventoryPage > 0)) --hud.inventoryPage;
        if (ui.button({x + 118, bottom - 148, 90, 32}, "Next", hud.inventoryPage + 1 < pages)) ++hud.inventoryPage;
        ui.label({x + 228, bottom - 142}, "Page " + std::to_string(hud.inventoryPage + 1) + "/" + std::to_string(pages), ui.theme.textDim);
    }
    if (hud.giving)
    {
        const bool coins = *hud.giving == coinsPicked;
        ui.label({x, bottom - 104}, "Give " + (coins ? World::coinText(sheet.coins) : sheet.inventory[*hud.giving].name) + " to:", ui.theme.accent);
        float bx = x;
        for (size_t other = 0; other < world.heroCount(); other++)
        {
            if (other == hero || world.creatures()[other].sheet.down())
                continue;
            if (ui.button({bx, bottom - 78, 116, 34}, world.creatures()[other].sheet.name))
            {
                nlohmann::json give{{"from", hero}, {"to", other}};
                if (coins)
                    give["coins"] = sheet.coins;
                else
                    give["item"] = *hud.giving;
                world.act("give", give.dump());
                hud.giving.reset();
                return; // the inventory just changed under this frame's rows
            }
            bx += 124;
        }
        if (ui.button({x + w - 96, bottom - 78, 96, 34}, "Cancel"))
            hud.giving.reset();
    }
    else if (!calm)
        ui.label({x, bottom - 62}, "In a fight, changing gear takes " + std::to_string(world.equipCost(hero))
            + (world.equipCost(hero) == 1 ? " action" : " actions") + " on " + sheet.name + "'s turn.", ui.theme.textDim);
    ui.label({x, bottom - 34}, "Click gear to equip, or Use to consume.   I or Esc: close", ui.theme.textDim);
}

void hud::lootPanel(Hud& hud)
{
    World& world = hud.world;
    yh::Ui& ui = hud.ui;
    const yh::Rect screen = hud.renderer.bounds();
    const size_t hero = world.leaderIndex();
    if (world.fighting() || world.talk() || hero >= world.heroCount())
    {
        hud.looting.reset();
        return;
    }
    // Walking away, or emptying it, closes it.
    if (hud.looting)
    {
        const yh::Cell at = world.cellOf(hero);
        const bool there = *hud.looting < world.piles().size() && !world.piles()[*hud.looting].empty()
            && std::abs(world.piles()[*hud.looting].at.x - at.x) <= 1 && std::abs(world.piles()[*hud.looting].at.y - at.y) <= 1;
        if (!there)
            hud.looting.reset();
    }
    if (!hud.looting)
    {
        const std::optional<size_t> near = world.pileNear(hero);
        if (!near)
            return;
        const yh::Rect button{screen.w / 2 - 170, screen.h - 112, 340, 40};
        hud.panels.push_back(button);
        if (ui.button(button, "Open " + world.piles()[*near].name + " (E)"))
            hud.looting = near;
        return;
    }

    const World::Pile& pile = world.piles()[*hud.looting];
    const float rows = static_cast<float>(pile.items.size() + (pile.coins > 0 ? 1 : 0));
    const yh::Rect area{screen.w / 2 - 240, screen.h * 0.2f, 480, std::min(screen.h * 0.6f, 150 + rows * 42)};
    ui.panel(area);
    hud.panels.push_back(area);
    const float x = area.x + 20, w = area.w - 40;
    float y = area.y + 16;
    ui.label({x, y}, pile.name, ui.theme.accent);
    ui.label({x + 200, y}, world.creatures()[hero].sheet.name + " takes what you click", ui.theme.textDim);
    y += ui.lineHeight() + 8;
    auto take = [&](nlohmann::json what) {
        what["hero"] = hero;
        what["pile"] = *hud.looting;
        world.act("loot", what.dump());
    };
    if (pile.coins > 0)
    {
        if (ui.button({x, y, w, 36}, World::coinText(pile.coins)))
        {
            take({{"coins", true}});
            return;
        }
        y += 42;
    }
    for (size_t i = 0; i < pile.items.size() && y + 42 < area.y + area.h - 56; i++)
    {
        if (ui.button({x, y, w, 36}, itemLabel(pile.items[i])))
        {
            take({{"item", i}});
            return; // the list just changed under this frame's rows
        }
        y += 42;
    }
    const float half = (w - 8) / 2;
    if (ui.button({x, area.y + area.h - 50, half, 36}, "Take all (E)"))
        take({{"all", true}});
    else if (ui.button({x + half + 8, area.y + area.h - 50, half, 36}, "Close (Esc)"))
        hud.looting.reset();
}
