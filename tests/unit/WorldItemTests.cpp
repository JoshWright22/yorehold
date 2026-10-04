#include "WorldFixture.h"

#include <algorithm>
#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;

std::map<std::string, std::string> yardFiles()
{
    return {{"chapters/item-yard/chapter.json", R"({"id":"item-yard","title":"Yard","map":"map.json",
        "party":[{"name":"Ana","class":"fighter","at":[3,3]},{"name":"Bo","class":"cleric","at":[3,4]}],
        "encounters":[{"id":"yard","creatures":[{"creature":"goblin","name":"Gik","at":[4,3]}]}]})"},
        {"chapters/item-yard/map.json", R"({"name":"Yard","tiles":{"floor":{"art":"grass"},
        "wall":{"art":"wall","walkable":false,"blocksSight":true}},"legend":{".":"floor","#":"wall"},
        "layers":[{"name":"ground","rows":["########","#......#","#......#","#......#",
        "#......#","#......#","#......#","########"]}]})"}};
}

void healing(const Check& check)
{
    WorldFixture world, peer;
    check(world.loadJson("chapters/item-yard", yardFiles(), 5) && peer.loadJson("chapters/item-yard", yardFiles(), 5), "The consumable yard loads");
    if (!world.chapter() || !peer.chapter()) return;
    const size_t item = world.sheet(0).inventory.size() - 1;
    check(world.sheet(0).inventory[item].id == "healing-potion" && world.sheet(0).inventory[item].use
        && !world.sheet(0).resources.contains("potions"), "New heroes carry an actual healing potion");
    world.sheet(0).inventory[item].quantity = 2;
    world.sheet(1).takeDamage(1000, world.rules());
    world.sheet(1).death.stable = true;
    world.sheet(1).death.successes = world.rules().death.successes;
    world.sheet(1).syncDeath(world.rules());
    check(peer.restoreState(world.snapshot()), "Consumable effects and stack counts survive a joining snapshot");
    const auto intent = nlohmann::json{{"hero", 0}, {"item", item}, {"target", 1}, {"healing", 1000}};
    std::string reason;
    const auto before = world.checksum();
    check(!world.validate(1, "consume", intent.dump(), reason) && world.checksum() == before, "A peer cannot consume another player's item");
    world.tokens().tokens[0].path = {world.grid().center({2,3})};
    check(!world.send("consume", intent), "Walking heroes must stop before using an item");
    world.tokens().tokens[0].path.clear();
    const auto command = world.validate(0, "consume", intent.dump(), reason);
    check(command && !nlohmann::json::parse(*command).contains("healing"), "The host ignores player-supplied consumable outcomes");
    if (!command) return;
    world.apply({0, 0, "consume", *command}); peer.apply({0, 0, "consume", *command});
    check(world.sheet(1).hp >= 4 && world.sheet(1).hp <= 10 && !world.sheet(1).death.stable
        && world.sheet(1).death.successes == 0 && !world.sheet(1).hasCondition("downed"), "A carried potion gets a stable ally up");
    check(world.sheet(0).inventory[item].quantity == 1 && world.checksum() == peer.checksum(), "One unit is spent and both peers roll the same healing");
    world.tokens().tokens[1].position = world.grid().center({6,6});
    const auto tooFar = world.checksum();
    check(!world.send("consume", intent) && world.checksum() == tooFar, "An out-of-reach target costs no potion or roll");
    world.tokens().tokens[1].position = world.grid().center({3,4});
    world.sheet(1).hp = 0; world.sheet(1).death.dead = true;
    check(!world.send("consume", intent) && world.sheet(0).inventory[item].quantity == 1, "Ordinary potions cannot revive the dead");
    world.sheet(1).death = {true};
    check(world.send("consume", intent) && world.sheet(0).inventory.size() == item, "The last consumed unit removes its entry");
    check(!world.send("consume", intent), "An exhausted stack cannot be used again");
    const auto saved = world.stateJson();
    check(world.restoreState(saved) && world.sheet(0).inventory.size() == item, "Consumed items stay gone after loading");
    const auto& remaining = world.sheet(1).inventory.back();
    check(remaining.use && remaining.use->effect.steps.size() == 1, "Other saved consumables retain their effects");
}

void scrolls(const Check& check)
{
    WorldFixture world, peer;
    check(world.loadJson("chapters/item-yard", yardFiles(), 7) && peer.loadJson("chapters/item-yard", yardFiles(), 7), "Two copies load the scroll yard");
    if (!world.chapter() || !peer.chapter()) return;
    for (WorldFixture* copy : {&world, &peer})
    {
        copy->sheet(0).inventory.push_back(*copy->chapter()->compendium.item("ward-scroll"));
        copy->sheet(0).inventory.push_back(*copy->chapter()->compendium.item("ember-scroll"));
        copy->sheet(0).stats.setBase("dex", 1000);
        copy->sheet(2).stats.setBase("maxHp", 100); copy->sheet(2).hp = 100;
    }
    const size_t ward = world.sheet(0).inventory.size() - 2;
    check(!world.send("consume", {{"hero",0},{"item",ward},{"target",1}}), "A self-only scroll refuses another target");
    check(!world.send("consume", {{"hero",0},{"item",ward + 1},{"target",2}}), "Hostile scrolls cannot bypass initiative between fights");
    check(world.send("consume", {{"hero",0},{"item",ward}}) && peer.send("consume", {{"hero",0},{"item",ward}})
        && world.sheet(0).tempHp >= 3 && world.sheet(0).tempHp <= 8 && world.checksum() == peer.checksum(),
        "Ward scrolls grant temporary HP through the effects interpreter");
    nlohmann::json positions = nlohmann::json::array();
    for (const auto& token : world.tokens().tokens) positions.push_back({token.position.x, token.position.y});
    check(world.send("fight", {{"group",0},{"at",positions}}) && peer.send("fight", {{"group",0},{"at",positions}})
        && world.currentCreature() == 0, "The scroll user wins initiative");
    if (world.currentCreature() != 0) return;
    const auto slots = world.sheet(0).resources;
    check(world.send("consume", {{"hero",0},{"item",ward},{"target",2}})
        && peer.send("consume", {{"hero",0},{"item",ward},{"target",2}}), "An ember scroll resolves its save and damage in combat");
    check(world.sheet(2).hp < 100 && world.sheet(2).hp >= 88 && world.encounter()->order()[world.encounter()->currentIndex()].budget.actions == 0
        && world.checksum() == peer.checksum(), "Scroll damage, action costs and consumption match on both peers");
    check(world.sheet(0).resources.size() == slots.size(), "Scrolls do not grant or spend spell slots");
    const auto before = world.checksum();
    const size_t potion = world.sheet(0).inventory.size() - 1;
    check(!world.send("consume", {{"hero",0},{"item",potion},{"target",0}}) && world.checksum() == before,
        "An empty action budget refuses a potion without consuming it");
    check(!world.send("consume", {{"hero",1},{"item",peer.sheet(1).inventory.size() - 1},{"target",1}}), "A different hero must take their own turn to consume");
    auto broken = yardFiles();
    broken["items/bad-tonic.json"] = R"({"id":"bad-tonic","use":{"effects":[{"do":"condition","id":"missing-condition"}]}})";
    WorldFixture invalid;
    std::string error;
    check(!invalid.loadJson("chapters/item-yard", broken, 1, &error) && error.find("bad-tonic") != std::string::npos,
        "An unknown condition in item effects names the item while loading");
}

}

void worldItemTests(const Check& check)
{
    healing(check);
    scrolls(check);
}
