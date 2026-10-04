#include "WorldFixture.h"

#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;

std::map<std::string, std::string> campFiles()
{
    auto chapter = nlohmann::json::parse(R"({"id":"camp","title":"Camp","map":"map.json",
        "party":[{"name":"Ana","class":"fighter","at":[3,3]},{"name":"Bo","class":"cleric","at":[3,4]}],
        "containers":[{"id":"supplies-chest","name":"Supply Chest","at":[5,3],"items":["supplies","supplies","supplies"]}]})");
    return {{"chapters/camp/chapter.json", chapter.dump()},
        {"chapters/camp/map.json", R"({"name":"Camp","tiles":{"floor":{"art":"grass"},
        "wall":{"art":"wall","walkable":false,"blocksSight":true}},"legend":{".":"floor","#":"wall"},
        "layers":[{"name":"ground","rows":["########","#......#","#......#","#......#",
        "#......#","#......#","#......#","########"]}]})"},
        {"items/supplies.json", R"({"id":"supplies","name":"Supplies","slot":"none","hands":0,
        "weight":1,"value":10,"quantity":40})"}};
}

bool load(WorldFixture& world, std::string* error = nullptr)
{
    if (!world.loadJson("chapters/camp", campFiles(), 5, error)) return false;
    for (size_t i = 0; i < world.creatures().size(); i++)
    {
        world.sheet(i).stats.setBase("maxHp", 100);
        world.sheet(i).hp = 50;  // Start at half health
    }
    return true;
}

void resting(const Check& check)
{
    WorldFixture world;
    check(load(world), "Camp loads");

    // Take damage to get below max HP
    world.sheet(0).takeDamage(25, world.rules());
    const int hpBefore = world.sheet(0).hp;
    check(hpBefore < world.sheet(0).maxHp(), "Hero takes damage");

    // Add supplies to stash
    yh::Item supplies;
    supplies.id = "supplies";
    supplies.name = "Supplies";
    supplies.quantity = 40;
    world.stash().push_back(supplies);

    // Take a long rest
    auto restDef = world.rules().rests[1];  // Long rest
    check(restDef.id == "long", "Long rest is available");

    // Test: long rest consumes supplies
    int suppliesBefore = 0;
    for (const auto& item : world.stash())
        if (item.id == "supplies") suppliesBefore += item.quantity;

    world.send("rest", {{"id", restDef.id}});

    int suppliesAfter = 0;
    for (const auto& item : world.stash())
        if (item.id == "supplies") suppliesAfter += item.quantity;

    check(suppliesBefore - suppliesAfter == restDef.supplyCost,
        "Long rest consumes the specified supply cost");

    // Test: HP is restored
    check(world.sheet(0).hp > hpBefore && world.sheet(0).hp >= world.sheet(0).maxHp(),
        "Long rest restores HP to maximum");
}

void revivingDead(const Check& check)
{
    WorldFixture world;
    check(load(world), "Camp loads");

    // Kill a hero
    world.sheet(0).hp = 0;
    world.sheet(0).death.dead = true;
    check(world.sheet(0).death.dead, "Hero dies");

    // Add supplies
    yh::Item supplies;
    supplies.id = "supplies";
    supplies.quantity = 40;
    world.stash().push_back(supplies);

    // Take a long rest (which has reviveDead enabled)
    auto restDef = world.rules().rests[1];
    check(restDef.reviveDead, "Long rest revives dead");

    world.send("rest", {{"id", restDef.id}});

    check(!world.sheet(0).death.dead && world.sheet(0).hp > 0,
        "Long rest with reviveDead restores dead heroes");
}

void insufficientSupplies(const Check& check)
{
    WorldFixture world;
    check(load(world), "Camp loads");

    // Add insufficient supplies
    yh::Item supplies;
    supplies.id = "supplies";
    supplies.quantity = 10;  // Only 10, need 40
    world.stash().push_back(supplies);

    auto restDef = world.rules().rests[1];  // Long rest
    const size_t logBefore = world.log.size();

    world.send("rest", {{"id", restDef.id}});

    // Rest should be refused
    check(world.log.size() > logBefore && world.said("Not enough supplies"),
        "Rest is refused without sufficient supplies");
}

void stashPersists(const Check& check)
{
    WorldFixture world;
    check(load(world), "Camp loads");

    // Add items to stash
    yh::Item supplies;
    supplies.id = "supplies";
    supplies.quantity = 100;
    world.stash().push_back(supplies);

    world.setStashCoins(500);

    // Save and restore
    std::string state = world.stateJson();
    WorldFixture world2;
    check(world2.loadJson("chapters/camp", campFiles(), 5) && world2.restoreState(state),
        "State with stash saves and loads");

    check(world2.stashCoins() == 500, "Stash coins persist");
    int suppliesAfter = 0;
    for (const auto& item : world2.stash())
        if (item.id == "supplies") suppliesAfter += item.quantity;
    check(suppliesAfter == 100, "Stash items persist");
}

}  // namespace

void runnableTests()
{
    Check check = [](bool ok, const char* desc) {
        if (!ok) throw std::runtime_error(desc);
    };
    resting(check);
    revivingDead(check);
    insufficientSupplies(check);
    stashPersists(check);
}
