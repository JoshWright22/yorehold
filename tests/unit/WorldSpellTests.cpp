#include "WorldFixture.h"

#include <algorithm>
#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;

// The fixture with the world's casting internals in reach, to set situations up directly.
struct SpellWorld : WorldFixture
{
    using World::creatures_;
    using World::castSpell;
    using World::endConcentration;
    // Runs an effect directly, then takes what the world said into the log (an intent nobody
    // knows is refused, and only reads the events).
    void hit(size_t from, const yh::ActionDefinition& action, size_t target)
    {
        runActionEffect(from, action, target);
        send("read-log");
    }
};

// A wizard, a cleric and a fighter in a long hall with three goblins: two in a row to the east,
// one off to the south.
std::map<std::string, std::string> hallFiles()
{
    return {{"chapters/spell-hall/chapter.json", R"({"id":"spell-hall","title":"Hall","map":"map.json",
        "party":[{"name":"Wiz","class":"wizard","at":[3,3]},{"name":"Bo","class":"cleric","at":[3,4]},{"name":"Ana","class":"fighter","at":[2,3]}],
        "encounters":[{"id":"hall","creatures":[{"creature":"goblin","name":"Gik","at":[5,3]},
        {"creature":"goblin","name":"Nok","at":[6,3]},{"creature":"goblin","name":"Zed","at":[3,6]}]}]})"},
        {"chapters/spell-hall/map.json", R"({"name":"Hall","tiles":{"floor":{"art":"grass"},
        "wall":{"art":"wall","walkable":false,"blocksSight":true}},"legend":{".":"floor","#":"wall"},
        "layers":[{"name":"ground","rows":["##############","#............#","#............#","#............#",
        "#............#","#............#","#............#","##############"]}]})"}};
}

void emptyHands(yh::Character& sheet)
{
    for (size_t i = 0; i < sheet.inventory.size(); i++)
        if (sheet.inventory[i].equipped && yh::Character::held(sheet.inventory[i]))
            sheet.unequip(i);
}

// Tough goblins that always fail the wizard's saves, with the wizard first in the order.
void prepare(SpellWorld& world)
{
    world.sheet(0).stats.setBase("dex", 1000);
    world.sheet(0).stats.setBase("int", 1000);
    world.sheet(0).stats.setBase("maxHp", 500);
    world.sheet(0).hp = 500;
    for (size_t goblin = 3; goblin < 6; goblin++)
    {
        world.sheet(goblin).stats.setBase("maxHp", 100);
        world.sheet(goblin).hp = 100;
    }
}

bool startFight(SpellWorld& world)
{
    nlohmann::json positions = nlohmann::json::array();
    for (const auto& token : world.tokens().tokens)
        positions.push_back({token.position.x, token.position.y});
    return world.send("fight", {{"group", 0}, {"at", positions}}) && world.currentCreature() == 0;
}

int actionsLeft(const SpellWorld& world)
{
    return world.encounter()->order()[world.encounter()->currentIndex()].budget.actions;
}

void betweenFights(const Check& check)
{
    SpellWorld world, peer;
    check(world.loadJson("chapters/spell-hall", hallFiles(), 5) && peer.loadJson("chapters/spell-hall", hallFiles(), 5), "The spell hall loads");
    if (!world.chapter() || !peer.chapter()) return;
    check(world.sheet(0).spells == std::vector<std::string>{"spark", "chill-bite", "arcane-dart", "flame-fan", "mire"}
        && world.sheet(1).spells == std::vector<std::string>{"rebuke", "kind-word", "shield-of-faith", "mend", "brand-of-light"}
        && world.sheet(2).spells.empty() && world.sheet(0).resources.at("slots-1").max == 2, "Casters start with their class's spells and slots");
    const auto has = world.actionsOf(0);
    check(world.findSpell("mire") && world.findAction("mire") == &world.findSpell("mire")->action
        && std::find(has.begin(), has.end(), world.findAction("spark")) != has.end()
        && has.back()->id == World::endTurnAction && world.actionsOf(2).size() + 5 == has.size(), "A caster's spells are listed with its actions");

    world.sheet(2).hp = 1; peer.sheet(2).hp = 1;
    const nlohmann::json mend{{"hero", 1}, {"spell", "mend"}, {"target", 2}, {"healing", 1000}, {"slot", 0}};
    const auto untouched = world.checksum();
    check(!world.send("cast", mend) && world.refusal.find("free hand") != std::string::npos && world.checksum() == untouched,
        "A mace and a shield leave no hand to cast with");
    emptyHands(world.sheet(1)); emptyHands(peer.sheet(1));
    std::string reason;
    const auto before = world.checksum();
    check(!world.validate(1, "cast", mend.dump(), reason) && world.checksum() == before, "A peer cannot cast with another player's hero");
    check(!world.send("cast", {{"hero", 0}, {"spell", "spark"}, {"target", 2}}) && !world.send("cast", {{"hero", 0}, {"spell", "spark"}, {"target", 3}})
        && !world.send("cast", {{"hero", 0}, {"spell", "flame-fan"}, {"target", 0}}) && !world.send("cast", {{"hero", 0}, {"spell", "mend"}, {"target", 2}})
        && world.checksum() == before, "Harmful spells wait for a fight and nobody casts a spell they don't know");
    world.tokens().tokens[2].position = world.grid().center({9, 5});
    check(!world.send("cast", mend) && world.sheet(1).resources.at("slots-1").current == 2, "A touch spell out of reach spends no slot");
    world.tokens().tokens[2].position = world.grid().center({2, 3});
    const auto command = world.validate(0, "cast", mend.dump(), reason);
    check(command && !nlohmann::json::parse(*command).contains("healing") && nlohmann::json::parse(*command).at("slot") == 1,
        "The host names the slot and ignores player-supplied outcomes");
    if (!command) return;
    world.apply({0, 0, "cast", *command}); peer.apply({0, 0, "cast", *command});
    check(world.sheet(2).hp >= 4 && world.sheet(2).hp <= 11 && world.sheet(1).resources.at("slots-1").current == 1
        && world.checksum() == peer.checksum(), "Mend heals, spends one slot and rolls the same on both peers");

    // With no slot of its own level left, the next one up is spent and the spell grows with it.
    world.sheet(1).resources["slots-1"].current = 0;
    world.sheet(1).resources["slots-2"] = {1, 1};
    world.sheet(2).hp = 1;
    check(world.send("cast", mend) && world.sheet(1).resources.at("slots-2").current == 0 && world.sheet(2).hp >= 5
        && world.said("from a level 2 slot"), "A spell can be cast from a higher slot");
    check(!world.send("cast", mend) && world.refusal.find("no spell slot left") != std::string::npos, "Without slots a levelled spell is refused");

    // Spent slots and a held spell are in the save; a long rest brings the slots back.
    world.creatures_[1].concentration.spell = "mire";
    world.creatures_[1].concentration.holds = {{2, "slowed"}};
    world.sheet(2).addCondition(world.rules(), "slowed", 10);
    check(peer.restoreState(world.snapshot()) && peer.sheet(1).resources.at("slots-1").current == 0
        && peer.creatures()[1].concentration.spell == "mire" && peer.creatures()[1].concentration.holds.size() == 1
        && peer.checksum() == world.checksum(), "Slots and concentration survive a save and a joining snapshot");
    auto saved = nlohmann::json::parse(world.stateJson());
    saved["creatures"][1]["concentration"]["holds"][0]["who"] = 99;
    check(!peer.restoreState(saved.dump()), "A save holding a spell on a creature that isn't there is refused");
    check(world.send("rest", {{"rest", 1}}) && world.sheet(1).resources.at("slots-1").current == 2
        && !world.creatures()[1].concentration.active() && !world.sheet(2).hasCondition("slowed"),
        "A long rest restores spell slots and ends concentration");
}

void aiming(const Check& check)
{
    SpellWorld world, peer;
    check(world.loadJson("chapters/spell-hall", hallFiles(), 9) && peer.loadJson("chapters/spell-hall", hallFiles(), 9), "Two copies load the hall");
    if (!world.chapter() || !peer.chapter()) return;
    for (SpellWorld* copy : {&world, &peer})
    {
        prepare(*copy);
        copy->sheet(4).stats.setBase("dex", 1000); // Nok always makes the save
    }
    check(startFight(world) && startFight(peer), "The wizard acts first");
    if (world.currentCreature() != 0) return;

    // A cantrip: one hand, one action, no slot.
    check(world.send("use", {{"action", "spark"}, {"target", 3}}) && peer.send("use", {{"action", "spark"}, {"target", 3}})
        && world.sheet(3).hp <= 99 && world.sheet(3).hp >= 94 && actionsLeft(world) == 1
        && world.sheet(0).resources.at("slots-1").current == 2, "Spark costs an action and no slot, and a failed save takes the damage");
    const int nok = world.sheet(4).hp;
    check(world.send("use", {{"action", "spark"}, {"target", 4}}) && peer.send("use", {{"action", "spark"}, {"target", 4}})
        && world.sheet(4).hp == nok && world.checksum() == peer.checksum(), "A successful save stops Spark");
    check(!world.send("use", {{"action", "spark"}, {"target", 5}}), "No actions left, no more spells");

    // Two hands: the staff has to be put away, and the cost is two actions.
    SpellWorld second, other;
    if (!second.loadJson("chapters/spell-hall", hallFiles(), 9) || !other.loadJson("chapters/spell-hall", hallFiles(), 9)) return;
    for (SpellWorld* copy : {&second, &other})
    {
        prepare(*copy);
        copy->sheet(4).stats.setBase("dex", 1000);
    }
    check(startFight(second) && startFight(other), "The wizard acts first again");
    if (second.currentCreature() != 0) return;
    const nlohmann::json fan{{"action", "flame-fan"}, {"at", {5, 3}}};
    check(!second.send("use", fan) && second.refusal.find("2 free hands") != std::string::npos && actionsLeft(second) == 2,
        "A two-handed spell is refused while a hand holds the staff");
    emptyHands(second.sheet(0)); emptyHands(other.sheet(0));
    check(!second.send("use", {{"action", "flame-fan"}, {"at", {3, 3}}}) && !second.send("use", {{"action", "flame-fan"}, {"at", {40, 3}}})
        && !second.send("use", {{"action", "flame-fan"}}) && actionsLeft(second) == 2, "A cone must be aimed away from the caster, on the map");
    const auto cone = second.creaturesIn(0, *second.findAction("flame-fan"), {5, 3});
    check(cone == std::vector<size_t>{3, 4}, "The cone covers the two goblins in a row and nobody behind or beside it");
    check(second.send("use", fan) && other.send("use", fan) && actionsLeft(second) == 0 && second.sheet(0).resources.at("slots-1").current == 1,
        "Flame fan costs two actions and a first-level slot");
    const int burned = 100 - second.sheet(3).hp;
    check(burned >= 2 && burned <= 12 && 100 - second.sheet(4).hp == burned / 2 && second.sheet(5).hp == 100 && second.sheet(2).hp == second.sheet(2).maxHp()
        && second.checksum() == other.checksum(), "One roll for the area: full on a failed save, half on a success, the same on both peers");

    // Walls stop an area, and so does range.
    const yh::ActionDefinition& mire = *second.findAction("mire");
    std::string why;
    check(second.validAim(0, mire, {8, 3}) && !second.validAim(0, mire, {11, 3}, &why) && why.find("out of range") != std::string::npos
        && !second.validAim(0, mire, {3, -1}), "A burst is aimed at a square in range");
    check(second.creaturesIn(0, mire, {5, 3}) == std::vector<size_t>{3, 4} && second.creaturesIn(0, mire, {3, 5}) == std::vector<size_t>{5}
        && second.creaturesIn(0, mire, {3, 3}).empty(), "A burst lands on the enemies inside it and spares the party");
}

void concentration(const Check& check)
{
    SpellWorld world;
    check(world.loadJson("chapters/spell-hall", hallFiles(), 11), "The hall loads for concentration");
    if (!world.chapter()) return;
    prepare(world);
    check(startFight(world), "The wizard acts first to concentrate");
    if (world.currentCreature() != 0) return;
    const auto slowed = [&](size_t who) { return world.sheet(who).hasCondition("slowed"); };

    check(world.send("use", {{"action", "mire"}, {"at", {5, 3}}}) && slowed(3) && slowed(4) && !slowed(5) && !slowed(1)
        && world.creatures()[0].concentration.spell == "mire" && world.creatures()[0].concentration.holds.size() == 2
        && world.said("concentrates on Mire"), "Mire slows the goblins in it and the wizard concentrates");
    // One at a time: casting it again lets the first go.
    check(world.send("use", {{"action", "mire"}, {"at", {3, 6}}}) && !slowed(3) && !slowed(4) && slowed(5)
        && world.creatures()[0].concentration.holds.size() == 1 && world.said("stops concentrating on Mire")
        && world.sheet(0).resources.at("slots-1").current == 0, "A second concentration spell ends the first");

    // Damage: a Constitution save against 10 or half the damage.
    const auto jab = yh::ActionDefinition::fromJson(R"({"id":"jab","target":{"kind":"creature","side":"any","range":20},
        "effects":[{"do":"damage","dice":40}]})");
    if (!jab) return;
    world.sheet(0).stats.setBase("con", 1000);
    world.hit(3, *jab, 0);
    check(world.creatures()[0].concentration.active() && slowed(5) && world.said("DC 20") && world.said("- held"),
        "A made save keeps the spell through damage");
    world.sheet(0).stats.setBase("con", 1);
    world.hit(3, *jab, 0);
    check(!world.creatures()[0].concentration.active() && !slowed(5) && world.said("- lost") && world.said("(hurt)"),
        "A failed save on damage breaks concentration and what it held");

    // Going down ends it without a roll; a spell that runs out ends it quietly.
    world.creatures_[0].concentration.spell = "mire";
    world.creatures_[0].concentration.holds = {{5, "slowed"}};
    world.sheet(5).addCondition(world.rules(), "slowed", 10);
    world.sheet(5).removeCondition("slowed");
    world.hit(3, *jab, 5);
    check(!world.creatures()[0].concentration.active() && world.said("run its course"), "Concentration with nothing left to hold is over");
    world.creatures_[0].concentration.spell = "mire";
    world.creatures_[0].concentration.holds = {{5, "slowed"}};
    world.sheet(5).addCondition(world.rules(), "slowed", 10);
    world.sheet(0).stats.setBase("con", 1000);
    world.sheet(0).hp = 5;
    world.hit(3, *jab, 0);
    check(world.sheet(0).down() && !world.creatures()[0].concentration.active() && !slowed(5) && world.said("(down)"),
        "A caster who drops lets the spell go");
}

void preparing(const Check& check)
{
    SpellWorld world, peer;
    check(world.loadJson("chapters/spell-hall", hallFiles(), 13) && peer.loadJson("chapters/spell-hall", hallFiles(), 13), "The hall loads for preparing");
    if (!world.chapter() || !peer.chapter()) return;
    const yh::Character& wizard = world.sheet(0);
    check(wizard.preparable == std::vector<std::string>{"flame-fan", "mire", "glass-skin"} && wizard.prepareLimit == 2
        && wizard.prepared == std::vector<std::string>{"flame-fan", "mire"}
        && world.creatures()[0].mayPrepare, "A new wizard has its first spells prepared and may choose again before the first fight");

    // Choosing: from its own list, within its limit, by its own player.
    const nlohmann::json justMire{{"hero", 0}, {"spells", {"mire"}}};
    std::string reason;
    check(!world.validate(1, "prepare", justMire.dump(), reason), "A peer cannot prepare another player's spells");
    check(!world.send("prepare", {{"hero", 0}, {"spells", {"mire", "mend"}}}) && !world.send("prepare", {{"hero", 0}, {"spells", nlohmann::json::array()}})
        && !world.send("prepare", {{"hero", 2}, {"spells", {"mire"}}}) && wizard.prepared.size() == 2, "Only spells from its list, and at least one");
    check(world.send("prepare", justMire) && peer.send("prepare", justMire) && wizard.prepared == std::vector<std::string>{"mire"}
        && std::find(wizard.spells.begin(), wizard.spells.end(), "flame-fan") == wizard.spells.end() && world.said("prepares Mire")
        && world.checksum() == peer.checksum(), "Preparing swaps what the wizard can cast");
    check(peer.restoreState(world.snapshot()) && peer.sheet(0).prepared == std::vector<std::string>{"mire"} && peer.creatures()[0].mayPrepare,
        "Prepared spells and the chance to change them are in the save");

    // Once a fight has started, only the long rest opens it again.
    world.creatures_[0].mayPrepare = false;
    check(!world.send("prepare", {{"hero", 0}, {"spells", {"flame-fan", "mire"}}}) && world.refusal.find("long rest") != std::string::npos,
        "The choice waits for a long rest");
    check(world.send("rest", {{"rest", 0}}) && !world.creatures()[0].mayPrepare, "A short rest does not open it");
    check(world.send("rest", {{"rest", 1}}) && world.creatures()[0].mayPrepare && world.said("may prepare spells again")
        && world.send("prepare", {{"hero", 0}, {"spells", {"flame-fan", "mire"}}}) && wizard.prepared.size() == 2, "After a long rest the wizard chooses again");
    SpellWorld fight;
    if (!fight.loadJson("chapters/spell-hall", hallFiles(), 9)) return;
    prepare(fight);
    check(startFight(fight) && !fight.creatures()[0].mayPrepare && !fight.send("prepare", justMire), "A fight closes it");
}

void focusSpells(const Check& check)
{
    SpellWorld world;
    check(world.loadJson("chapters/spell-hall", hallFiles(), 15), "The hall loads for focus spells");
    if (!world.chapter()) return;
    emptyHands(world.sheet(1));
    const nlohmann::json ward{{"hero", 1}, {"spell", "shield-of-faith"}, {"target", 2}};
    check(world.sheet(1).resources.at("focus").max == 1 && world.send("cast", ward) && world.sheet(2).tempHp >= 3
        && world.sheet(1).resources.at("focus").current == 0 && world.sheet(1).resources.at("slots-1").current == 2,
        "A focus spell spends a focus point and no slot");
    check(!world.send("cast", ward) && world.refusal.find("needs 1 focus") != std::string::npos, "An empty pool refuses it");
    check(world.send("rest", {{"rest", 0}}) && world.sheet(1).resources.at("focus").current == 1 && world.send("cast", ward),
        "A short rest refills the pool");
}

// The starter lists: each spell cast once through the real casting path, from the slot of its own
// level, against goblins that always fail the save.
void starterLists(const Check& check)
{
    SpellWorld world;
    check(world.loadJson("chapters/spell-hall", hallFiles(), 17), "The hall loads for the starter lists");
    if (!world.chapter()) return;
    prepare(world);
    world.sheet(1).stats.setBase("wis", 1000);
    for (size_t caster : {size_t{0}, size_t{1}})
        for (int level = 1; level <= 3; level++)
            world.sheet(caster).resources["slots-" + std::to_string(level)] = {4, 4};
    const auto cell = [&](size_t who) { return world.grid().cellAt(world.tokens().tokens[who].position); };
    const auto lost = [&](size_t who) { return world.sheet(who).maxHp() - world.sheet(who).hp; };
    const auto heal = [&] {
        for (size_t i = 0; i < world.creatures().size(); i++)
        {
            world.sheet(i).hp = world.sheet(i).maxHp();
            world.sheet(i).tempHp = 0;
            for (const char* id : {"slowed", "frightened", "prone", "off-guard", "aided"})
                world.sheet(i).removeCondition(id);
        }
    };
    const auto cast = [&](size_t caster, const char* id, std::optional<size_t> target, std::optional<yh::Cell> at = std::nullopt) {
        const yh::SpellDefinition* spell = world.findSpell(id);
        if (!spell) return false;
        world.castSpell(caster, *spell, target, at, spell->level);
        world.send("read-log");
        return true;
    };

    // Between fights: helping spells go through the cast intent like any other.
    world.sheet(2).hp = 0;
    world.sheet(0).hp = 1;
    check(!world.send("cast", {{"hero", 1}, {"spell", "kind-word"}, {"target", 2}}) && world.sheet(2).hp == 0
        && !world.sheet(2).hasCondition("aided"), "Kind word cannot help someone downed");
    check(world.send("cast", {{"hero", 1}, {"spell", "kind-word"}, {"target", 0}}) && world.sheet(0).hasCondition("aided")
        && world.sheet(1).resources.at("slots-1").current == 4, "Kind word aids an ally, even with a mace and shield in hand");
    world.sheet(1).spells.push_back("gathered-mending");
    emptyHands(world.sheet(1));
    check(world.send("cast", {{"hero", 1}, {"spell", "gathered-mending"}, {"target", 1}}) && world.sheet(2).hp >= 4 && world.sheet(0).hp >= 5
        && world.sheet(1).resources.at("slots-2").current == 3, "Gathered mending heals everyone near the cleric and gets the downed up");
    heal();
    check(startFight(world), "The fight for the starter lists starts");
    if (world.currentCreature() != 0) return;

    // Wizard.
    check(world.actionCost(1, *world.findAction("kind-word")) == 1 && world.actionCost(1, *world.findAction("steadfast-chorus")) == 2,
        "A spell cast with no hands still costs its actions");
    check(cast(0, "chill-bite", 3) && lost(3) >= 1 && lost(3) <= 4 && world.sheet(3).hasCondition("slowed"),
        "Chill bite does 1d4 cold and slows");
    heal();
    const int ac = world.sheet(0).armorClass(world.rules());
    check(cast(0, "glass-skin", std::nullopt) && world.sheet(0).armorClass(world.rules()) == ac + 2
        && world.sheet(0).resources.at("slots-1").current == 3, "Glass skin gives the wizard +2 AC for a first-level slot");
    check(cast(0, "arcane-dart", 3) && lost(3) >= 3 && lost(3) <= 9 && world.sheet(0).resources.at("focus").current == 0,
        "Arcane dart does 2d4+1 force for a focus point");
    heal();
    const yh::Cell nok = cell(4);
    check(cast(0, "rams-breath", std::nullopt, yh::Cell{5, 3}) && lost(3) >= 3 && lost(3) <= 18 && lost(4) == lost(3)
        && cell(4).x == nok.x + 2 && world.sheet(0).resources.at("slots-2").current == 3, "Ram's breath hits the cone and shoves the goblins back");
    world.tokens().tokens[4].position = world.grid().center(nok);
    heal();
    const int attack = world.sheet(5).attackModifier(world.rules());
    check(cast(0, "lead-limbs", 5) && world.sheet(5).hasCondition("slowed") && world.sheet(5).attackModifier(world.rules()) == attack - 2
        && world.creatures()[0].concentration.spell == "lead-limbs" && world.creatures()[0].concentration.holds.size() == 2,
        "Lead limbs slows and weakens one goblin while the wizard concentrates");
    world.endConcentration(0, "");
    check(!world.sheet(5).hasCondition("slowed") && world.sheet(5).attackModifier(world.rules()) == attack, "Letting go of Lead limbs undoes both");
    heal();
    check(cast(0, "cinder-burst", std::nullopt, cell(4)) && lost(3) >= 5 && lost(3) <= 30 && lost(4) == lost(3) && lost(5) == 0
        && lost(0) == 0 && world.sheet(0).resources.at("slots-3").current == 3, "Cinder burst burns everyone two squares around the spot");
    heal();
    check(cast(0, "earth-heave", std::nullopt, cell(5)) && lost(5) >= 3 && lost(5) <= 18 && world.sheet(5).hasCondition("prone")
        && lost(1) == 0 && !world.sheet(1).hasCondition("prone"), "Earth heave hurts and floors enemies only");
    heal();

    // Cleric.
    check(cast(1, "rebuke", 3) && lost(3) >= 1 && lost(3) <= 6 && world.sheet(3).hasCondition("frightened"),
        "Rebuke does 1d6 radiant and frightens");
    heal();
    check(cast(1, "brand-of-light", 4) && lost(4) >= 3 && lost(4) <= 18 && world.sheet(4).hasCondition("off-guard"),
        "Brand of light does 3d6 radiant and leaves the goblin off-guard");
    heal();
    const int fighterAttack = world.sheet(2).attackModifier(world.rules());
    const int wizardAttack = world.sheet(0).attackModifier(world.rules());
    const int goblinAttack = world.sheet(3).attackModifier(world.rules());
    check(cast(1, "rallying-hymn", std::nullopt) && world.sheet(2).attackModifier(world.rules()) == fighterAttack + 1
        && world.sheet(0).attackModifier(world.rules()) == wizardAttack + 1 && world.sheet(3).attackModifier(world.rules()) == goblinAttack
        && world.creatures()[1].concentration.spell == "rallying-hymn", "Rallying hymn lifts the allies near the cleric and nobody else");
    const int fighterAc = world.sheet(2).armorClass(world.rules());
    check(cast(1, "iron-vow", 2) && world.sheet(2).armorClass(world.rules()) == fighterAc + 2
        && world.sheet(2).attackModifier(world.rules()) == fighterAttack && world.creatures()[1].concentration.spell == "iron-vow",
        "Iron vow gives +2 AC and ends the hymn");
    world.endConcentration(1, "");
    check(world.sheet(2).armorClass(world.rules()) == fighterAc, "The vow ends with the cleric's concentration");
    check(cast(1, "dawnburst", std::nullopt, cell(4)) && lost(3) >= 4 && lost(3) <= 32 && lost(4) == lost(3) && lost(0) == 0,
        "Dawnburst burns the goblins and spares the party");
    heal();
    world.sheet(2).addCondition(world.rules(), "frightened", 3, 2);
    check(cast(1, "steadfast-chorus", std::nullopt) && !world.sheet(2).hasCondition("frightened") && world.sheet(2).tempHp >= 5
        && world.sheet(0).tempHp >= 5 && world.sheet(3).tempHp == 0 && world.sheet(1).resources.at("slots-3").current == 2,
        "Steadfast chorus ends fear and gives the party temporary HP");
}

void badFiles(const Check& check)
{
    auto broken = hallFiles();
    broken["rulesets/yorehold/spells/murk.json"] = R"({"id":"murk","level":1,"effects":[{"do":"condition","id":"missing-condition"}]})";
    WorldFixture invalid;
    std::string error;
    check(!invalid.loadJson("chapters/spell-hall", broken, 1, &error) && error.find("murk") != std::string::npos
        && error.find("missing-condition") != std::string::npos, "An unknown condition in a spell names the file while loading");
    auto clash = hallFiles();
    clash["rulesets/yorehold/spells/strike.json"] = R"({"id":"strike","effects":[{"do":"damage","dice":1}]})";
    check(!invalid.loadJson("chapters/spell-hall", clash, 1, &error) && error.find("strike") != std::string::npos,
        "A spell may not take an action's id");
    auto rules = hallFiles();
    rules["rulesets/yorehold/spellcasting.json"] = R"({"concentration":{"ability":"luck"}})";
    check(!invalid.loadJson("chapters/spell-hall", rules, 1, &error) && error.find("spellcasting.json") != std::string::npos,
        "Spellcasting rules naming an unknown ability stop the chapter loading");
    // Hands that only set the cost: the cleric casts with a mace and a shield.
    auto loose = hallFiles();
    loose["rulesets/yorehold/spellcasting.json"] = R"({"hands":"ignored"})";
    WorldFixture easy;
    check(easy.loadJson("chapters/spell-hall", loose, 1, &error), "A ruleset may ignore hands");
    if (!easy.chapter()) return;
    easy.sheet(2).hp = 1;
    check(easy.send("cast", {{"hero", 1}, {"spell", "mend"}, {"target", 2}}) && easy.sheet(2).hp > 1, "With hands ignored, a full-handed cleric casts");
}

}

void worldSpellTests(const Check& check)
{
    betweenFights(check);
    aiming(check);
    concentration(check);
    preparing(check);
    focusSpells(check);
    starterLists(check);
    badFiles(check);
}
