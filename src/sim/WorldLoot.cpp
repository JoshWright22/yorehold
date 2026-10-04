// What lies on the map to be taken: containers and what dead enemies leave, and how it is stored.

#include "World.h"

#include <nlohmann/json.hpp>

#include <algorithm>
#include <cstdlib>
#include <stdexcept>

std::string World::coinText(int copper)
{
    const int gold = copper / 100, silver = copper / 10 % 10, rest = copper % 10;
    std::string text;
    auto add = [&](int amount, const char* coin) {
        if (amount > 0)
            text += (text.empty() ? "" : " ") + std::to_string(amount) + " " + coin;
    };
    add(gold, "gp");
    add(silver, "sp");
    add(rest, "cp");
    return text.empty() ? "0 cp" : text;
}

void World::addTo(yh::Character& sheet, yh::Item item)
{
    item.equipped = false;
    // Things that are only carried stack; anything worn or held stays its own entry.
    if (item.slot.empty())
        for (yh::Item& have : sheet.inventory)
            if (have.id == item.id && have.slot.empty())
            {
                yh::Item existing = have, incoming = item;
                existing.quantity = incoming.quantity = 1;
                if (yh::Compendium::itemToJson(existing) != yh::Compendium::itemToJson(incoming)) continue;
                have.quantity += item.quantity;
                return;
            }
    sheet.inventory.push_back(std::move(item));
}

std::string World::magicLimitText(size_t hero) const
{
    return creatures_[hero].sheet.name + " already carries " + std::to_string(creatures_[hero].sheet.magicItems()) + " magic items of the "
        + std::to_string(rules_.magicItemLimit) + " allowed. Give one up first.";
}

void World::fillContainers()
{
    piles_.clear();
    // Its own dice, so a chapter gaining a container doesn't change how its fights roll.
    yh::Random dice(seed_ ^ 0xc0ffeeull);
    for (size_t i = 0; i < chapter_->containers.size(); i++)
    {
        const Chapter::Container& container = chapter_->containers[i];
        Pile pile{container.name, container.at, container.coins, {}, static_cast<int>(i)};
        for (const std::string& id : container.items)
            pile.items.push_back(*chapter_->compendium.item(id)); // Chapter::load checked the ids
        const yh::LootRoll found = yh::rollLoot(container.loot, dice);
        pile.coins += found.coins;
        for (yh::Item& item : chapter_->compendium.lootItems(found))
            pile.items.push_back(std::move(item));
        for (yh::Item& item : pile.items)
            item.equipped = false;
        piles_.push_back(std::move(pile));
    }
}

void World::dropLoot()
{
    yh::Random dice(seed_ ^ 0x100700ull ^ (static_cast<uint64_t>(fights_) << 32));
    for (size_t i = heroCount_; i < creatures_.size(); i++)
    {
        Creature& c = creatures_[i];
        // Only the dead leave their things: not those who got away, gave up, or never woke.
        if (c.dropped || !c.sheet.down() || c.fled || c.surrendered || c.team != 1)
            continue;
        c.dropped = true;
        Pile pile{c.sheet.name, cellOf(i), 0, c.sheet.inventory, -1};
        if (const yh::CreatureDefinition* definition = chapter_->compendium.creature(c.creatureId))
        {
            const yh::LootRoll found = yh::rollLoot(definition->loot, dice);
            pile.coins = found.coins;
            for (yh::Item& item : chapter_->compendium.lootItems(found))
                pile.items.push_back(std::move(item));
        }
        for (yh::Item& item : pile.items)
            item.equipped = false;
        if (!pile.empty())
            piles_.push_back(std::move(pile));
    }
}

std::optional<size_t> World::pileNear(size_t hero) const
{
    if (hero >= heroCount_)
        return std::nullopt;
    const yh::Cell at = cellOf(hero);
    for (size_t i = 0; i < piles_.size(); i++)
        if (!piles_[i].empty() && std::abs(piles_[i].at.x - at.x) <= 1 && std::abs(piles_[i].at.y - at.y) <= 1)
            return i;
    return std::nullopt;
}

nlohmann::json World::pilesJson() const
{
    nlohmann::json piles = nlohmann::json::array();
    for (const Pile& pile : piles_)
    {
        // Items are written as a sheet writes its inventory, so the two can't drift apart.
        yh::Character holder;
        holder.inventory = pile.items;
        piles.push_back({{"name", pile.name}, {"at", {pile.at.x, pile.at.y}}, {"coins", pile.coins}, {"container", pile.container},
            {"items", nlohmann::json::parse(holder.toJson()).at("inventory")}});
    }
    return piles;
}

std::vector<World::Pile> World::pilesFrom(const nlohmann::json& saved) const
{
    if (!saved.is_array() || saved.size() > 10000)
        throw std::runtime_error("saved loot is not a list");
    std::vector<Pile> piles;
    for (const nlohmann::json& entry : saved)
    {
        Pile pile;
        pile.name = entry.at("name").get<std::string>();
        pile.at = {entry.at("at").at(0).get<int>(), entry.at("at").at(1).get<int>()};
        pile.coins = entry.at("coins").get<int>();
        pile.container = entry.value("container", -1);
        std::string error;
        const std::optional<yh::Character> holder = yh::Character::fromJson(nlohmann::json{{"inventory", entry.at("items")}}.dump(), &error);
        if (!holder || pile.coins < 0 || !map().inside(pile.at) || pile.container < -1 || pile.container >= static_cast<int>(chapter_->containers.size()))
            throw std::runtime_error("saved loot doesn't fit the chapter");
        pile.items = holder->inventory;
        for (yh::Item& item : pile.items)
            item.equipped = false;
        piles.push_back(std::move(pile));
    }
    return piles;
}
