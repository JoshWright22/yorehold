#include "Online.h"

namespace
{

constexpr int timeoutMs = 5000;

nlohmann::json parse(const std::string& text)
{
    return nlohmann::json::parse(text, nullptr, false);
}

std::string why(const yh::HttpResponse& response)
{
    if (response.status == 0)
        return response.error;
    const nlohmann::json body = parse(response.body);
    if (body.is_object() && body.contains("message") && body["message"].is_string())
        return body["message"].get<std::string>();
    return "the server answered " + std::to_string(response.status);
}

}

void Online::connect(std::string server, std::string serverKey, std::string device)
{
    while (server.ends_with('/'))
        server.pop_back();
    server_ = std::move(server);
    serverKey_ = std::move(serverKey);
    device_ = std::move(device);
    token_.clear();
    userId_.clear();
    if (server_.empty())
    {
        state_ = State::Off;
        status_.clear();
        return;
    }
    signIn();
}

void Online::fail(std::string reason)
{
    state_ = State::Failed;
    token_.clear();
    status_ = "Offline: " + std::move(reason);
}

void Online::signIn()
{
    state_ = State::Connecting;
    status_ = "Connecting to the server...";
    yh::HttpRequest request;
    request.method = "POST";
    request.url = server_ + "/v2/account/authenticate/device?create=true";
    request.headers = {{"Authorization", "Basic " + yh::base64(serverKey_ + ":")}, {"Content-Type", "application/json"}};
    request.body = nlohmann::json{{"id", device_}}.dump();
    request.timeoutMs = timeoutMs;
    http_.send(std::move(request), [this](const yh::HttpResponse& response) {
        const nlohmann::json body = parse(response.body);
        if (!response.ok() || !body.is_object() || !body.contains("token") || !body["token"].is_string())
        {
            fail(response.status == 401 ? "the server didn't accept this game's key" : why(response));
            return;
        }
        token_ = body["token"].get<std::string>();

        // Which server is this, and who are we on it?
        rpc("healthcheck", "", [this](std::optional<nlohmann::json> health) {
            const std::string version = health && health->is_object() ? health->value("version", std::string()) : std::string();
            if (version.empty())
                fail("the server isn't a Yorehold server");
            else if (!version.starts_with(std::string(protocol) + "."))
                fail("this build can't talk to server " + version);
            else
            {
                state_ = State::SignedIn;
                status_ = "Online (server " + version + ")";
                refreshConfig();
            }
        });
        yh::HttpRequest account;
        account.url = server_ + "/v2/account";
        account.headers = {{"Authorization", "Bearer " + token_}};
        account.timeoutMs = timeoutMs;
        http_.send(std::move(account), [this](const yh::HttpResponse& answer) {
            const nlohmann::json me = parse(answer.body);
            if (answer.ok() && me.is_object() && me.contains("user") && me["user"].is_object())
                userId_ = me["user"].value("id", std::string());
        });
    });
}

void Online::refreshConfig()
{
    rpc("config", "", [this](std::optional<nlohmann::json> config) {
        // A failed fetch keeps the settings already in use.
        if (!config || !config->is_object() || *config == config_)
            return;
        config_ = std::move(*config);
        configVersion_++;
    });
}

void Online::rpc(std::string_view id, const nlohmann::json& payload, Answer answer)
{
    if (token_.empty())
    {
        if (answer)
            answer(std::nullopt);
        return;
    }
    yh::HttpRequest request;
    request.method = "POST";
    request.url = server_ + "/v2/rpc/" + std::string(id);
    request.headers = {{"Authorization", "Bearer " + token_}, {"Content-Type", "application/json"}};
    // The payload travels as a JSON string, and so does the answer.
    request.body = nlohmann::json(payload.is_string() ? payload.get<std::string>() : payload.dump()).dump();
    request.timeoutMs = timeoutMs;
    http_.send(std::move(request), [this, answer = std::move(answer)](const yh::HttpResponse& response) {
        // The sign-in ran out (they last two hours): get a new one for the next call.
        if (response.status == 401 && state_ == State::SignedIn)
            signIn();
        const nlohmann::json body = parse(response.body);
        std::optional<nlohmann::json> result;
        if (response.ok() && body.is_object() && body.contains("payload") && body["payload"].is_string())
        {
            nlohmann::json value = parse(body["payload"].get<std::string>());
            if (!value.is_discarded())
                result = std::move(value);
        }
        if (answer)
            answer(std::move(result));
    });
}
