#include "WorldFixture.h"

#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;

const char* room = R"("tiles":{"floor":{"art":"stone"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
    "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":["##########","#........#","#........#","#........#","#........#","##########"]}])";

// A room with a goblin and a chest, and a camp of its own (over the shared one) with four seats.
std::map<std::string, std::string> campFiles(const std::string& roomExtra = "")
{
    return {
        {"chapters/wild/chapter.json", R"({"id":"wild","title":"The Wild",)" + roomExtra + R"(
            "party":[{"name":"Ana","class":"fighter","at":[2,2]},{"name":"Bo","class":"cleric","at":[2,3]}],
            "encounters":[{"id":"g","creatures":[{"creature":"goblin","at":[7,4]}]}],
            "containers":[{"id":"box","name":"Box","at":[3,4],"coins":50}]})"},
        {"chapters/wild/map.json", std::string("{") + room + "}"},
        {"chapters/camp/chapter.json", R"({"id":"camp","title":"The Camp",
            "party":[{"name":"A","class":"fighter","at":[2,2]},{"name":"B","class":"fighter","at":[3,2]},
                {"name":"C","class":"fighter","at":[4,2]},{"name":"D","class":"fighter","at":[5,2]}]})"},
        {"chapters/camp/map.json", std::string("{") + room + R"(,"markers":{"entry":[4,3]}})"},
    };
}

yh::Item food(int units)
{
    yh::Item item;
    item.id = "supplies";
    item.name = "Supplies";
    item.slot = "";
    item.hands = 0;
    item.supplies = 10;
    item.quantity = units;
    return item;
}

size_t restIndex(const World& world, std::string_view id)
{
    for (size_t i = 0; i < world.rules().rests.size(); i++)
        if (world.rules().rests[i].id == id)
            return i;
    return world.rules().rests.size();
}

void goingToCamp(const Check& check)
{
    WorldFixture world;
    std::string error;
    check(world.loadJson("chapters/wild", campFiles(), 7, &error) && !world.atCamp(), "A chapter loads with a camp to go to");
    world.tokens().tokens[0].position = world.grid().center({3, 3});
    world.send("loot", {{"hero", 0}, {"pile", 0}, {"all", true}});
    const yh::Cell where = world.cellOf(0);
    world.tokens().tokens[1].color = {1, 2, 3, 255};
    world.sheet(1).hp = 1;
    check(world.send("camp") && world.atCamp() && world.chapter()->id == "camp" && world.said("makes camp"), "The party makes camp between fights");
    check(world.heroCount() == 2 && world.sheet(0).name == "Ana" && world.sheet(1).hp == 1 && world.sheet(0).coins == 50
        && world.tokens().tokens[1].color.r == 1 && world.tokens().tokens[1].color.b == 3, "Camp seats as many heroes as came, as they were");
    check(world.cellOf(0) == yh::Cell{4, 3} && world.cellOf(1) != world.cellOf(0), "They arrive at the camp's entry marker");
    check(!world.send("camp"), "Camp can't be made at camp");

    // A save at camp comes back at camp, still knowing the way back.
    WorldFixture other;
    check(other.loadJson("chapters/wild", campFiles(), 7) && other.restoreState(world.stateJson(), &error) && other.atCamp()
        && other.canLeaveCamp() && other.heroCount() == 2, "A save made at camp loads at camp");

    check(world.send("leave-camp") && !world.atCamp() && world.chapter()->id == "wild" && world.cellOf(0) == where,
        "Leaving camp goes back to the same place");
    check(world.piles()[0].coins == 0 && world.creatures().size() == 3 && !world.creatures()[2].sheet.down() && world.sheet(1).hp == 1,
        "The chapter is as the party left it, and the heroes as they left camp");
    check(!world.send("leave-camp"), "Only a party at camp can leave it");

    check(other.send("leave-camp") && other.chapter()->id == "wild" && other.piles()[0].coins == 0, "The loaded save leaves camp the same way");

    WorldFixture shut;
    check(shut.loadJson("chapters/wild", campFiles(R"("camp":false,)"), 7) && !shut.send("camp") && !shut.atCamp(),
        "A chapter can forbid making camp");
}

void restingAtCamp(const Check& check)
{
    WorldFixture world;
    check(world.loadJson("chapters/wild", campFiles(), 7), "Camp test loads");
    const size_t longRest = restIndex(world, "long"), shortRest = restIndex(world, "short");
    check(longRest < world.rules().rests.size() && world.rules().rests[longRest].campOnly
        && world.rules().rests[longRest].supplyCost == 40, "The game's long rest is taken at camp and costs 40 supplies");
    world.sheet(0).hp = 3;
    world.sheet(0).inventory.push_back(food(3));
    check(!world.send("rest", {{"rest", longRest}}) && world.refusal.find("Make camp") != std::string::npos, "The long rest waits for camp");
    world.send("camp");
    check(world.suppliesHeld() == 30 && !world.send("rest", {{"rest", longRest}}) && world.refusal.find("30 of 40") != std::string::npos,
        "Without enough supplies the long rest is refused");
    world.send("stash", {{"hero", 0}, {"item", world.sheet(0).inventory.size() - 1}});
    check(world.stash().items.size() == 1 && world.stash().supplies() == 30 && world.suppliesHeld() == 30, "Supplies go in the stash");
    world.sheet(1).inventory.push_back(food(2));
    world.send("rest", {{"rest", shortRest}});
    world.send("rest", {{"rest", shortRest}});
    check(world.restsLeft(world.rules().rests[shortRest]) == 0, "Two short rests are used up");
    check(world.send("rest", {{"rest", longRest}}) && world.sheet(0).hp == world.sheet(0).maxHp(), "A long rest at camp heals fully");
    check(world.suppliesHeld() == 10 && world.stash().empty() && world.sheet(1).inventory.back().quantity == 1,
        "It uses the stash's supplies first, then what the heroes carry");
    check(world.restsLeft(world.rules().rests[shortRest]) == 2, "A long rest gives the short rests back");
}

void stashAndRevival(const Check& check)
{
    WorldFixture world;
    check(world.loadJson("chapters/wild", campFiles(), 7), "Camp test loads");
    world.sheet(0).inventory.push_back(food(1));
    const size_t carried = world.sheet(0).inventory.size();
    check(!world.send("stash", {{"hero", 0}, {"item", carried - 1}}), "The stash is only at camp");
    world.sheet(1).hp = 0;
    world.sheet(1).death.dead = true;
    world.sheet(0).coins = 0;
    world.send("camp");
    check(world.send("stash", {{"hero", 0}, {"item", carried - 1}}) && world.sheet(0).inventory.size() == carried - 1, "A hero puts an item in the stash");
    check(!world.send("unstash", {{"hero", 1}, {"item", 0}}), "The dead take nothing from the stash");
    check(world.send("unstash", {{"hero", 0}, {"item", 0}}) && world.stash().empty() && world.sheet(0).inventory.size() == carried,
        "A hero takes it back out");

    const int price = world.rules().revivePrice;
    check(price > 0, "The game sells revival at camp");
    check(!world.send("revive", {{"hero", 0}, {"target", 1}}) && world.refusal.find("needs") != std::string::npos, "Revival needs the price");
    world.sheet(0).coins = price + 5;
    check(!world.send("revive", {{"hero", 0}, {"target", 0}}), "Only the dead are revived");
    check(world.send("revive", {{"hero", 0}, {"target", 1}}) && !world.sheet(1).death.dead && world.sheet(1).hp > 0
        && world.sheet(0).coins == 5, "Paying the price brings a dead hero back");

    WorldFixture away;
    away.loadJson("chapters/wild", campFiles(), 7);
    away.sheet(1).hp = 0;
    away.sheet(1).death.dead = true;
    away.sheet(0).coins = price;
    check(!away.send("revive", {{"hero", 0}, {"target", 1}}), "Revival is only at camp");
    away.send("camp");
    away.sheet(0).inventory.push_back(food(4));
    away.send("rest", {{"rest", restIndex(away, "long")}});
    check(away.sheet(1).death.dead, "A long rest doesn't bring back the dead");

    // The stash comes along to the next save.
    away.send("stash", {{"hero", 0}, {"item", 0}});
    const std::string saved = away.stateJson();
    WorldFixture later;
    check(later.loadJson("chapters/wild", campFiles(), 7) && later.restoreState(saved) && later.stash().items.size() == 1,
        "The stash is saved");
}

}

void worldCampTests(const Check& check)
{
    goingToCamp(check);
    restingAtCamp(check);
    stashAndRevival(check);
}
