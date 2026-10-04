#include "Hud.h"

#include <nlohmann/json.hpp>

#include <algorithm>
#include <cmath>

// What one hero can cast (K): slots, free hands, what they are concentrating on and each spell.
// Between fights the spells that help can be cast from here on the party; in a fight spells are
// on the action bar with everything else.
void hud::spellPanel(Hud& hud)
{
    World& world = hud.world;
    yh::Ui& ui = hud.ui;
    const yh::Rect screen = hud.renderer.bounds();
    const std::optional<size_t> acting = world.fighting() ? world.currentCreature() : std::nullopt;
    const size_t hero = acting && *acting < world.heroCount() && world.mine(*acting) ? *acting : world.leaderIndex();
    if (hero >= world.heroCount() || !world.chapter())
        return;
    const World::Creature& caster = world.creatures()[hero];
    const yh::Character& sheet = caster.sheet;
    const yh::SpellRules& rules = world.spellRules();
    const float line = ui.lineHeight();
    const bool calm = !world.fighting();
    if (hud.casting && (!calm || !world.findSpell(*hud.casting)))
        hud.casting.reset();

    const yh::Rect area{screen.w / 2 - 300, screen.h * 0.12f, 600, screen.h * 0.72f};
    ui.panel(area);
    hud.panels.push_back(area);
    const float x = area.x + 20, w = area.w - 40, bottom = area.y + area.h;
    float y = area.y + 16;
    ui.label({x, y}, sheet.name + ": spells", ui.theme.accent);
    y += line + 4;

    std::string slots;
    for (int level = 1; level <= 20; level++)
        if (const auto found = sheet.resources.find(rules.slotPrefix + std::to_string(level)); found != sheet.resources.end())
            slots += (slots.empty() ? "Slots   " : "     ") + std::string("level ") + std::to_string(level) + ": "
                + std::to_string(found->second.current) + "/" + std::to_string(found->second.max);
    // Pools that spells spend instead of slots (focus points and the like), as the sheet holds them.
    std::vector<std::string> pools;
    for (const std::string& id : sheet.spells)
        if (const yh::SpellDefinition* spell = world.findSpell(id))
            for (const auto& [resource, amount] : spell->spends)
                if (std::find(pools.begin(), pools.end(), resource) == pools.end()) pools.push_back(resource);
    for (const std::string& resource : pools)
        if (const auto found = sheet.resources.find(resource); found != sheet.resources.end())
            slots += (slots.empty() ? "" : "     ") + resource + ": " + std::to_string(found->second.current) + "/" + std::to_string(found->second.max);
    ui.label({x, y}, slots.empty() ? std::string("No spell slots.") : slots);
    y += line;
    // A prepared caster: how many are ready, and whether they can be changed now.
    std::string notReady;
    const bool preparing = calm && !sheet.preparable.empty() && world.canPrepare(hero, sheet.prepared, &notReady);
    if (!sheet.preparable.empty())
    {
        ui.label({x, y}, "Prepared " + std::to_string(sheet.prepared.size()) + " of " + std::to_string(sheet.prepareLimit)
            + (preparing ? "   - choose until the next fight" : notReady.empty() ? std::string() : "   - " + notReady),
            preparing ? ui.theme.accent : ui.theme.textDim);
        y += line;
    }
    std::string state = "Free hands: " + std::to_string(std::max(0, sheet.freeHands())) + " of " + std::to_string(yh::Character::handCount);
    if (caster.concentration.active())
    {
        const yh::SpellDefinition* held = world.findSpell(caster.concentration.spell);
        state += "     Concentrating on " + (held ? held->name() : caster.concentration.spell);
    }
    ui.label({x, y}, state, ui.theme.textDim);
    y += line + 12;

    if (hud.casting)
    {
        const yh::SpellDefinition& spell = *world.findSpell(*hud.casting);
        ui.label({x, y}, "Cast " + spell.name() + ": choose who", ui.theme.accent);
        y += line + 8;
        bool any = false;
        for (size_t target = 0; target < world.heroCount() && y + 42 < bottom - 56; target++)
        {
            if (!world.canCast(hero, spell.id(), target))
                continue;
            any = true;
            const yh::Character& who = world.creatures()[target].sheet;
            if (ui.button({x, y, w, 36}, who.name + "  HP " + std::to_string(who.hp) + "/" + std::to_string(who.maxHp()), world.mine(hero)))
            {
                world.act("cast", nlohmann::json{{"hero", hero}, {"spell", spell.id()}, {"target", target}}.dump());
                hud.casting.reset();
                return;
            }
            y += 42;
        }
        if (!any)
            ui.label({x, y}, "Nobody in clear reach it can help.", ui.theme.textDim);
        if (ui.button({x + w - 100, bottom - 50, 100, 36}, "Cancel"))
            hud.casting.reset();
        return;
    }

    // What it can cast, then what it could prepare instead.
    std::vector<std::string> shown = sheet.spells;
    for (const std::string& id : sheet.preparable)
        if (std::find(shown.begin(), shown.end(), id) == shown.end()) shown.push_back(id);
    if (shown.empty())
        ui.label({x, y}, "Knows no spells.", ui.theme.textDim);
    const float rowHeight = line * 2 + 14;
    const size_t rows = static_cast<size_t>(std::max(1.0f, std::floor((bottom - 100 - y) / rowHeight)));
    const size_t pages = std::max<size_t>(1, (shown.size() + rows - 1) / rows);
    hud.inventoryPage = std::min(hud.inventoryPage, pages - 1);
    for (size_t i = hud.inventoryPage * rows; i < shown.size() && i < (hud.inventoryPage + 1) * rows; i++, y += rowHeight)
    {
        const yh::SpellDefinition* spell = world.findSpell(shown[i]);
        if (!spell)
        {
            ui.label({x, y}, shown[i] + " (not in this ruleset)", ui.theme.textDim);
            continue;
        }
        const bool isPrepared = std::find(sheet.prepared.begin(), sheet.prepared.end(), spell->id()) != sheet.prepared.end();
        const bool preparable = std::find(sheet.preparable.begin(), sheet.preparable.end(), spell->id()) != sheet.preparable.end();
        if (preparable && preparing)
        {
            // One click swaps it in or out; the world checks the limit.
            std::vector<std::string> next = sheet.prepared;
            if (isPrepared)
                std::erase(next, spell->id());
            else
                next.push_back(spell->id());
            const bool allowed = world.canPrepare(hero, next);
            if (ui.button({x + w - 196, y + 2, 112, 34}, isPrepared ? "Unprepare" : "Prepare", allowed && world.mine(hero)))
            {
                world.act("prepare", nlohmann::json{{"hero", hero}, {"spells", next}}.dump());
                return;
            }
        }
        if (preparable && !isPrepared)
        {
            ui.label({x, y}, spell->name() + "   level " + std::to_string(spell->level) + ", not prepared", ui.theme.textDim);
            continue;
        }
        const yh::ActionDefinition& action = spell->action;
        ui.label({x, y}, spell->name() + "   " + (spell->level == 0 ? std::string("cantrip") : "level " + std::to_string(spell->level))
            + ", " + std::to_string(spell->hands) + (spell->hands == 1 ? " hand, " : " hands, ")
            + std::to_string(action.cost) + (action.cost == 1 ? " action" : " actions") + (spell->concentration ? ", concentration" : "")
            + (spell->spends.empty() ? std::string() : ", " + std::to_string(spell->spends.begin()->second) + " " + spell->spends.begin()->first));
        std::string why;
        const bool ready = yh::canCast(sheet, *spell, rules, &why);
        const std::string detail = !ready ? "Can't cast now: " + why
            : action.description.size() > 72 ? action.description.substr(0, 69) + "..." : action.description;
        ui.label({x + 12, y + line}, detail, ready ? ui.theme.textDim : ui.theme.bad);
        // Between fights: the ones that can be cast on the party.
        if (!calm)
            continue;
        bool offered = false;
        for (size_t target = 0; target < world.heroCount() && !offered; target++)
            offered = world.canCast(hero, spell->id(), target);
        if (offered && ui.button({x + w - 76, y + 2, 76, 34}, "Cast", world.mine(hero)))
        {
            if (action.target == yh::ActionDefinition::Target::Self)
                world.act("cast", nlohmann::json{{"hero", hero}, {"spell", spell->id()}, {"target", hero}}.dump());
            else
                hud.casting = spell->id();
            return;
        }
    }
    if (pages > 1)
    {
        if (ui.button({x, bottom - 92, 110, 32}, "Previous", hud.inventoryPage > 0)) --hud.inventoryPage;
        if (ui.button({x + 118, bottom - 92, 90, 32}, "Next", hud.inventoryPage + 1 < pages)) ++hud.inventoryPage;
    }
    ui.label({x, bottom - 34}, calm ? "Spells that harm are cast in a fight, from the action bar.   K or Esc: close"
        : "In a fight, cast from the action bar on this hero's turn.   K or Esc: close", ui.theme.textDim);
}
