// Co-op: hosting and joining. The commands every copy applies are the World's (sim/WorldNet.cpp);
// this file carries them between machines.

#include "YoreholdGame.h"

#include <yorehold/framework/net/Transport.h>

#include <nlohmann/json.hpp>

#include <SDL3/SDL_stdinc.h>
#include <SDL3/SDL_timer.h>

#include <algorithm>
#include <cstdio>
#include <cstdlib>

namespace
{

constexpr const char* gameName = "yorehold";

}

int YoreholdGame::coopPort()
{
    if (const char* port = SDL_getenv("YOREHOLD_PORT"))
        return std::clamp(std::atoi(port), 1, 65535);
    return 47310;
}

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
    if (host_)
    {
        std::string reason;
        if (!host_->submit(type, data, &reason) && !reason.empty())
            say(reason);
        return;
    }
    World::act(type, data);
}

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
        if ((encounter_ && !encounter_->finished()) || talk_ || inCutscene_)
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
    remote_ = true;
}

void YoreholdGame::endSession(const std::string& reason)
{
    const bool wasJoined = client_ != nullptr;
    std::fprintf(stderr, "Co-op ended: %s\n", reason.c_str());
    if (client_ && !client_->ended())
        client_->leave();
    client_.reset();
    host_.reset();
    remote_ = false;
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
