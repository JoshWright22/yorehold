#include "WorldFixture.h"

#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;

const char* room = R"("tiles":{"floor":{"art":"stone"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
    "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":["########","#......#","#......#","#......#","#......#","########"]}])";

// Two small rooms. East of the first leads into the second and back the same way; a secret way
// back opens once its flag is set.
std::map<std::string, std::string> adventureFiles()
{
    return {
        {"adventure.json", R"({"id":"two-rooms","title":"Two Rooms","minLevel":1,"maxLevel":3,"recommendedPartySize":2,
            "chapters":["chapters/adv-a","chapters/adv-b"],
            "transitions":[{"from":"adv-a","exitMarker":"east","to":"adv-b","entryMarker":"west"},
                {"from":"adv-b","exitMarker":"west","to":"adv-a","entryMarker":"east"},
                {"from":"adv-b","exitMarker":"secret","to":"adv-a","entryMarker":"hidden","when":["found-secret"]}],
            "flags":["met-guide","found-secret"]})"},
        {"chapters/adv-a/chapter.json", R"({"id":"adv-a","title":"The First Room","localFlags":["a-local"],
            "party":[{"name":"Ana","class":"fighter","at":[2,2]},{"name":"Bo","class":"cleric","at":[2,3]}]})"},
        {"chapters/adv-a/map.json", std::string("{") + room + R"(,"markers":{"east":[6,2],"hidden":[1,4]}})"},
        {"chapters/adv-b/chapter.json", R"({"id":"adv-b","title":"The Second Room","level":2,
            "party":[{"name":"Cy","class":"fighter","at":[3,2]},{"name":"Di","class":"cleric","at":[3,3]}]})"},
        {"chapters/adv-b/map.json", std::string("{") + room + R"(,"markers":{"west":[1,2],"secret":[6,4]}})"},
    };
}

void put(WorldFixture& world, size_t hero, yh::Cell cell)
{
    world.tokens().tokens[hero].position = world.grid().center(cell);
    world.tokens().tokens[hero].path.clear();
}

void travelling(const Check& check)
{
    WorldFixture world;
    std::string error;
    check(world.loadJson("chapters/adv-a", adventureFiles(), 4, &error) && world.adventure() && world.adventure()->id == "two-rooms",
        "A chapter listed in adventure.json plays as part of that adventure");
    if (!world.adventure()) { std::fprintf(stderr, "%s\n", error.c_str()); return; }
    check(world.exitMarkerAt({6, 2}) == "east" && world.exitMarkerAt({1, 4}).empty(), "Only markers a transition leaves from are exits");

    world.setFlags({"met-guide", "a-local"});
    world.sheet(0).hp -= 3;
    world.sheet(0).coins = 77;
    const int hp = world.sheet(0).hp;
    const std::string name = world.sheet(0).name;
    std::string reason;
    put(world, 0, {6, 2});
    check(!world.validate(1, "travel", nlohmann::json{{"hero", 0}, {"marker", "east"}}.dump(), reason), "Only the host sends the party through an exit");
    world.step(0.1);
    check(world.chapter()->id == "adv-b" && world.said("goes on to The Second Room"), "Stepping onto an exit takes the party to the next chapter");
    check(world.cellOf(0) == yh::Cell{1, 2} && world.cellOf(1) != world.cellOf(0) && world.walkable(world.cellOf(1)), "The party arrives at the entry marker");
    check(world.sheet(0).name == name && world.sheet(0).hp == hp && world.sheet(0).coins == 77, "Heroes come along as they were, wounds and coins too");
    check(world.flags().contains("met-guide") && !world.flags().contains("a-local"), "Adventure flags travel; the old chapter's local flags stay behind");
    world.step(0.5);
    check(world.chapter()->id == "adv-b", "Arriving on a marker that leads back doesn't send the party straight back");

    put(world, 0, {6, 4});
    world.step(0.1);
    check(world.chapter()->id == "adv-b", "A way whose flags aren't set stays shut");
    const std::string saved = world.stateJson();
    WorldFixture peer;
    check(peer.loadJson("chapters/adv-a", adventureFiles(), 4) && peer.restoreState(world.snapshot()) && peer.chapter()->id == "adv-b"
        && peer.checksum() == world.checksum(), "A joining copy follows the host into the chapter it is in");
    world.setFlags({"found-secret"});
    world.step(0.1);
    check(world.chapter()->id == "adv-b", "Setting the flag while standing there doesn't move anyone");
    put(world, 0, {5, 4});
    world.step(0.1);
    put(world, 0, {6, 4});
    world.step(0.1);
    check(world.chapter()->id == "adv-a" && world.cellOf(0) == yh::Cell{1, 4}, "Once open, the secret way leads back to its own entry");

    WorldFixture again;
    check(again.loadJson("chapters/adv-a", adventureFiles(), 4) && again.restoreState(saved) && again.chapter()->id == "adv-b"
        && again.sheet(0).hp == hp && again.flags().contains("met-guide"), "A save made in the second chapter loads into it");
}

void checking(const Check& check)
{
    auto broken = [](const std::string& path, const std::string& text) {
        std::map<std::string, std::string> files = adventureFiles();
        files[path] = text;
        return files;
    };
    std::string error;
    WorldFixture a;
    check(!a.loadJson("chapters/adv-a", broken("adventure.json", R"({"id":"x","chapters":["chapters/adv-a","chapters/adv-b"],
        "transitions":[{"from":"adv-a","exitMarker":"nowhere","to":"adv-b","entryMarker":"west"}]})"), 1, &error)
        && error.find("no marker") != std::string::npos, "A transition from a marker the map doesn't have is refused");
    WorldFixture b;
    check(!b.loadJson("chapters/adv-a", broken("adventure.json", R"({"id":"x","chapters":["chapters/adv-a","chapters/adv-b"],
        "transitions":[{"from":"adv-a","exitMarker":"east","to":"adv-c","entryMarker":"west"}]})"), 1, &error)
        && error.find("isn't in the list") != std::string::npos, "A transition to a chapter outside the adventure is refused");
    WorldFixture c;
    check(!c.loadJson("chapters/adv-a", broken("adventure.json", R"({"id":"x","maxLevel":1,"chapters":["chapters/adv-a","chapters/adv-b"]})"), 1, &error)
        && error.find("outside") != std::string::npos, "Every chapter is written for a level in the adventure's range");
    WorldFixture d;
    check(!d.loadJson("chapters/adv-a", broken("chapters/adv-b/chapter.json", R"({"id":"adv-b","party":[{"name":"Cy","class":"fighter","at":[3,2]}]})"), 1, &error)
        && error.find("seats") != std::string::npos, "Every chapter seats the same party");
    WorldFixture e;
    check(!e.loadJson("chapters/adv-a", broken("adventure.json", R"({"id":"x","minLevel":4,"maxLevel":2,"chapters":["chapters/adv-a"]})"), 1, &error),
        "A level range has its lowest first");
    WorldFixture alone;
    check(alone.loadJson("chapters/adv-a", broken("adventure.json", R"({"id":"x","chapters":["chapters/adv-b"]})"), 1) && !alone.adventure(),
        "A chapter the adventure doesn't list plays on its own");
}

}

void worldTravelTests(const Check& check)
{
    travelling(check);
    checking(check);
}
