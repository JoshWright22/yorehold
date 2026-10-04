#include "WorldFixture.h"

#include <functional>

namespace
{

bool yard(WorldFixture& world)
{
    if (!world.loadJson("chapters/turn-yard", {
        {"chapters/turn-yard/chapter.json", R"({"id":"turn-yard","title":"Yard","map":"map.json",
            "party":[{"name":"Ana","class":"fighter","at":[2,2]},{"name":"Bo","class":"cleric","at":[3,2]}],
            "encounters":[{"id":"yard","creatures":[{"creature":"goblin","name":"Gik","at":[5,5]},
                {"creature":"goblin","name":"Gok","at":[6,5]}]}]})"},
        {"chapters/turn-yard/map.json", R"({"name":"Yard","tiles":{"floor":{"art":"grass"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
            "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":[
                "########","#......#","#......#","#......#","#......#","#......#","#......#","########"]}]})"}
    }, 5)) return false;
    nlohmann::json positions = nlohmann::json::array();
    for (size_t i = 0; i < world.creatures().size(); i++)
    {
        world.sheet(i).stats.setBase("dex", 2000.0f - static_cast<float>(i) * 200.0f);
        world.sheet(i).stats.setBase("maxHp", 1000);
        world.sheet(i).hp = 1000;
        const yh::Vec2 at = world.tokens().tokens[i].position;
        positions.push_back({at.x, at.y});
    }
    return world.send("fight", {{"group", 0}, {"at", positions}});
}

}

void worldTurnTests(const std::function<void(bool, const char*)>& check)
{
    WorldFixture world, peer;
    check(yard(world) && yard(peer), "Both shared-turn yards load");
    if (!world.fighting() || !peer.fighting()) return;
    check(world.rules().sharedTurns && world.currentCreature() == 0 && world.canChooseTurn(1)
        && !world.canChooseTurn(2) && !world.canChooseTurn(999), "Only the active contiguous allies can be selected");
    auto command = [&](yh::PlayerId player, const char* type, const nlohmann::json& data) {
        std::string reason;
        const auto accepted = world.validate(player, type, data.dump(), reason);
        if (!accepted) return false;
        const yh::NetCommand cmd{0, player, type, *accepted};
        world.apply(cmd); peer.apply(cmd);
        return world.checksum() == peer.checksum();
    };
    check(command(0, "use", {{"action", "defend"}}) && command(0, "step", {{"at", {2, 3}}}),
        "Actions and movement agree on both copies");
    check(!world.canChooseTurn(1) && !world.send("turn", {{"creature", 1}}), "An animated move completes before switching members");
    for (WorldFixture* copy : {&world, &peer})
    {
        copy->tokens().tokens[0].position = copy->grid().center({2, 3});
        copy->tokens().tokens[0].path.clear();
    }
    const yh::TurnBudget ana = world.encounter()->order()[0].budget;
    check(command(0, "turn", {{"creature", 1}}) && command(0, "use", {{"action", "defend"}})
        && command(0, "turn", {{"creature", 0}}), "Allies can interleave their actions through replicated commands");
    check(world.encounter()->order()[0].budget.actions == ana.actions
        && world.encounter()->order()[0].budget.movementLeft == ana.movementLeft && world.sheet(0).hasCondition("shielded")
        && world.encounter()->order()[1].budget.actions == 1, "Switching preserves both budgets and turn-start conditions");
    check(command(0, "seats", {{"owners", {0, 1}}, {"names", {{"0", "Host"}, {"1", "Guest"}}}}), "The allies may have separate owners");
    check(!world.send("turn", {{"creature", 1}}) && command(1, "turn", {{"creature", 1}}),
        "Only a member's owner can select it, even during another owner's partial turn");
    check(!world.send("use", {{"action", "defend"}}) && command(0, "turn", {{"creature", 0}}),
        "Selecting an ally does not grant its actions to another player");
    check(command(0, "use", {{"action", "end-turn"}}) && world.currentCreature() == 1 && !world.canChooseTurn(0),
        "Ending one member leaves the other available and cannot be undone by switching");
    check(command(1, "use", {{"action", "end-turn"}}) && world.currentCreature() == 2 && !world.canChooseTurn(1),
        "Finishing every ally advances to the next side's block");
    check(command(0, "turn", {{"creature", 3}}) && command(0, "use", {{"action", "end-turn"}})
        && world.currentCreature() == 2, "The host can choose a different enemy in a shared block");
    check(command(0, "use", {{"action", "end-turn"}}) && world.encounter()->round() == 2
        && world.canChooseTurn(1) && world.encounter()->order()[0].budget.actions == 2 && !world.sheet(0).hasCondition("shielded"),
        "The next round refreshes the entire allied block exactly once");
    check(command(0, "use", {{"action", "ready"}}) && world.creatures()[0].readiedAction == "strike"
        && world.currentCreature() == 1 && command(1, "turn", {{"creature", 1}}) && world.creatures()[0].readiedAction == "strike",
        "Ready survives another ally's selection in the same block");
}
