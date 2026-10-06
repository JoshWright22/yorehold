using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

public enum OnlineState
{
    Off,
    Connecting,
    SignedIn,
    Failed,
}

/// <summary>
/// The account server (yorehold-server, on Nakama): signs this install in and checks the server
/// is one this build can talk to. Playing never waits on it; without a server the game is simply
/// offline. The C++ client's Online, call for call.
/// </summary>
public sealed class Online : IAccountServer
{
    /// <summary>The server's major.minor must match; patch versions are free to differ.</summary>
    public const string Protocol = "0.1";
    public const int TimeoutMs = 5000;

    private readonly IWebTransport _web;
    private string _server = "";
    private string _serverKey = "";
    private string _device = "";
    private string _token = "";
    // answers to a sign-in that was replaced by a later connect are dropped
    private int _attempt;

    public Online(IWebTransport web)
    {
        _web = web;
    }

    public OnlineState State { get; private set; } = OnlineState.Off;
    /// <summary>One line for the title screen ("" while off).</summary>
    public string Status { get; private set; } = "";
    public string UserId { get; private set; } = "";
    /// <summary>The address connected to, "" while off.</summary>
    public string Server => _server;

    /// <summary>
    /// Settings the server hands every player ({"ai": {...}}), so behaviour can be tuned without
    /// an update. Empty until signed in; ConfigVersion goes up each time it changes.
    /// </summary>
    public JsonObject Config { get; private set; } = new();
    public int ConfigVersion { get; private set; }

    /// <summary>The account is only known once the server has said who we are.</summary>
    public string Account => State == OnlineState.SignedIn ? UserId : "";

    /// <summary>
    /// `server` like "http://127.0.0.1:7350". `device` names this install (10 to 128 characters);
    /// the first sign-in with it makes the account. An empty server goes offline.
    /// </summary>
    public void Connect(string server, string serverKey, string device)
    {
        _attempt++;
        _server = server.TrimEnd('/');
        _serverKey = serverKey;
        _device = device;
        _token = "";
        UserId = "";
        if (_server.Length == 0)
        {
            State = OnlineState.Off;
            Status = "";
            return;
        }
        SignIn();
    }

    /// <summary>Call once per frame: answers that came in are handed on here.</summary>
    public void Update() => _web.Poll();

    public void RefreshConfig()
    {
        Rpc("config", null, config =>
        {
            // a failed fetch keeps the settings already in use
            if (config is not JsonObject fresh || JsonNode.DeepEquals(fresh, Config))
            {
                return;
            }
            Config = fresh;
            ConfigVersion++;
        });
    }

    public void Call(string rpc, JsonNode? payload, Action<JsonNode?> answer) => Rpc(rpc, payload, answer);

    /// <summary>Calls one of the server's functions as the signed-in player. Null = it failed.</summary>
    public void Rpc(string id, JsonNode? payload, Action<JsonNode?>? answer)
    {
        if (_token.Length == 0)
        {
            answer?.Invoke(null);
            return;
        }
        int attempt = _attempt;
        // The payload travels as a JSON string, and so does the answer.
        string inner = payload == null ? "" : payload is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : payload.ToJsonString();
        var request = new WebRequest
        {
            Method = "POST",
            Url = _server + "/v2/rpc/" + id,
            Headers = new[] { Header("Authorization", "Bearer " + _token), Header("Content-Type", "application/json") },
            Body = JsonValue.Create(inner).ToJsonString(),
            TimeoutMs = TimeoutMs,
        };
        _web.Send(request, response =>
        {
            if (attempt != _attempt)
            {
                answer?.Invoke(null);
                return;
            }
            // The sign-in ran out (they last two hours): get a new one for the next call.
            if (response.Status == 401 && State == OnlineState.SignedIn)
            {
                SignIn();
            }
            JsonNode? result = null;
            if (response.Ok && Parse(response.Body) is JsonObject body && body["payload"] is JsonValue text && text.GetValueKind() == JsonValueKind.String)
            {
                result = Parse(text.GetValue<string>());
            }
            answer?.Invoke(result);
        });
    }

    private void SignIn()
    {
        int attempt = _attempt;
        State = OnlineState.Connecting;
        Status = "Connecting to the server...";
        var request = new WebRequest
        {
            Method = "POST",
            Url = _server + "/v2/account/authenticate/device?create=true",
            Headers = new[]
            {
                Header("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(_serverKey + ":"))),
                Header("Content-Type", "application/json"),
            },
            Body = new JsonObject { ["id"] = _device }.ToJsonString(),
            TimeoutMs = TimeoutMs,
        };
        _web.Send(request, response =>
        {
            if (attempt != _attempt)
            {
                return;
            }
            if (!response.Ok || Parse(response.Body) is not JsonObject body || body["token"] is not JsonValue token || token.GetValueKind() != JsonValueKind.String)
            {
                Fail(response.Status == 401 ? "the server didn't accept this game's key" : Why(response));
                return;
            }
            _token = token.GetValue<string>();

            // Which server is this, and who are we on it?
            Rpc("healthcheck", null, health =>
            {
                string version = health is JsonObject h && h["version"] is JsonValue ver && ver.GetValueKind() == JsonValueKind.String ? ver.GetValue<string>() : "";
                if (attempt != _attempt)
                {
                    return;
                }
                if (version.Length == 0)
                {
                    Fail("the server isn't a Yorehold server");
                }
                else if (!version.StartsWith(Protocol + ".", StringComparison.Ordinal))
                {
                    Fail("this build can't talk to server " + version);
                }
                else
                {
                    State = OnlineState.SignedIn;
                    Status = $"Online (server {version})";
                    RefreshConfig();
                }
            });
            var account = new WebRequest
            {
                Url = _server + "/v2/account",
                Headers = new[] { Header("Authorization", "Bearer " + _token) },
                TimeoutMs = TimeoutMs,
            };
            _web.Send(account, answer =>
            {
                if (attempt == _attempt && answer.Ok && Parse(answer.Body) is JsonObject me && me["user"] is JsonObject user
                    && user["id"] is JsonValue userId && userId.GetValueKind() == JsonValueKind.String)
                {
                    UserId = userId.GetValue<string>();
                }
            });
        });
    }

    private void Fail(string reason)
    {
        State = OnlineState.Failed;
        _token = "";
        Status = "Offline: " + reason;
    }

    private static string Why(WebResponse response)
    {
        if (response.Status == 0)
        {
            return response.Error.Length > 0 ? response.Error : "couldn't reach the server";
        }
        if (Parse(response.Body) is JsonObject body && body["message"] is JsonValue message && message.GetValueKind() == JsonValueKind.String)
        {
            return message.GetValue<string>();
        }
        return $"the server answered {response.Status}";
    }

    internal static JsonNode? Parse(string text)
    {
        try
        {
            return text.Length == 0 ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static KeyValuePair<string, string> Header(string name, string value) => new(name, value);
}
