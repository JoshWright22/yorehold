#include "WorldFixture.h"

#include <functional>

namespace
{

bool yard(WorldFixture& world, bool wall = false, const std::string& config = {}, std::string* error = nullptr)
{
    std::map<std::string, std::string> files{
        {"chapters/position-yard/chapter.json", R"({"id":"position-yard","title":"Yard","map":"map.json",
            "party":[{"name":"Ana","class":"fighter","at":[2,3]},{"name":"Bo","class":"cleric","at":[4,4]}],
            "encounters":[{"id":"yard","creatures":[{"creature":"goblin","name":"Gik","at":[3,3]},
                {"creature":"goblin","name":"Gok","at":[6,5]}]}]})"},
        {"rulesets/yorehold/actions/shoot.json", R"({"id":"shoot","cost":0,"target":{"kind":"creature","side":"enemy","range":6},
            "effects":[{"do":"roll","kind":"attack","steps":[]}]})"},
        {"rulesets/yorehold/actions/position-check.json", R"({"id":"position-check","cost":0,"target":{"kind":"creature","side":"enemy","range":6},
            "effects":[{"do":"damage","dice":1,"ifFlag":"offGuard"}]})"}
    };
    nlohmann::json map{{"name", "Yard"}, {"tiles", {{"floor", {{"art", "grass"}}},
        {"wall", {{"art", "wall"}, {"walkable", false}, {"blocksSight", true}}}}},
        {"legend", {{".", "floor"}, {"#", "wall"}}}, {"layers", {{{"name", "ground"}, {"rows",
            {"########", "#......#", wall ? "#..#...#" : "#......#", "#......#", "#......#", "#......#", "#......#", "########"}}}}}};
    files["chapters/position-yard/map.json"] = map.dump();
    if (!config.empty()) files["rulesets/yorehold/positioning.json"] = config;
    if (!world.loadJson("chapters/position-yard", files, 5, error)) return false;
    world.sheet(0).stats.setBase("dex", 2000);
    world.tokens().tokens[1].position = world.grid().center({4, 3});
    nlohmann::json positions = nlohmann::json::array();
    for (size_t i = 0; i < world.creatures().size(); i++)
    {
        world.sheet(i).stats.setBase("maxHp", 1000); world.sheet(i).hp = 1000;
        const auto at = world.tokens().tokens[i].position;
        positions.push_back({at.x, at.y});
    }
    return world.send("fight", {{"group", 0}, {"at", positions}});
}

}

void worldPositioningTests(const std::function<void(bool, const char*)>& check)
{
    WorldFixture world;
    check(yard(world), "The positioning yard loads");
    if (!world.fighting()) return;
    const int ac = world.sheet(2).armorClass(world.rules());
    check(world.isFlanked(2) && world.positionalArmorClass(2) == ac - 2
        && world.attackArmorClass(0, 2, false) == ac - 2, "Opposite foes apply the Off-guard file's AC modifier");
    check(world.send("use", {{"action", "strike"}, {"target", 2}}) && world.said("(AC " + std::to_string(ac - 2) + ")"),
        "A Strike rolls against the positional AC");
    const int hp = world.sheet(2).hp;
    check(world.send("use", {{"action", "position-check"}, {"target", 2}}) && world.sheet(2).hp == hp - 1,
        "Flanking also supplies Off-guard's flag to conditional effects");
    world.sheet(2).addCondition(world.rules(), "off-guard");
    check(world.positionalArmorClass(2) == ac - 2, "An existing Off-guard condition does not stack with flanking");
    world.sheet(2).removeCondition("off-guard");
    world.tokens().tokens[1].position = world.grid().center({4, 4});
    const int before = world.sheet(2).hp;
    check(!world.isFlanked(2) && world.positionalArmorClass(2) == ac && !world.sheet(2).hasCondition("off-guard")
        && world.send("use", {{"action", "position-check"}, {"target", 2}}) && world.sheet(2).hp == before,
        "Leaving the opposite square ends positional Off-guard without a lingering condition");
    world.tokens().tokens[1].position = world.grid().center({4, 3});
    world.sheet(1).hp = 0;
    check(!world.isFlanked(2), "A downed ally cannot flank");
    world.sheet(1).hp = 1000;
    world.tokens().tokens[2].position = world.grid().center({5, 3});
    check(world.coverFrom(0, 2) == yh::Cover::Half && world.attackArmorClass(0, 2, true) == ac + 2
        && world.attackArmorClass(0, 2, false) == ac, "A standing body supplies half cover to ranged attacks, without affecting melee");
    check(world.send("use", {{"action", "shoot"}, {"target", 2}}) && world.said("(AC " + std::to_string(ac + 2) + ")"),
        "An attack effect includes the half-cover bonus");
    world.sheet(1).hp = 0;
    check(world.coverFrom(0, 2) == yh::Cover::None, "Downed bodies do not supply cover");

    WorldFixture terrain;
    check(yard(terrain, true), "The wall-cover yard loads");
    if (!terrain.fighting()) return;
    terrain.tokens().tokens[2].position = terrain.grid().center({5, 2});
    check(terrain.coverFrom(0, 2) == yh::Cover::Half, "A wall obscuring two corners supplies half cover");
    terrain.tokens().tokens[2].position = terrain.grid().center({4, 2});
    const int targetAc = terrain.sheet(2).armorClass(terrain.rules());
    check(terrain.coverFrom(0, 2) == yh::Cover::ThreeQuarters && terrain.attackArmorClass(0, 2, true) == targetAc + 4
        && terrain.send("use", {{"action", "shoot"}, {"target", 2}}) && terrain.said("(AC " + std::to_string(targetAc + 4) + ")"),
        "Three obscured corners add four AC while one clear ray still allows the shot");
    terrain.tokens().tokens[0].position = terrain.grid().center({2, 2});
    check(terrain.coverFrom(0, 2) == yh::Cover::Full && !terrain.send("use", {{"action", "shoot"}, {"target", 2}}),
        "Full terrain cover refuses the ranged action");

    WorldFixture disabled, invalid;
    check(yard(disabled, false, R"({"enabled":false})") && !disabled.isFlanked(2)
        && disabled.positionalArmorClass(2) == disabled.sheet(2).armorClass(disabled.rules()), "A ruleset can disable positioning");
    std::string error;
    check(!yard(invalid, false, R"({"flankingCondition":"missing"})", &error)
        && error.find("positioning.json") != std::string::npos && error.find("missing") != std::string::npos,
        "A missing positional condition fails with a named file and reason");
}
