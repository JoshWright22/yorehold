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
                hiding |= tokens_.tokens[i].owner == player && creatures_[i].sneaking && !creatures_[i].sheet.down();
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
            return accepted;
        }
        if (type == "dash")
            return acting && encounter_->canAct() ? std::optional(accepted) : std::nullopt;
        if (type == "attack")
        {
            const size_t target = j.at("target").get<size_t>();
            if (!acting || target >= creatures_.size() || !encounter_->canStrike() || creatures_[target].sheet.down()
                || creatures_[target].team == creatures_[*current].team || !orderIndex(target) || !adjacent(*current, target))
                return std::nullopt;
            return accepted;
        }
        if (type == "end")
            return acting ? std::optional(accepted) : std::nullopt;
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
        if (type == "rest")
        {
            const size_t index = j.at("rest").get<size_t>();
            if (!calm || talk_ || index >= rules_.rests.size() || restsLeft(rules_.rests[index]) == 0)
                return std::nullopt;
            return accepted;
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
            creatures_[i].sneaking = on;
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
        const std::vector<yh::Cell> path = findPath(grid_, standing_, to, [this](yh::Cell c) { return c == standing_ || reach_.contains(c); });
        for (size_t i = 1; i < path.size(); i++)
            token.path.push_back(grid_.center(path[i]));
        encounter_->spendMovement(std::min(squares, encounter_->current().budget.movementLeft));
        computeReach(*current);
    }
    else if (type == "dash" && current)
    {
        encounter_->dash();
        syncLog();
        computeReach(*current);
    }
    else if (type == "attack")
        attack(j.at("target").get<size_t>());
    else if (type == "end")
        endTurn();
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
        if (encounter_->finished())
            endCombat();
        else
            endTurn();
    }
    else if (type == "rest")
        rest(rules_.rests[j.at("rest").get<size_t>()]);
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
    for (const Creature& c : creatures_)
        mix(static_cast<uint64_t>(c.sheet.hp + 1000));
    for (const std::string& flag : flags_)
        for (const char ch : flag)
            mix(static_cast<unsigned char>(ch));
    mix(rolls_);
    if (encounter_ && !encounter_->finished() && encounter_->started())
    {
        mix(encounter_->currentIndex());
        mix(static_cast<uint64_t>(encounter_->round()));
        mix(static_cast<uint64_t>(encounter_->order()[encounter_->currentIndex()].budget.movementLeft + 100));
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
