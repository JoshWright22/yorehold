// The one path that changes the game: intent -> validate (host) -> command -> apply (everyone).
// Alone, act() runs both halves at once; in co-op the session carries intents to the host and
// commands back to every copy, which apply them in the same order.

#include "World.h"

#include <yorehold/framework/map/Pathfinding.h>

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <span>

namespace
{

bool finiteWorld(const nlohmann::json& point)
{
    return point.is_array() && point.size() == 2 && point[0].is_number() && point[1].is_number()
        && std::isfinite(point[0].get<double>()) && std::isfinite(point[1].get<double>());
}

}

void World::act(std::string_view type, const std::string& data)
{
    if (!chapter_)
        return;
    std::string reason;
    if (std::optional<std::string> accepted = validate(0, type, data, reason))
        apply({0, 0, std::string(type), std::move(*accepted)});
    else if (!reason.empty())
        say(reason);
}

// Runs on the host (or alone) before a command is accepted. Anything a player sends is checked
// here; apply() can then trust it.
std::optional<std::string> World::validate(yh::PlayerId player, std::string_view type, std::string_view data, std::string& reason)
{
    if (!chapter_)
        return std::nullopt;
    try
    {
        const nlohmann::json j = nlohmann::json::parse(data);
        if (!j.is_object())
            return std::nullopt;
        const bool fighting = encounter_ && !encounter_->finished();
        const std::optional<size_t> current = currentCreature();
        const bool acting = fighting && current && mayAct(player, *current) && !inCutscene_;
        const bool calm = !fighting && !inCutscene_ && !partyDown();
        auto cellFrom = [](const nlohmann::json& at) { return yh::Cell{at.at(0).get<int>(), at.at(1).get<int>()}; };
        const std::string accepted(data);

        if (type == "wipe-return")
        {
            if (player != 0 || !pendingWipe_ || inCutscene_) return std::nullopt;
            return nlohmann::json{{"checkpoint", checkpoint_}}.dump();
        }

        if (type == "reaction")
        {
            if (!pendingReaction_ || !reactionPrompt_ || !j.at("take").is_boolean()
                || j.at("creature").get<size_t>() != pendingReaction_->creature || j.at("offer").get<uint64_t>() != reactionPrompt_->id)
                return std::nullopt;
            const bool take = j.at("take").get<bool>();
            const bool timedOut = player == 0 && take && reactionPrompt_->secondsLeft <= 0;
            if (!mayAct(player, pendingReaction_->creature) && !timedOut)
                return std::nullopt;
            return nlohmann::json{{"take", take}, {"creature", pendingReaction_->creature}, {"offer", reactionPrompt_->id}}.dump();
        }
        if (pendingMovement_ && type != "seats")
            return std::nullopt;

        if (type == "turn")
        {
            const size_t creature = j.at("creature").get<size_t>();
            if (!canChooseTurn(creature) || !mayAct(player, creature)) return std::nullopt;
            return nlohmann::json{{"creature", creature}}.dump();
        }

        if (type == "walk")
        {
            if (fighting || !j.at("heroes").is_array())
                return std::nullopt;
            const yh::Rect bounds = map().map().worldBounds();
            for (const nlohmann::json& h : j.at("heroes"))
            {
                const size_t t = h.at("t").get<size_t>();
                if (t >= heroCount_ || tokens_.tokens[t].owner != player || !finiteWorld(h.at("at")) || !h.at("path").is_array() || h.at("path").size() > 256)
                    return std::nullopt;
                if (!bounds.contains({h.at("at")[0].get<float>(), h.at("at")[1].get<float>()}))
                    return std::nullopt;
                for (const nlohmann::json& p : h.at("path"))
                    if (!finiteWorld(p))
                        return std::nullopt;
            }
            return accepted;
        }
        if (type == "go")
        {
            // A hero walking to a square between fights (the mouse walks heroes through the token
            // controller and "walk" instead).
            const size_t hero = j.at("hero").get<size_t>();
            if (!calm || talk_ || hero >= heroCount_ || !mayAct(player, hero) || !canGo(hero, cellFrom(j.at("at"))))
                return std::nullopt;
            return accepted;
        }
        if (type == "fight")
        {
            const int group = j.at("group").get<int>();
            if (player != 0 || !calm || talk_ || group < 0 || group >= static_cast<int>(chapter_->encounters.size())
                || j.at("at").size() != creatures_.size() || (j.contains("note") && !j.at("note").is_string()))
                return std::nullopt;
            for (const nlohmann::json& p : j.at("at"))
                if (!finiteWorld(p))
                    return std::nullopt;
            return accepted;
        }
        if (type == "provoke")
        {
            // Picking a fight with an NPC or someone who surrendered. The host adds where everyone
            // stands, as for "fight".
            const size_t creature = j.at("creature").get<size_t>();
            if (!calm || talk_ || !talkable(creature))
                return std::nullopt;
            nlohmann::json at = nlohmann::json::array();
            for (const yh::Token& t : std::span(tokens_.tokens).first(creatures_.size()))
                at.push_back({t.position.x, t.position.y});
            return nlohmann::json{{"creature", creature}, {"at", at}}.dump();
        }
        if (type == "sneak")
        {
            // A player's standing heroes start or stop sneaking together.
            bool any = false;
            for (size_t i = 0; i < heroCount_; i++)
                any |= tokens_.tokens[i].owner == player && !creatures_[i].sheet.down();
            return calm && !talk_ && any && j.at("on").is_boolean() ? std::optional(accepted) : std::nullopt;
        }
        if (type == "unseen") // the host telling everyone a sneaking hero passed a check
            return player == 0 && calm && j.at("hero").get<size_t>() < heroCount_ ? std::optional(accepted) : std::nullopt;
        if (type == "ambush")
        {
            // Attacking from hiding: the fight starts with the enemies caught off guard.
            const size_t creature = j.at("creature").get<size_t>();
            if (!calm || talk_ || creature < heroCount_ || creature >= creatures_.size() || creatures_[creature].team != 1
                || creatures_[creature].awake || creatures_[creature].sheet.down()
                || fog_.state(0, 0, cellOf(creature)) != yh::FogState::Visible)
                return std::nullopt;
            bool hiding = false;
            for (size_t i = 0; i < heroCount_; i++)
                hiding |= tokens_.tokens[i].owner == player && creatures_[i].sneaking() && !creatures_[i].sheet.down();
            if (!hiding)
                return std::nullopt;
            nlohmann::json at = nlohmann::json::array();
            for (const yh::Token& t : std::span(tokens_.tokens).first(creatures_.size()))
                at.push_back({t.position.x, t.position.y});
            return nlohmann::json{{"creature", creature}, {"at", at}}.dump();
        }
        if (type == "step")
        {
            if (!acting)
                return std::nullopt;
            const yh::Cell to = cellFrom(j.at("at"));
            computeReach(*current);
            if (to == standing_ || !reach_.contains(to))
            {
                reason = "Can't move there.";
                return std::nullopt;
            }
            return nlohmann::json{{"at", {to.x, to.y}}, {"prompts", options_.reactionPrompts}}.dump();
        }
        if (type == "use")
        {
            // One of the acting creature's actions (the ruleset's actions/ files): it must have it,
            // be able to pay for it, and aim it at someone it may be aimed at.
            const std::string id = j.at("action").get<std::string>();
            const yh::ActionDefinition* action = acting ? findAction(id) : nullptr;
            if (!action || !canUse(*current, *action))
            {
                // A spell says what it lacks (a slot, a free hand); other actions stay quiet as before.
                std::string lacks;
                if (const yh::SpellDefinition* spell = action ? findSpell(id) : nullptr;
                    spell && &spell->action == action && !yh::canCast(creatures_[*current].sheet, *spell, spellRules(), &lacks))
                    reason = spell->name() + " " + lacks + ".";
                return std::nullopt;
            }
            nlohmann::json command{{"action", id}};
            // A spell names the slot it spends: the one asked for, else the lowest that will do.
            if (const yh::SpellDefinition* spell = findSpell(id); spell && &spell->action == action)
            {
                const std::optional<int> slot = yh::slotFor(creatures_[*current].sheet, *spell, spellRules(), j.value("slot", 0));
                if (!slot)
                    return std::nullopt;
                command["slot"] = *slot;
            }
            if (action->target == yh::ActionDefinition::Target::Self)
                return command.dump();
            if (action->target == yh::ActionDefinition::Target::Point)
            {
                const yh::Cell at = cellFrom(j.at("at"));
                if (!validAim(*current, *action, at, &reason))
                    return std::nullopt;
                command["at"] = {at.x, at.y};
                return command.dump();
            }
            const size_t target = j.at("target").get<size_t>();
            if (!validTarget(*current, *action, target))
                return std::nullopt;
            command["target"] = target;
            return command.dump();
        }
        if (type == "cast")
        {
            // A spell between fights, on the party: free of actions, but it spends its slot.
            const size_t hero = j.at("hero").get<size_t>();
            const std::string id = j.at("spell").get<std::string>();
            const size_t target = j.value("target", hero);
            if (!canCast(hero, id, target, &reason) || !mayAct(player, hero))
                return std::nullopt;
            const std::optional<int> slot = yh::slotFor(creatures_[hero].sheet, *findSpell(id), spellRules(), j.value("slot", 0));
            if (!slot)
                return std::nullopt;
            return nlohmann::json{{"hero", hero}, {"spell", id}, {"target", target}, {"slot", *slot}}.dump();
        }
        if (type == "prepare")
        {
            // A prepared caster's spells for the day, between fights after the right rest.
            const size_t hero = j.at("hero").get<size_t>();
            const auto spells = j.at("spells").get<std::vector<std::string>>();
            if (!canPrepare(hero, spells, &reason) || !mayAct(player, hero))
                return std::nullopt;
            return nlohmann::json{{"hero", hero}, {"spells", spells}}.dump();
        }
        // Only the creatures the game plays lose their nerve; "escape" takes one out of the fight for
        // good, "surrender" leaves it standing; "alarm" brings in a group it ran to.
        if (type == "flee")
        {
            const std::string as = j.value("as", "flee");
            return acting && *current >= heroCount_ && (as == "flee" || as == "alarm") ? std::optional(accepted) : std::nullopt;
        }
        if (type == "escape" || type == "surrender")
            return acting && *current >= heroCount_ ? std::optional(accepted) : std::nullopt;
        if (type == "alarm")
        {
            const int group = j.at("group").get<int>();
            return acting && *current >= heroCount_ && sleepingGroupNear(*current, aiFor(*current).alarmReach) == group ? std::optional(accepted) : std::nullopt;
        }
        if (type == "consume")
        {
            const size_t hero = j.at("hero").get<size_t>(), item = j.at("item").get<size_t>();
            const size_t target = j.value("target", hero);
            if (!canConsume(hero, item, target, &reason) || !mayAct(player, hero))
                return std::nullopt;
            return nlohmann::json{{"hero", hero}, {"item", item}, {"target", target}}.dump();
        }
        if (type == "equip")
        {
            // Putting an item on or away: free between fights, an Interact on the hero's own turn in one.
            const size_t hero = j.at("hero").get<size_t>();
            const size_t item = j.at("item").get<size_t>();
            if (hero >= heroCount_ || !mayAct(player, hero) || creatures_[hero].sheet.down() || !j.at("on").is_boolean()
                || item >= creatures_[hero].sheet.inventory.size() || creatures_[hero].sheet.inventory[item].slot.empty()
                || creatures_[hero].sheet.inventory[item].equipped == j.at("on").get<bool>())
                return std::nullopt;
            if (fighting)
            {
                if (!acting || *current != hero)
                {
                    reason = "Gear can only be changed on " + creatures_[hero].sheet.name + "'s own turn.";
                    return std::nullopt;
                }
                if (!encounter_->canAct(equipCost(hero)))
                {
                    reason = "Not enough actions left to change gear.";
                    return std::nullopt;
                }
            }
            else if (!calm || talk_)
                return std::nullopt;
            return accepted;
        }
        if (type == "loot")
        {
            // Taking from a pile the hero stands on or beside: one item, the coins, or all of it.
            const size_t hero = j.at("hero").get<size_t>();
            const size_t pile = j.at("pile").get<size_t>();
            if (!calm || talk_ || hero >= heroCount_ || !mayAct(player, hero) || creatures_[hero].sheet.down() || pile >= piles_.size()
                || piles_[pile].empty())
                return std::nullopt;
            const yh::Cell at = cellOf(hero);
            if (std::abs(piles_[pile].at.x - at.x) > 1 || std::abs(piles_[pile].at.y - at.y) > 1)
            {
                reason = creatures_[hero].sheet.name + " is too far from " + piles_[pile].name + ".";
                return std::nullopt;
            }
            if (j.value("all", false))
                return nlohmann::json{{"hero", hero}, {"pile", pile}, {"all", true}}.dump();
            if (j.value("coins", false))
                return piles_[pile].coins > 0 ? std::optional(nlohmann::json{{"hero", hero}, {"pile", pile}, {"coins", true}}.dump()) : std::nullopt;
            const size_t item = j.at("item").get<size_t>();
            if (item >= piles_[pile].items.size())
                return std::nullopt;
            if (piles_[pile].items[item].magic && !creatures_[hero].sheet.roomForMagic(rules_, piles_[pile].items[item].quantity))
            {
                reason = magicLimitText(hero);
                return std::nullopt;
            }
            return nlohmann::json{{"hero", hero}, {"pile", pile}, {"item", item}}.dump();
        }
        if (type == "give")
        {
            // Handing an item or coins to another hero, between fights.
            const size_t from = j.at("from").get<size_t>();
            const size_t to = j.at("to").get<size_t>();
            if (!calm || talk_ || from >= heroCount_ || to >= heroCount_ || from == to || !mayAct(player, from)
                || creatures_[from].sheet.down() || creatures_[to].sheet.down())
                return std::nullopt;
            if (j.contains("coins"))
            {
                const int coins = j.at("coins").get<int>();
                return coins > 0 && coins <= creatures_[from].sheet.coins
                    ? std::optional(nlohmann::json{{"from", from}, {"to", to}, {"coins", coins}}.dump()) : std::nullopt;
            }
            const size_t item = j.at("item").get<size_t>();
            if (item >= creatures_[from].sheet.inventory.size())
                return std::nullopt;
            const yh::Item& given = creatures_[from].sheet.inventory[item];
            if (given.magic && !creatures_[to].sheet.roomForMagic(rules_, given.quantity))
            {
                reason = magicLimitText(to);
                return std::nullopt;
            }
            return nlohmann::json{{"from", from}, {"to", to}, {"item", item}}.dump();
        }
        if (type == "rest")
        {
            const size_t index = j.at("rest").get<size_t>();
            if (!calm || talk_ || index >= rules_.rests.size() || restsLeft(rules_.rests[index]) == 0)
                return std::nullopt;
            return accepted;
        }
        if (type == "buy" || type == "sell")
        {
            const size_t hero = j.at("hero").get<size_t>();
            const size_t npc = j.at("npc").get<size_t>();
            const size_t item = j.at("item").get<size_t>();
            if (!calm || !canTrade(hero, npc) || !mayAct(player, hero))
            {
                reason = "Stand beside a peaceful merchant to trade.";
                return std::nullopt;
            }
            const yh::Merchant& shop = *merchant(npc);
            const yh::Character& sheet = creatures_[hero].sheet;
            if (!(type == "buy" ? shop.canBuy(sheet, item, rules_, &reason) : shop.canSell(sheet, item, &reason)))
                return std::nullopt;
            return nlohmann::json{{"hero", hero}, {"npc", npc}, {"item", item}}.dump();
        }
        if (type == "talk")
        {
            const size_t creature = j.at("creature").get<size_t>();
            return calm && !talk_ && talkable(creature) ? std::optional(accepted) : std::nullopt;
        }
        if (type == "reply")
        {
            const size_t hero = j.at("hero").get<size_t>();
            if (!talk_ || j.at("choice").get<int>() < 0 || hero >= heroCount_ || !mayAct(player, hero) || creatures_[hero].sheet.down())
                return std::nullopt;
            return accepted;
        }
        if (type == "leave")
            return talk_ ? std::optional(accepted) : std::nullopt;
        if (type == "seats")
            return player == 0 && j.at("owners").size() == heroCount_ ? std::optional(accepted) : std::nullopt;
        if (type == "restart")
            return player == 0 ? std::optional(nlohmann::json{{"seed", j.at("seed").get<uint64_t>()}}.dump()) : std::nullopt;
    }
    catch (const std::exception&)
    {
        // Malformed data from a peer: refuse it quietly.
    }
    return std::nullopt;
}

void World::apply(const yh::NetCommand& command)
{
    const nlohmann::json j = nlohmann::json::parse(command.data);
    const std::string& type = command.type;
    const std::optional<size_t> current = currentCreature();

    if (type == "wipe-return")
    {
        returnFromWipe(j.at("checkpoint").get<std::string>());
        return;
    }
    if (type == "walk")
    {
        // Someone else's heroes: follow where they say they're going.
        for (const nlohmann::json& h : j.at("heroes"))
        {
            yh::Token& token = tokens_.tokens[h.at("t").get<size_t>()];
            if (token.owner == self_)
                continue;
            const yh::Vec2 at{h.at("at")[0].get<float>(), h.at("at")[1].get<float>()};
            token.path.clear();
            for (const nlohmann::json& p : h.at("path"))
                token.path.push_back({p[0].get<float>(), p[1].get<float>()});
            const yh::Vec2 off = token.position - at;
            if (token.path.empty() || off.x * off.x + off.y * off.y > GameMap::cellSize * GameMap::cellSize)
                token.position = at;
        }
    }
    else if (type == "go")
        go(j.at("hero").get<size_t>(), {j.at("at")[0].get<int>(), j.at("at")[1].get<int>()});
    else if (type == "sneak")
    {
        const bool on = j.at("on").get<bool>();
        for (size_t i = 0; i < heroCount_; i++)
        {
            if (tokens_.tokens[i].owner != command.player || creatures_[i].sheet.down())
                continue;
            setSneaking(i, on);
            sneak_[i].reset();
        }
        if (command.player == self_)
            say(on ? "Sneaking: slower, with lights covered. Stay out of the red cones." : "No longer sneaking.");
    }
    else if (type == "unseen")
        emit({Event::Kind::Floater, "Unseen", tokens_.tokens[j.at("hero").get<size_t>()].position, FloatKind::Unseen});
    else if (type == "fight" || type == "provoke" || type == "ambush")
    {
        const nlohmann::json& at = j.at("at");
        for (size_t i = 0; i < creatures_.size(); i++)
        {
            tokens_.tokens[i].position = {at[i][0].get<float>(), at[i][1].get<float>()};
            tokens_.tokens[i].path.clear();
        }
        talk_.reset();
        pendingTalk_.reset();
        if (type == "fight")
        {
            if (const std::string note = j.value("note", ""); !note.empty())
                say(note);
            startCombat(j.at("group").get<int>());
            return;
        }
        if (type == "ambush")
        {
            startCombat(creatures_[j.at("creature").get<size_t>()].group, std::nullopt, true);
            return;
        }
        turnHostile(j.at("creature").get<size_t>());
    }
    else if (type == "step" && current)
    {
        // Finish any walk still playing, then take the new one and pay for it.
        yh::Token& token = tokens_.tokens[*current];
        if (!token.path.empty())
            token.position = token.path.back();
        token.path.clear();
        computeReach(*current);
        const yh::Cell to{j.at("at")[0].get<int>(), j.at("at")[1].get<int>()};
        const auto cost = reach_.find(to);
        if (cost == reach_.end())
            return;
        const int squares = static_cast<int>(std::ceil(cost->second - 0.01f));
        std::vector<yh::Cell> path = findPath(grid_, standing_, to, [this](yh::Cell c) { return c == standing_ || reach_.contains(c); });
        encounter_->spendMovement(std::min(squares, encounter_->current().budget.movementLeft));
        creatures_[*current].sheet.conditionEvent(rules_, "move");
        startMovement(*current, std::move(path), j.value("prompts", false));
    }
    else if (type == "reaction")
    {
        resolveReaction(j.at("take").get<bool>());
        continueMovement();
    }
    else if (type == "turn")
    {
        if (const auto index = orderIndex(j.at("creature").get<size_t>()); index && encounter_->selectTurn(*index))
        {
            syncLog();
            beginTurn();
        }
    }
    else if (type == "use" && current)
    {
        if (const yh::ActionDefinition* action = findAction(j.at("action").get<std::string>()))
            perform(*action, j.contains("target") ? std::optional(j.at("target").get<size_t>()) : std::nullopt,
                j.contains("at") ? std::optional(yh::Cell{j.at("at")[0].get<int>(), j.at("at")[1].get<int>()}) : std::nullopt, j.value("slot", 0));
    }
    else if (type == "cast")
    {
        const size_t hero = j.at("hero").get<size_t>();
        if (const yh::SpellDefinition* spell = findSpell(j.at("spell").get<std::string>()))
        {
            castSpell(hero, *spell, j.at("target").get<size_t>(), std::nullopt, j.value("slot", 0));
            requestSave();
        }
    }
    else if (type == "prepare")
    {
        yh::Character& sheet = creatures_[j.at("hero").get<size_t>()].sheet;
        if (sheet.prepare(j.at("spells").get<std::vector<std::string>>()))
        {
            std::string names;
            for (const std::string& id : sheet.prepared)
            {
                const yh::SpellDefinition* spell = findSpell(id);
                names += (names.empty() ? "" : ", ") + (spell ? spell->name() : id);
            }
            say(sheet.name + " prepares " + names + ".");
            requestSave();
        }
    }
    else if (type == "flee" && current)
    {
        Creature& runner = creatures_[*current];
        runner.fleeing = true;
        runner.breakAs = j.value("as", "flee");
        say(runner.sheet.name + (runner.breakAs == "alarm" ? " runs for help!" : " turns and runs!"));
    }
    else if (type == "surrender" && current)
    {
        Creature& yielded = creatures_[*current];
        yh::Token& token = tokens_.tokens[*current];
        if (!token.path.empty())
            token.position = token.path.back();
        token.path.clear();
        yielded.surrendered = true;
        yielded.fleeing = false;
        yielded.team = 2;
        say(yielded.sheet.name + " throws down their weapon and surrenders!");
        encounter_->withdraw(*orderIndex(*current), "surrenders");
        syncLog();
        if (encounter_->finished())
            endCombat();
        else
            endTurn();
    }
    else if (type == "alarm" && current)
    {
        // Everyone asleep in that group joins the fight where they stand.
        const int group = j.at("group").get<int>();
        Creature& runner = creatures_[*current];
        say(runner.sheet.name + " raises the alarm!");
        runner.fleeing = false;
        runner.breakAs = "fight"; // with friends at its side it fights on
        for (size_t i = heroCount_; i < creatures_.size(); i++)
        {
            Creature& c = creatures_[i];
            if (c.group != group || c.team != 1 || c.awake || c.sheet.down())
                continue;
            c.awake = true;
            encounter_->join(c.sheet, 1);
            sideAtStart_[1]++;
            hadLeader_[1] |= aiFor(i).leader;
        }
        if (group < static_cast<int>(chapter_->encounters.size()) && !chapter_->encounters[group].text.empty())
            say(chapter_->encounters[group].text);
        syncLog();
    }
    else if (type == "escape" && current)
    {
        Creature& gone = creatures_[*current];
        yh::Token& token = tokens_.tokens[*current];
        say(gone.sheet.name + " gets away.");
        if (!token.path.empty())
            token.position = token.path.back();
        token.path.clear();
        token.floor = dead;
        gone.sheet.hp = 0;
        gone.fled = true;
        gone.sheet.conditions.clear(); // it is out of the adventure, not lying dead in it
        if (encounter_->finished())
            endCombat();
        else
            endTurn();
    }
    else if (type == "equip")
    {
        const size_t hero = j.at("hero").get<size_t>();
        const size_t index = j.at("item").get<size_t>();
        yh::Character& sheet = creatures_[hero].sheet;
        if (encounter_ && !encounter_->finished())
            encounter_->spendActions(equipCost(hero));
        if (j.at("on").get<bool>())
        {
            // Whatever it pushed out of a slot or a hand is named too.
            std::vector<bool> before;
            for (const yh::Item& item : sheet.inventory)
                before.push_back(item.equipped);
            sheet.equip(index);
            std::string away;
            for (size_t i = 0; i < sheet.inventory.size(); i++)
                if (before[i] && !sheet.inventory[i].equipped)
                    away += (away.empty() ? "" : ", ") + sheet.inventory[i].name;
            say(sheet.name + (yh::Character::held(sheet.inventory[index]) ? " takes up " : " puts on ") + sheet.inventory[index].name
                + (away.empty() ? "." : ", putting away " + away + "."));
        }
        else
        {
            sheet.unequip(index);
            say(sheet.name + (yh::Character::held(sheet.inventory[index]) ? " puts away " : " takes off ") + sheet.inventory[index].name + ".");
        }
        sheet.hp = std::min(sheet.hp, sheet.maxHp());
        syncLog();
    }
    else if (type == "loot")
    {
        yh::Character& sheet = creatures_[j.at("hero").get<size_t>()].sheet;
        Pile& pile = piles_[j.at("pile").get<size_t>()];
        const bool all = j.value("all", false);
        std::string taken;
        auto note = [&](const std::string& what) { taken += (taken.empty() ? "" : ", ") + what; };
        if ((all || j.value("coins", false)) && pile.coins > 0)
        {
            note(coinText(pile.coins));
            sheet.coins += pile.coins;
            pile.coins = 0;
        }
        const size_t first = all ? 0 : j.value("item", pile.items.size());
        std::string left;
        for (size_t i = first; i < pile.items.size() && (all || i == first);)
        {
            // Taking everything leaves the magic items there is no room for.
            if (pile.items[i].magic && !sheet.roomForMagic(rules_, pile.items[i].quantity))
            {
                left += (left.empty() ? "" : ", ") + pile.items[i].name;
                i++;
                continue;
            }
            note(pile.items[i].name + (pile.items[i].quantity > 1 ? " x" + std::to_string(pile.items[i].quantity) : ""));
            addTo(sheet, pile.items[i]);
            pile.items.erase(pile.items.begin() + static_cast<std::ptrdiff_t>(i));
            if (!all)
                break;
        }
        if (!taken.empty())
            say(sheet.name + " takes " + taken + " (" + pile.name + ").");
        if (!left.empty())
            say(sheet.name + " leaves " + left + ": " + std::to_string(rules_.magicItemLimit) + " magic items is all anyone can carry.");
    }
    else if (type == "give")
    {
        yh::Character& from = creatures_[j.at("from").get<size_t>()].sheet;
        yh::Character& to = creatures_[j.at("to").get<size_t>()].sheet;
        if (j.contains("coins"))
        {
            const int coins = j.at("coins").get<int>();
            from.coins -= coins;
            to.coins += coins;
            say(from.name + " gives " + coinText(coins) + " to " + to.name + ".");
        }
        else
        {
            const size_t index = j.at("item").get<size_t>();
            from.unequip(index);
            const yh::Item item = from.inventory[index];
            // Later items move up a place: take their modifiers off and put them back under their new index.
            std::vector<bool> worn;
            for (size_t i = index + 1; i < from.inventory.size(); i++)
            {
                worn.push_back(from.inventory[i].equipped);
                from.unequip(i);
            }
            from.inventory.erase(from.inventory.begin() + static_cast<std::ptrdiff_t>(index));
            for (size_t i = 0; i < worn.size(); i++)
                if (worn[i])
                    from.equip(index + i);
            from.hp = std::min(from.hp, from.maxHp());
            addTo(to, item);
            say(from.name + " gives " + item.name + " to " + to.name + ".");
        }
    }
    else if (type == "consume")
        consume(j.at("hero").get<size_t>(), j.at("item").get<size_t>(), j.at("target").get<size_t>());
    else if (type == "rest")
        rest(rules_.rests[j.at("rest").get<size_t>()]);
    else if (type == "buy" || type == "sell")
    {
        const size_t npc = j.at("npc").get<size_t>(), item = j.at("item").get<size_t>();
        yh::Merchant& shop = *merchants_[npc];
        yh::Character& sheet = creatures_[j.at("hero").get<size_t>()].sheet;
        const bool buying = type == "buy";
        const yh::Item traded = buying ? shop.inventory[item] : sheet.inventory[item];
        const int cost = buying ? shop.buyPrice(traded) : shop.sellPrice(traded);
        if (buying ? shop.buy(sheet, item, rules_) : shop.sell(sheet, item))
        {
            say(sheet.name + (buying ? " buys " : " sells ") + traded.name + " for " + coinText(cost)
                + (buying ? " from " : " to ") + chapter_->npcs[npc].name + ".");
            requestSave();
        }
    }
    else if (type == "talk")
        startTalk(j.at("creature").get<size_t>());
    else if (type == "reply")
        chooseReply(j.at("choice").get<size_t>(), j.at("hero").get<size_t>());
    else if (type == "leave")
        talk_.reset();
    else if (type == "seats")
    {
        seats_ = j.at("owners").get<std::vector<int>>();
        playerNames_.clear();
        for (const auto& [id, name] : j.at("names").items())
            playerNames_[std::atoi(id.c_str())] = name.get<std::string>();
        for (size_t i = 0; i < heroCount_; i++)
            tokens_.tokens[i].owner = seats_[i];
        tokens_.clearLinks();
        for (size_t i = 1; i < heroCount_; i++)
            tokens_.link(i, i - 1);
        selectOwnHero();
        std::string who;
        for (size_t i = 0; i < heroCount_; i++)
            who += (i ? ", " : "") + creatures_[i].sheet.name + ": " + seatName(i);
        say("Seats: " + who);
    }
    else if (type == "restart")
        newAdventure(j.at("seed").get<uint64_t>());
}

// Everything a desync would show up in: health, story, and the fight's turn and positions.
uint64_t World::checksum() const
{
    uint64_t hash = 14695981039346656037ull;
    auto mix = [&](uint64_t value) {
        for (int i = 0; i < 8; i++)
        {
            hash ^= (value >> (i * 8)) & 0xff;
            hash *= 1099511628211ull;
        }
    };
    auto mixText = [&](std::string_view text) {
        mix(text.size());
        for (const char ch : text) mix(static_cast<unsigned char>(ch));
    };
    for (const Creature& c : creatures_)
    {
        mix(c.sheet.coins);
        mix(c.sheet.inventory.size());
        for (const yh::Item& item : c.sheet.inventory)
        {
            mixText(yh::Compendium::itemToJson(item));
            mix(item.equipped);
        }
        mix(static_cast<uint64_t>(c.sheet.hp + 1000));
        for (const auto& [id, resource] : c.sheet.resources)
        {
            mixText(id);
            mix(static_cast<uint64_t>(resource.current));
        }
        mixText(c.concentration.toJson());
        mix(c.sheet.level);
        mix(c.sheet.death.saves); mix(c.sheet.death.successes); mix(c.sheet.death.failures);
        mix(c.sheet.death.stable); mix(c.sheet.death.dead);
        mixText(c.sheet.dcAbility);
        mix(c.sheet.proficiencyRanks.size());
        for (const auto& [target, rank] : c.sheet.proficiencyRanks)
        {
            mixText(target);
            mixText(rank);
        }
    }
    for (const std::string& flag : flags_)
        for (const char ch : flag)
            mix(static_cast<unsigned char>(ch));
    mix(rolls_);
    mixText(merchantsJson().dump());
    if (encounter_ && !encounter_->finished() && encounter_->started())
    {
        mix(encounter_->currentIndex());
        mix(static_cast<uint64_t>(encounter_->round()));
        mix(encounter_->blockFirst());
        mix(encounter_->blockEnd());
        for (const yh::Combatant& c : encounter_->order())
        {
            mix(c.turnDone);
            mix(c.budget.actions);
            mix(c.budget.movementLeft);
            mix(c.budget.bonusAction);
            mix(c.budget.reaction);
        }
        for (size_t i = 0; i < creatures_.size(); i++)
        {
            const yh::Cell c = cellOf(i);
            mix(static_cast<uint64_t>((c.x + 1000) * 100000 + c.y + 1000));
        }
    }
    if (talk_ && talk_->current())
        for (const char ch : talk_->current()->id)
            mix(static_cast<unsigned char>(ch));
    return hash;
}
