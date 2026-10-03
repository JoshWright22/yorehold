// Co-op: hosting, joining, and the commands every copy of the game applies in the same order.

#include "YoreholdGame.h"

#include <yorehold/framework/map/Pathfinding.h>
#include <yorehold/framework/net/Transport.h>

#include <nlohmann/json.hpp>

#include <SDL3/SDL_stdinc.h>
#include <SDL3/SDL_timer.h>

#include <algorithm>
#include <cmath>
#include <span>
#include <cstdio>
#include <cstdlib>

namespace
{

constexpr const char* gameName = "yorehold";

bool finiteWorld(const nlohmann::json& point)
{
    return point.is_array() && point.size() == 2 && point[0].is_number() && point[1].is_number()
        && std::isfinite(point[0].get<double>()) && std::isfinite(point[1].get<double>());
}

}

int YoreholdGame::coopPort()
{
    if (const char* port = SDL_getenv("YOREHOLD_PORT"))
        return std::clamp(std::atoi(port), 1, 65535);
    return 47310;
}

bool YoreholdGame::mine(size_t creature) const
{
    if (creature < heroCount_)
        return tokens_.tokens[creature].owner == self_;
    return !client_; // the host plays the enemies
}

bool YoreholdGame::mayAct(yh::PlayerId player, size_t creature) const
{
    if (creature < heroCount_)
        return tokens_.tokens[creature].owner == player || (player == 0 && autoPlay_);
    return player == 0;
}

void YoreholdGame::selectOwnHero()
{
    std::optional<size_t> pick;
    for (size_t i = 0; i < heroCount_; i++)
        if (mine(i) && !creatures_[i].sheet.down() && (!pick || tokens_.tokens[i].selected))
            pick = i;
    for (size_t i = 0; i < heroCount_; i++)
        tokens_.tokens[i].selected = pick && i == *pick;
}

std::string YoreholdGame::seatName(size_t hero) const
{
    const int owner = hero < heroCount_ ? tokens_.tokens[hero].owner : 0;
    const auto name = playerNames_.find(owner);
    return name != playerNames_.end() ? name->second : "Player " + std::to_string(owner + 1);
}

// ---------------------------------------------------------------- commands

void YoreholdGame::act(std::string_view type, const std::string& data)
{
    if (!chapter_)
        return;
    if (client_)
    {
        if (client_->joined())
            client_->submit(type, data);
        return;
    }
    std::string reason;
    if (host_)
    {
        if (!host_->submit(type, data, &reason) && !reason.empty())
            say(reason);
        return;
    }
    if (std::optional<std::string> accepted = validate(0, type, data, reason))
        apply({0, 0, std::string(type), std::move(*accepted)});
    else if (!reason.empty())
        say(reason);
}

// Runs on the host (or alone) before a command is accepted. Anything a player sends is checked
// here; apply() can then trust it.
std::optional<std::string> YoreholdGame::validate(yh::PlayerId player, std::string_view type, std::string_view data, std::string& reason)
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
        const bool acting = fighting && current && mayAct(player, *current) && !cutscene_.running();
        const bool calm = !fighting && !cutscene_.running() && !partyDown();
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
        if (type == "fight")
        {
            const int group = j.at("group").get<int>();
            if (player != 0 || !calm || talk_ || group < 0 || group >= static_cast<int>(chapter_->encounters.size())
                || j.at("at").size() != creatures_.size())
                return std::nullopt;
            for (const nlohmann::json& p : j.at("at"))
                if (!finiteWorld(p))
                    return std::nullopt;
            return accepted;
        }
        if (type == "provoke")
        {
            // Picking a fight with an NPC. The host adds where everyone stands, as for "fight".
            const size_t npc = j.at("npc").get<size_t>();
            if (!calm || talk_ || npc >= chapter_->npcs.size() || !peaceful(npc))
                return std::nullopt;
            nlohmann::json at = nlohmann::json::array();
            for (const yh::Token& t : std::span(tokens_.tokens).first(creatures_.size()))
                at.push_back({t.position.x, t.position.y});
            return nlohmann::json{{"npc", npc}, {"at", at}}.dump();
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
            return acting && encounter_->current().budget.action ? std::optional(accepted) : std::nullopt;
        if (type == "attack")
        {
            const size_t target = j.at("target").get<size_t>();
            if (!acting || target >= creatures_.size() || !encounter_->current().budget.action || creatures_[target].sheet.down()
                || creatures_[target].team == creatures_[*current].team || !orderIndex(target) || !adjacent(*current, target))
                return std::nullopt;
            return accepted;
        }
        if (type == "end")
            return acting ? std::optional(accepted) : std::nullopt;
        if (type == "rest")
        {
            const size_t index = j.at("rest").get<size_t>();
            if (!calm || talk_ || index >= rules_.rests.size() || restsLeft(rules_.rests[index]) == 0)
                return std::nullopt;
            return accepted;
        }
        if (type == "talk")
        {
            const size_t npc = j.at("npc").get<size_t>();
            return calm && !talk_ && npc < chapter_->npcs.size() && peaceful(npc) ? std::optional(accepted) : std::nullopt;
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

void YoreholdGame::apply(const yh::NetCommand& command)
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
    else if (type == "fight" || type == "provoke")
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
            startCombat(j.at("group").get<int>());
            return;
        }
        // The NPC turns on the party and fights alone.
        const size_t npc = j.at("npc").get<size_t>();
        Creature& them = creatures_[npcToken(npc)];
        them.team = 1;
        tokens_.tokens[npcToken(npc)].owner = enemyOwner;
        say(them.sheet.name + " fights back!");
        setFlags(chapter_->npcs[npc].attacked);
        startCombat(them.group);
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
    else if (type == "rest")
        rest(rules_.rests[j.at("rest").get<size_t>()]);
    else if (type == "talk")
        startTalk(j.at("npc").get<size_t>());
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
    {
        newAdventure(j.at("seed").get<uint64_t>());
        if (!onTitle())
            menu_ = menu_ == Menu::Pause || menu_ == Menu::Settings ? menu_ : Menu::None;
    }
}

// Everything a desync would show up in: health, story, and the fight's turn and positions.
uint64_t YoreholdGame::checksum() const
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

std::string YoreholdGame::snapshot() const
{
    nlohmann::json j = nlohmann::json::parse(stateJson());
    j["seats"] = seats_;
    for (const auto& [id, name] : playerNames_)
        j["names"][std::to_string(id)] = name;
    return j.dump();
}

// ---------------------------------------------------------------- hosting and joining

void YoreholdGame::hostSession()
{
    if (!chapter_ || inSession())
        return;
    std::string error;
    std::unique_ptr<yh::TcpTransport> transport = yh::TcpTransport::listen(static_cast<uint16_t>(coopPort()), &error);
    if (!transport)
    {
        say("Couldn't host: " + error);
        return;
    }
    yh::SessionHost::Rules rules;
    rules.validate = [this](yh::PlayerId player, std::string_view type, std::string_view data, std::string& reason) {
        return validate(player, type, data, reason);
    };
    rules.apply = [this](const yh::NetCommand& command) { apply(command); };
    rules.snapshot = [this] { return snapshot(); };
    rules.checksum = [this] { return checksum(); };
    rules.admit = [this](std::string_view, std::string& reason) {
        // A joiner gets the game between fights; the fight itself isn't saved.
        if ((encounter_ && !encounter_->finished()) || talk_ || cutscene_.running())
            reason = "The host is busy (a fight, a conversation or a cutscene). Try again in a moment.";
        else if (host_->players().size() >= heroCount_)
            reason = "Every hero already has a player.";
        return reason.empty();
    };
    rules.joined = [this](yh::PlayerId player, std::string_view name) {
        playerNames_[player] = name.empty() ? "Player " + std::to_string(player + 1) : std::string(name);
        say(playerNames_[player] + " joined.");
        reseat_ = true;
    };
    rules.left = [this](yh::PlayerId player) {
        say((playerNames_.contains(player) ? playerNames_[player] : "A player") + " left.");
        playerNames_.erase(player);
        reseat_ = true;
    };
    host_ = std::make_unique<yh::SessionHost>(std::move(transport), gameName, chapter_->signature, std::move(rules));
    self_ = 0;
    tokens_.localPlayer = 0;
    playerNames_ = {{0, settings_.playerName}};
    seats_.assign(heroCount_, 0);
    menu_ = Menu::None;
    say("Hosting co-op on port " + std::to_string(coopPort()) + ". Friends pick Play > Join co-op and type this computer's address.");
}

void YoreholdGame::joinSession(const std::string& address)
{
    if (!chapter_ || inSession())
        return;
    // "host" or "host:port".
    std::string hostName = address;
    int port = coopPort();
    if (const size_t colon = address.rfind(':'); colon != std::string::npos && address.find(':') == colon)
    {
        hostName = address.substr(0, colon);
        port = std::atoi(address.c_str() + colon + 1);
    }
    std::string error;
    std::unique_ptr<yh::TcpTransport> transport = port > 0 && port < 65536 && !hostName.empty()
        ? yh::TcpTransport::connect(hostName, static_cast<uint16_t>(port), &error) : nullptr;
    if (!transport)
    {
        notice_ = "Couldn't connect to " + address + (error.empty() ? "" : ": " + error);
        noticeBad_ = true;
        return;
    }
    yh::SessionClient::Handlers handlers;
    handlers.welcomed = [this](yh::PlayerId self, std::string_view state) {
        self_ = self;
        tokens_.localPlayer = self;
        std::string problem;
        if (!restoreState(state, &problem))
        {
            sessionEnded_ = "Couldn't load the host's game: " + problem;
            return;
        }
        playerNames_.clear();
        const nlohmann::json names = nlohmann::json::parse(state).value("names", nlohmann::json::object());
        for (const auto& [id, name] : names.items())
            playerNames_[std::atoi(id.c_str())] = name.get<std::string>();
        menu_ = Menu::None;
        notice_.clear();
        say("Joined " + (playerNames_.contains(0) ? playerNames_[0] : std::string("the host")) + "'s game.");
    };
    handlers.apply = [this](const yh::NetCommand& command) {
        try
        {
            apply(command);
        }
        catch (const std::exception& e)
        {
            std::fprintf(stderr, "Co-op command %s failed: %s (%s)\n", command.type.c_str(), e.what(), command.data.c_str());
            throw;
        }
    };
    handlers.rejected = [this](uint64_t, std::string_view reason) {
        if (!reason.empty())
            say(std::string(reason));
    };
    handlers.disconnected = [this](std::string_view reason) { sessionEnded_ = reason.empty() ? "Disconnected." : std::string(reason); };
    handlers.checksum = [this] { return checksum(); };
    client_ = std::make_unique<yh::SessionClient>(std::move(transport), gameName, chapter_->signature, settings_.playerName, std::move(handlers));
}

void YoreholdGame::endSession(const std::string& reason)
{
    const bool wasJoined = client_ != nullptr;
    std::fprintf(stderr, "Co-op ended: %s\n", reason.c_str());
    if (client_ && !client_->ended())
        client_->leave();
    client_.reset();
    host_.reset();
    seats_.clear();
    playerNames_.clear();
    self_ = 0;
    tokens_.localPlayer = 0;
    netStatus_.clear();
    lastSync_.clear();
    reseat_ = false;
    for (size_t i = 0; i < heroCount_; i++)
        tokens_.tokens[i].owner = 0;
    if (wasJoined)
    {
        // The game on screen was the host's; go back to our own.
        newAdventure(SDL_GetTicks());
        if (menu_ != Menu::Join)
            menu_ = Menu::Main;
        notice_ = reason;
        noticeBad_ = true;
        return;
    }
    selectOwnHero();
    say(reason);
}

void YoreholdGame::updateSession(double deltaSeconds)
{
    if (host_)
    {
        host_->update(deltaSeconds);
        if (reseat_)
        {
            reseat_ = false;
            assignSeats();
        }
        const size_t players = host_->players().size();
        netStatus_ = "Hosting on port " + std::to_string(coopPort()) + ": " + std::to_string(players) + (players == 1 ? " player" : " players");
    }
    if (client_)
    {
        client_->update(deltaSeconds);
        if (!sessionEnded_.empty())
        {
            const std::string reason = std::move(sessionEnded_);
            sessionEnded_.clear();
            endSession(reason);
            return;
        }
        netStatus_ = client_->joined() ? "Co-op with " + (playerNames_.contains(0) ? playerNames_[0] : std::string("the host")) : "Connecting...";
    }
}

// Heroes are dealt out in turn: with two players, the host gets heroes 1 and 3.
void YoreholdGame::assignSeats()
{
    if (!host_)
        return;
    const std::vector<yh::PlayerId> players = host_->players();
    nlohmann::json owners = nlohmann::json::array(), names = nlohmann::json::object();
    for (size_t i = 0; i < heroCount_; i++)
        owners.push_back(players[i % players.size()]);
    for (const yh::PlayerId p : players)
        names[std::to_string(p)] = playerNames_.contains(p) ? playerNames_[p] : "Player " + std::to_string(p + 1);
    act("seats", nlohmann::json{{"owners", owners}, {"names", names}}.dump());
}

// While exploring, each player tells the others where their heroes are walking.
void YoreholdGame::shareWalking(double deltaSeconds)
{
    if (!inSession() || (client_ && !client_->joined()) || (encounter_ && !encounter_->finished()))
        return;
    syncTimer_ += deltaSeconds;
    if (syncTimer_ < 0.1)
        return;
    syncTimer_ = 0;
    nlohmann::json heroes = nlohmann::json::array();
    for (size_t i = 0; i < heroCount_; i++)
    {
        const yh::Token& token = tokens_.tokens[i];
        if (token.owner != self_)
            continue;
        nlohmann::json path = nlohmann::json::array();
        for (const yh::Vec2 p : token.path)
            path.push_back({p.x, p.y});
        heroes.push_back({{"t", i}, {"at", {token.position.x, token.position.y}}, {"path", path}});
    }
    std::string text = nlohmann::json{{"heroes", heroes}}.dump();
    if (heroes.empty() || text == lastSync_)
        return;
    lastSync_ = text;
    act("walk", text);
}
