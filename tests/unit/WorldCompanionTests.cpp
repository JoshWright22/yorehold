#include "WorldFixture.h"

#include <algorithm>
#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;

// A wall down the middle keeps the goblin out of sight until a test starts the fight.
const char* room = R"("tiles":{"floor":{"art":"stone"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
    "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":["############","#......#...#","#......#...#","#......#...#","#......#...#","############"]}])";

// Choices, in order: be kind, ask them along, send them away, nudge tam, goodbye.
enum Choice : size_t { Kind, Join, Part, Nudge, Bye };

const char* talk = R"({"id":"talk","start":"hello","nodes":[{"id":"hello","speaker":"Them","text":"Hello.","choices":[
    {"id":"kind","text":"Kind words.","do":["approve +10"],"next":"hello"},
    {"id":"join","text":"Come along.","do":["recruit"],"next":""},
    {"id":"part","text":"Wait here.","do":["dismiss"],"next":""},
    {"id":"nudge","text":"Tam would like this.","do":["approve tam 3"],"next":"hello"},
    {"id":"bye","text":"Bye.","next":""}]}]})";

// Four heroes and three people who could join, in an adventure of two rooms. The yorehold ruleset
// (the default) allows two companions and a party of six.
std::map<std::string, std::string> companionFiles()
{
    const std::string party = R"("party":[{"name":"Ana","class":"fighter","at":[1,1]},{"name":"Bo","class":"cleric","at":[1,2]},
        {"name":"Cy","class":"rogue","at":[1,3]},{"name":"Di","class":"wizard","at":[1,4]}])";
    return {
        {"adventure.json", R"({"id":"pair","title":"Pair","chapters":["chapters/yard","chapters/far"],
            "transitions":[{"from":"yard","exitMarker":"east","to":"far","entryMarker":"west"},
                {"from":"far","exitMarker":"west","to":"yard","entryMarker":"back"}]})"},
        {"chapters/yard/chapter.json", R"({"id":"yard","title":"The Yard",)" + party + R"(,
            "encounters":[{"id":"g","creatures":[{"creature":"goblin","at":[10,2]}]}],
            "npcs":[{"id":"tam","name":"Tam","at":[4,1],"dialogue":"talk.json","companion":{"joinAt":10,"leaveAt":-20,"flags":{"burned":-40}}},
                {"id":"bel","name":"Bel","at":[4,2],"dialogue":"talk.json","companion":{}},
                {"id":"cob","name":"Cob","at":[4,3],"dialogue":"talk.json","companion":{"approval":5}},
                {"id":"dee","name":"Dee","at":[4,4],"dialogue":"talk.json"}]})"},
        {"chapters/yard/talk.json", talk},
        {"chapters/yard/map.json", std::string("{") + room + R"(,"markers":{"east":[6,4],"back":[5,1]}})"},
        {"chapters/far/chapter.json", R"({"id":"far","title":"Far Away",)" + party + "}"},
        {"chapters/far/map.json", std::string("{") + room + R"(,"markers":{"west":[2,2]}})"},
    };
}

void say(WorldFixture& world, size_t who, size_t choice)
{
    world.send("talk", {{"creature", who}});
    world.send("reply", {{"choice", choice}, {"hero", 0}});
    if (world.talk())
        world.send("leave");
}

void put(WorldFixture& world, size_t creature, yh::Cell cell)
{
    world.tokens().tokens[creature].position = world.grid().center(cell);
    world.tokens().tokens[creature].path.clear();
}

bool inFight(const WorldFixture& world, size_t creature)
{
    const auto& order = world.encounter()->order();
    return std::any_of(order.begin(), order.end(), [&](const yh::Combatant& c) { return c.character == &world.creatures()[creature].sheet && c.team == 0; });
}

void joining(const Check& check)
{
    WorldFixture world;
    std::string error;
    check(world.loadJson("chapters/yard", companionFiles(), 3, &error), "The companion yard loads");
    if (!world.chapter()) { std::fprintf(stderr, "%s\n", error.c_str()); return; }
    const size_t tam = world.companionToken("tam").value_or(0), bel = world.companionToken("bel").value_or(0);
    const size_t cob = world.companionToken("cob").value_or(0);
    check(tam >= world.heroCount() && !world.companion(tam) && world.companions().approval("tam") == 0 && world.companions().approval("cob") == 5
        && !world.companionToken("dee"), "NPCs with a companion entry are met with their starting approval; others aren't companions");
    check(world.rules().companions.limit == 2 && world.rules().companions.partyLimit == 6, "The party cap is ruleset data");

    say(world, tam, Join);
    check(!world.companion(tam) && world.said("isn't ready"), "Below their joinAt they won't come");
    say(world, tam, Kind);
    check(world.companions().approval("tam") == 10 && world.said("Tam approves (+10)"), "Approval moves with dialogue choices");
    say(world, tam, Join);
    check(world.companion(tam) && world.creatures()[tam].team == 0 && world.tokens().follows(tam) && world.said("Tam joins the party"),
        "Asked again they join, on the party's side and walking with it");
    say(world, bel, Join);
    say(world, cob, Join);
    check(world.companion(bel) && !world.companion(cob) && world.said("The party is full") && world.partyMemberCount() == 6,
        "Four heroes and two companions fill the party");
    say(world, world.npcToken(3), Join);
    check(world.said("Dee won't join"), "Someone without a companion entry never joins");

    say(world, bel, Part);
    check(!world.companion(bel) && world.creatures()[bel].team == 2 && !world.tokens().follows(bel) && world.said("Bel leaves the party"),
        "A companion can be sent away and stays as an NPC");
    say(world, cob, Join);
    check(world.companion(cob), "That makes room for another");
    say(world, cob, Nudge);
    check(world.companions().approval("tam") == 13, "One conversation can change what another companion thinks");

    nlohmann::json at = nlohmann::json::array();
    for (const yh::Token& token : world.tokens().tokens)
        at.push_back({token.position.x, token.position.y});
    check(world.send("fight", {{"group", 0}, {"at", at}}) && inFight(world, tam) && inFight(world, cob) && !inFight(world, bel),
        "Companions fight on the party's side");
}

void approvalFromFlags(const Check& check)
{
    WorldFixture world;
    check(world.loadJson("chapters/yard", companionFiles(), 3), "The yard loads again");
    if (!world.chapter()) return;
    const size_t tam = *world.companionToken("tam");
    say(world, tam, Kind);
    say(world, tam, Join);
    world.setFlags({"burned"});
    world.step(0.05); // the log catches up
    check(!world.companion(tam) && world.companions().approval("tam") == -30 && world.said("Tam disapproves (-40)") && world.said("Tam leaves the party"),
        "A story flag changes approval, and falling to leaveAt makes them leave");
    world.setFlags({"burned", "other"});
    check(world.companions().approval("tam") == -30, "A flag counts once");
}

void savingAndTravelling(const Check& check)
{
    WorldFixture world;
    check(world.loadJson("chapters/yard", companionFiles(), 5), "The yard loads for travel");
    if (!world.chapter()) return;
    const size_t tam = *world.companionToken("tam");
    say(world, tam, Kind);
    say(world, tam, Join);
    world.sheet(tam).hp -= 1;
    const int hp = world.sheet(tam).hp;

    WorldFixture loaded;
    check(loaded.loadJson("chapters/yard", companionFiles(), 5) && loaded.restoreState(world.stateJson()) && loaded.companion(tam)
        && loaded.tokens().follows(tam) && loaded.companions().approval("tam") == 10, "A save keeps who joined and what they think");

    put(world, 0, {6, 4});
    world.step(0.1);
    check(world.chapter()->id == "far", "The party travels on");
    const std::optional<size_t> along = world.companionToken("tam");
    check(along && world.companion(*along) && world.sheet(*along).hp == hp && world.creatures().size() == world.heroCount() + 1
        && world.walkable(world.cellOf(*along)), "Companions come along to the next chapter as they were");
    check(along && world.talkable(*along), "and can still be talked to there");

    const std::string far = world.stateJson();
    WorldFixture again;
    check(again.loadJson("chapters/yard", companionFiles(), 5) && again.restoreState(far) && again.chapter()->id == "far"
        && again.companionToken("tam") && again.companion(*again.companionToken("tam")) && again.sheet(*again.companionToken("tam")).hp == hp,
        "A save in another chapter brings the companion back");

    check(world.send("camp") && world.atCamp(), "Make camp with a companion");
    check(world.companionToken("tam") && world.companion(*world.companionToken("tam")), "Companions come to camp");
    world.sheet(*world.companionToken("tam")).hp = hp - 1;
    check(world.send("leave-camp") && world.chapter()->id == "far" && world.companionToken("tam")
        && world.sheet(*world.companionToken("tam")).hp == hp - 1 && world.creatures().size() == world.heroCount() + 1,
        "and back again, without a second copy");

    put(world, 0, {3, 3});
    world.step(0.1);
    put(world, 0, {2, 2});
    world.step(0.1);
    check(world.chapter()->id == "yard" && world.companionToken("tam") == tam && world.companion(tam)
        && world.creatures().size() == world.heroCount() + 1 + 4, "Back in their own chapter they are the NPC there, not a copy");
}

}

void worldCompanionTests(const Check& check)
{
    joining(check);
    approvalFromFlags(check);
    savingAndTravelling(check);
}
