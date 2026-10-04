#include "Hud.h"

#include <algorithm>
#include <cmath>

void hud::merchantPanel(Hud& hud)
{
    World& world = hud.world;
    yh::Ui& ui = hud.ui;
    const size_t hero = world.leaderIndex();
    const yh::Rect screen = hud.renderer.bounds();
    if (hud.trading && !world.canTrade(hero, *hud.trading))
        hud.trading.reset();
    if (!hud.trading)
    {
        if (hud.looting) return;
        const auto near = world.merchantNear(hero);
        if (!near) return;
        const yh::Rect button{screen.w / 2 - 170, screen.h - 158, 340, 40};
        hud.panels.push_back(button);
        if (ui.button(button, "Trade with " + world.chapter()->npcs[*near].name + " (E)"))
        {
            hud.trading = near;
            hud.tradePage = 0;
        }
        return;
    }

    const size_t npc = *hud.trading;
    const yh::Merchant& shop = *world.merchant(npc);
    const yh::Character& sheet = world.creatures()[hero].sheet;
    const float width = std::min(900.0f, screen.w - 40);
    const yh::Rect area{(screen.w - width) / 2, screen.h * 0.12f, width, screen.h * 0.74f};
    hud.panels.push_back(area);
    ui.panel(area);
    const float x = area.x + 20, half = (area.w - 48) / 2, right = x + half + 8;
    float y = area.y + 16;
    const float line = ui.lineHeight();
    ui.label({x, y}, "Trade with " + world.chapter()->npcs[npc].name, ui.theme.accent);
    y += line + 6;
    ui.label({x, y}, "Buy one   |   Merchant: " + World::coinText(shop.coins));
    ui.label({right, y}, "Sell one   |   " + sheet.name + ": " + World::coinText(sheet.coins));
    y += line + 12;
    const size_t rows = static_cast<size_t>(std::max(1.0f, std::floor((area.y + area.h - 100 - y) / 42)));
    const size_t count = std::max(shop.inventory.size(), sheet.inventory.size());
    const size_t pages = std::max<size_t>(1, (count + rows - 1) / rows);
    hud.tradePage = std::min(hud.tradePage, pages - 1);
    const size_t first = hud.tradePage * rows;
    auto trade = [&](const char* type, size_t item) {
        world.act(type, nlohmann::json{{"hero", hero}, {"npc", npc}, {"item", item}}.dump());
    };
    auto label = [&](const yh::Item& item, int price) {
        return item.name + (item.quantity > 1 ? " x" + std::to_string(item.quantity) : "")
            + (item.magic ? " *" : "") + "   " + (price < 0 ? "no offer" : World::coinText(price));
    };
    for (size_t row = 0; row < rows; row++, y += 42)
    {
        const size_t index = first + row;
        if (index < shop.inventory.size() && ui.button({x, y, half, 36}, label(shop.inventory[index], shop.buyPrice(shop.inventory[index])), world.mine(hero)))
        {
            trade("buy", index);
            return;
        }
        if (index < sheet.inventory.size() && ui.button({right, y, half, 36},
            label(sheet.inventory[index], shop.sellPrice(sheet.inventory[index])) + (sheet.inventory[index].equipped ? " (worn)" : ""), world.mine(hero)))
        {
            trade("sell", index);
            return;
        }
    }
    const float bottom = area.y + area.h;
    ui.label({x, bottom - 90}, "Put worn items away in Gear (I) before selling. Prices are per item.", ui.theme.textDim);
    ui.label({x, bottom - 65}, "Page " + std::to_string(hud.tradePage + 1) + " of " + std::to_string(pages), ui.theme.textDim);
    if (ui.button({right, bottom - 56, 85, 36}, "Previous", hud.tradePage > 0)) --hud.tradePage;
    if (ui.button({right + 93, bottom - 56, 70, 36}, "Next", hud.tradePage + 1 < pages)) ++hud.tradePage;
    if (ui.button({area.x + area.w - 146, bottom - 56, 126, 36}, "Close (Esc)")) hud.trading.reset();
}
