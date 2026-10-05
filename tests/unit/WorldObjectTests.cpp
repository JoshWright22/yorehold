#include "WorldFixture.h"

#include <algorithm>
#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;

// A wall down the middle with a door and a gate in it, a lever for the gate, a chest only a key
// opens, a trap anyone would see and one nobody will.
std::map<std::string, std::string> yardFiles()
{
    return {{"chapters/obj-yard/chapter.json", R"({"id":"obj-yard","title":"Yard","map":"map.json",
        "party":[{"name":"Ana","class":"fighter","at":[3,2]},{"name":"Bo","class":"rogue","at":[2,2]}]})"},
        {"chapters/obj-yard/items/cell-key.json", R"({"id":"cell-key","name":"Cell key","weight":0,"value":0})"},
        {"chapters/obj-yard/map.json", R"({"name":"Yard","tiles":{"floor":{"art":"stone"},
        "wall":{"art":"wall","walkable":false,"blocksSight":true}},"legend":{".":"floor","#":"wall"},
        "layers":[{"name":"ground","rows":["##########","#....#...#","#........#","#....#...#",
        "#....#...#","#........#","#....#...#","##########"]}],
        "objects":[
            {"kit":"door","at":[5,2]},
            {"kit":"door","at":[5,5],"name":"the gate","tags":["link:gate"],"door":{"locked":true}},
            {"kit":"lever","at":[1,5],"tags":["link:gate","flag:gate-opened"]},
            {"kit":"locked-chest","at":[1,1],"tags":["key:cell-key"],"lock":{"dc":40},"contents":{"coins":50,"healing-potion":1}},
            {"kit":"dart-trap","at":[3,4],"trap":{"detectDc":1,"disarmDc":0}},
            {"kit":"dart-trap","at":[2,6],"name":"the hidden darts","trap":{"detectDc":60}}
        ]})"}};
}

std::optional<yh::ObjectId> objectOn(const World& world, yh::Cell cell)
{
    return world.map().objectAt(cell);
}

void put(WorldFixture& world, size_t hero, yh::Cell cell)
{
    world.tokens().tokens[hero].position = world.grid().center(cell);
    world.tokens().tokens[hero].path.clear();
}

void doorsAndLocks(const Check& check)
{
    WorldFixture world, peer;
    std::string error;
    check(world.loadJson("chapters/obj-yard", yardFiles(), 3, &error) && peer.loadJson("chapters/obj-yard", yardFiles(), 3), "The object yard loads");
    if (!world.chapter()) { std::fprintf(stderr, "%s\n", error.c_str()); return; }
    check(world.map().objects().all().size() == 6, "Every placed kit becomes a map object");
    const yh::ObjectId door = *objectOn(world, {5, 2}), gate = *objectOn(world, {5, 5}), lever = *objectOn(world, {1, 5}), chest = *objectOn(world, {1, 1});
    check(!world.walkable({5, 2}) && !world.walkable({5, 5}), "Shut doors block the way");

    put(world, 0, {3, 2});
    check(!world.send("interact", {{"hero", 0}, {"object", door}}) && world.refusal.find("too far") != std::string::npos, "A door two squares off is out of reach");
    put(world, 0, {4, 2});
    const size_t wallsShut = world.map().walls().size();
    check(world.objectNear(0) == door && world.send("interact", {{"hero", 0}, {"object", door}}), "A hero beside a door opens it");
    check(world.walkable({5, 2}) && world.map().walls().size() < wallsShut && world.said("opens the door"), "An open door lets people and sight through");
    put(world, 1, {5, 2});
    check(!world.send("interact", {{"hero", 0}, {"object", door}}) && world.refusal == "Something is in the way.", "A door doesn't close on someone in it");
    put(world, 1, {2, 2});
    check(world.send("interact", {{"hero", 0}, {"object", door}}) && !world.walkable({5, 2}) && world.map().walls().size() == wallsShut, "Closing it blocks the way again");

    // The chest: locked past any roll, so only the key opens it.
    put(world, 0, {2, 1});
    size_t pile = world.piles().size();
    for (size_t i = 0; i < world.piles().size(); i++)
        if (world.piles()[i].object == chest) pile = i;
    check(pile < world.piles().size() && world.piles()[pile].coins == 50 && world.piles()[pile].items.size() == 1, "A chest object holds its coins and items as a pile");
    check(world.pileLocked(pile) && !world.pileNear(0) && !world.send("loot", {{"hero", 0}, {"pile", pile}, {"all", true}}), "A locked chest can't be looted");
    check(world.send("interact", {{"hero", 0}, {"object", chest}}) && world.said("stays locked") && world.pileLocked(pile), "A failed check leaves the lock shut");
    world.sheet(0).inventory.push_back(*world.chapter()->compendium.item("cell-key"));
    const int coins = world.sheet(0).coins;
    check(world.send("interact", {{"hero", 0}, {"object", chest}}) && !world.pileLocked(pile) && world.said("unlocks"), "The key the lock names opens it");
    check(world.send("loot", {{"hero", 0}, {"pile", pile}, {"all", true}}) && world.sheet(0).coins == coins + 50, "An unlocked chest is looted as before");
    check(!world.objectNear(0) || world.objectNear(0) != chest, "An unlocked chest isn't something to Interact with any more");

    // The lever swings the gate, locked or not, and sets the story flag on it.
    put(world, 0, {1, 4});
    check(world.send("interact", {{"hero", 0}, {"object", lever}}) && world.map().objects().get(gate)->door->open && world.walkable({5, 5}),
        "The lever opens the gate it is linked to");
    check(world.flags().contains("gate-opened"), "A flag: tag sets its story flag when used");

    // Saves keep the objects as they were left; a co-op partner gets the same.
    const std::string saved = world.stateJson();
    check(peer.restoreState(world.snapshot()) && peer.checksum() == world.checksum() && peer.map().objects().get(gate)->door->open,
        "A joining copy gets the doors as the host has them");
    WorldFixture again;
    check(again.loadJson("chapters/obj-yard", yardFiles(), 3) && again.restoreState(saved) && again.map().objects().get(gate)->door->open
        && again.walkable({5, 5}) && !again.pileLocked(pile), "A loaded save keeps doors open and chests unlocked");
    nlohmann::json old = nlohmann::json::parse(saved);
    old.erase("objects");
    check(again.restoreState(old.dump()) && !again.map().objects().get(gate)->door->open, "A save from before map objects loads with the map's own doors");
    again.newAdventure(3);
    check(!again.map().objects().get(door)->door->open && again.map().objects().get(chest)->locked(), "A new adventure puts every object back as the map has it");
}

void traps(const Check& check)
{
    WorldFixture world;
    check(world.loadJson("chapters/obj-yard", yardFiles(), 9), "The trap yard loads");
    if (!world.chapter()) return;
    const yh::ObjectId seen = *objectOn(world, {3, 4}), hidden = *objectOn(world, {2, 6});
    put(world, 0, {3, 2});
    put(world, 1, {1, 2});
    world.step(0.1);
    check(world.map().objects().get(seen)->trap->found && world.said("spots"), "A hero whose passive score beats a nearby trap finds it");
    check(!world.map().objects().get(hidden)->trap->found, "A trap too well hidden stays unseen");
    check(!world.walkable({3, 4}), "Paths go around a trap the party knows about");
    put(world, 0, {3, 3});
    check(world.send("interact", {{"hero", 0}, {"object", seen}}) && !world.map().objects().get(seen)->armedTrap() && world.said("disarms"),
        "A found trap is disarmed with a check");
    check(world.walkable({3, 4}), "A disarmed trap is safe to walk over");

    const int hp = world.sheet(0).hp;
    put(world, 0, {2, 6});
    world.step(0.1);
    check(world.said("sets off the hidden darts") && !world.map().objects().get(hidden)->armedTrap() && world.map().objects().get(hidden)->trap->found,
        "Stepping on a hidden trap sets it off once");
    check(world.sheet(0).hp <= hp, "The trap's effect is run on the hero");
    const int after = world.sheet(0).hp;
    world.step(0.5);
    check(world.sheet(0).hp == after, "A sprung trap doesn't go off again");
    std::string reason;
    check(!world.validate(1, "trap", nlohmann::json{{"object", seen}, {"hero", 0}}.dump(), reason), "Only the host says when a trap goes off");
}

void mapFormats(const Check& check)
{
    // The text map is an import: written back in the editor's form it loads to the same map.
    GameMap::Kits kits;
    kits["door"] = *yh::Kit::fromJson(R"({"name":"Door","object":{"area":[0,0,64,64],"tags":["blocksMovement","blocksSight"],"door":{}}})");
    const std::string text = R"({"name":"T","tiles":{"floor":{"art":"stone"},"wall":{"art":"wall","walkable":false,"blocksSight":true},
        "roof":{"art":"wood","indoors":true}},"legend":{".":"floor","#":"wall","r":"roof"},
        "layers":[{"name":"ground","rows":["#####","#..r#","#...#","#####"]}],
        "objects":[{"kit":"door","at":[2,2]}],"markers":{"start":[1,1]},"lights":[{"at":[2.5,1.5],"radius":3}]})";
    std::string error;
    const std::optional<GameMap> imported = GameMap::fromJson(text, &error, kits);
    check(imported.has_value(), "A text map with objects imports");
    if (!imported) { std::fprintf(stderr, "%s\n", error.c_str()); return; }
    const std::optional<GameMap> native = GameMap::fromJson(imported->toJson(), &error);
    check(native.has_value() && nlohmann::json::parse(imported->toJson()).contains("tileMap"), "The editor's form loads without the kits it came from");
    if (!native) { std::fprintf(stderr, "%s\n", error.c_str()); return; }
    bool same = native->width() == 5 && native->height() == 4;
    for (int y = 0; y < 4; y++)
        for (int x = 0; x < 5; x++)
            same = same && native->walkable({x, y}) == imported->walkable({x, y}) && native->blocksSight({x, y}) == imported->blocksSight({x, y})
                && native->indoors({x, y}) == imported->indoors({x, y});
    check(same && !native->walkable({2, 2}) && native->indoors({3, 1}), "Both forms give the same floor, walls, roofs and doors");
    check(native->marker("start") == yh::Cell{1, 1} && native->lights().size() == 1 && native->walls().size() == imported->walls().size(),
        "Markers, lights and walls come through");
    check(!GameMap::fromJson(R"({"tiles":{"a":{}},"legend":{".":"a"},"layers":[{"rows":["."]}],"objects":[{"kit":"nope","at":[0,0]}]})", &error)
        && error.find("unknown kit") != std::string::npos, "An unknown kit is named");
    check(!GameMap::fromJson(R"({"tiles":{"a":{}},"legend":{".":"a"},"layers":[{"rows":["."]}],"objects":[{"at":[3,0]}]})", &error)
        && error.find("outside") != std::string::npos, "Objects stay on the map");
    check(!GameMap::fromJson(R"({"tiles":[{"name":"a"}],"tileMap":{"width":2,"height":2,"tileSize":64,"layers":[{"name":"g","default":5,"chunks":[]}]}})", &error),
        "A tileMap can't use tiles the map doesn't name");

    std::map<std::string, std::string> broken = yardFiles();
    broken["chapters/obj-yard/map.json"] = R"({"tiles":{"a":{}},"legend":{".":"a"},"layers":[{"rows":["....","...."]}],
        "objects":[{"kit":"dart-trap","at":[3,1],"trap":{"effect":[{"do":"condition","id":"no-such-condition"}]}}]})";
    WorldFixture invalid;
    check(!invalid.loadJson("chapters/obj-yard", broken, 1, &error) && error.find("trap") != std::string::npos, "A trap's effect is checked against the ruleset");
}

}

void worldObjectTests(const Check& check)
{
    doorsAndLocks(check);
    traps(check);
    mapFormats(check);
}
