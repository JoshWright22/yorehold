#pragma once

#include <nlohmann/json.hpp>

#include <functional>
#include <optional>
#include <string>
#include <string_view>

// The part of the account server that sync needs: who is signed in, and a way to call the
// server's functions. Online is the real one; unit checks put a stand-in behind it.
class AccountServer
{
public:
    using Answer = std::function<void(std::optional<nlohmann::json>)>;

    virtual ~AccountServer() = default;
    // The signed-in account's id, "" while offline.
    virtual std::string account() const = 0;
    // Calls one of the server's functions as the signed-in player. Nothing = it failed. The answer
    // may arrive inside the call or on a later frame.
    virtual void call(std::string_view rpc, const nlohmann::json& payload, Answer answer) = 0;
};
