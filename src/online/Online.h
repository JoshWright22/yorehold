#pragma once

#include "AccountServer.h"

#include <yorehold/framework/net/Http.h>

#include <nlohmann/json.hpp>

#include <functional>
#include <optional>
#include <string>
#include <string_view>
#include <utility>

// The account server (yorehold-server): signs this install in and checks the server is one this
// build can talk to. Playing never waits on it; without a server the game is simply offline.
class Online : public AccountServer
{
public:
    // The server's major.minor must match; patch versions are free to differ.
    static constexpr std::string_view protocol = "0.1";

    enum class State { Off, Connecting, SignedIn, Failed };

    // `server` like "http://127.0.0.1:7350". `device` names this install (10 to 128 characters);
    // the first sign-in with it creates the account.
    void connect(std::string server, std::string serverKey, std::string device);
    // Call once per frame.
    void update() { http_.poll(); }

    State state() const { return state_; }
    // One line for the title screen ("" while off).
    const std::string& status() const { return status_; }
    const std::string& userId() const { return userId_; }

    // Settings the server hands every player ({"ai": {...}}), so behaviour can be tuned without
    // an update. Empty until signed in; configVersion() goes up each time it changes.
    const nlohmann::json& config() const { return config_; }
    int configVersion() const { return configVersion_; }
    void refreshConfig();

    using Answer = AccountServer::Answer;
    // Calls one of the server's functions as the signed-in player. Nothing = it failed.
    void rpc(std::string_view id, const nlohmann::json& payload, Answer answer);

    // AccountServer. The account is only known once the server has said who we are.
    std::string account() const override { return state_ == State::SignedIn ? userId_ : std::string(); }
    void call(std::string_view id, const nlohmann::json& payload, Answer answer) override { rpc(id, payload, std::move(answer)); }

private:
    void signIn();
    void fail(std::string why);

    yh::HttpClient http_;
    State state_ = State::Off;
    std::string status_;
    std::string server_, serverKey_, device_;
    std::string token_, userId_;
    nlohmann::json config_ = nlohmann::json::object();
    int configVersion_ = 0;
};
