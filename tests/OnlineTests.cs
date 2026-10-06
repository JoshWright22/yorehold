using System.Text;
using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

/// <summary>
/// Signing in to the account server through a stand-in for the network that answers like Nakama
/// with the yorehold-server modules: device sign-in, the healthcheck's version, who we are, the
/// config, and RPC payloads that travel as JSON strings.
/// </summary>
public class OnlineTests
{
    private sealed class FakeWeb : IWebTransport
    {
        public string Key = "test-key";
        public string Version = "0.1.3";
        public bool Up = true;
        public bool TokenExpired;
        public int SignIns;
        public JsonObject Config = new() { ["ai"] = new JsonObject() };
        public readonly List<WebRequest> Sent = new();
        private readonly Queue<Action> _waiting = new();

        public void Send(WebRequest request, Action<WebResponse> answer)
        {
            Sent.Add(request);
            WebResponse response = Answer(request);
            _waiting.Enqueue(() => answer(response));
        }

        public void Poll()
        {
            // answers that come in while handing these on wait for the next frame, like the real one
            int count = _waiting.Count;
            for (int i = 0; i < count; i++)
            {
                _waiting.Dequeue()();
            }
        }

        private static string Header(WebRequest request, string name) =>
            request.Headers.FirstOrDefault(h => h.Key == name).Value ?? "";

        private WebResponse Answer(WebRequest request)
        {
            if (!Up)
            {
                return new WebResponse { Error = "No connection could be made" };
            }
            string path = request.Url[(request.Url.IndexOf("/v2/", StringComparison.Ordinal))..];
            if (path.StartsWith("/v2/account/authenticate/device", StringComparison.Ordinal))
            {
                SignIns++;
                string basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(Key + ":"));
                if (Header(request, "Authorization") != basic)
                {
                    return new WebResponse { Status = 401, Body = """{"error": "Server key invalid", "message": "Server key invalid", "code": 16}""" };
                }
                string device = JsonNode.Parse(request.Body)!["id"]!.GetValue<string>();
                if (device.Length < 10)
                {
                    return new WebResponse { Status = 400, Body = """{"message": "Device ID invalid, must be 10-128 bytes."}""" };
                }
                TokenExpired = false;
                return new WebResponse { Status = 200, Body = $$"""{"token": "t{{SignIns}}", "created": true}""" };
            }
            if (Header(request, "Authorization") != "Bearer t" + SignIns || TokenExpired)
            {
                return new WebResponse { Status = 401, Body = """{"message": "Auth token invalid"}""" };
            }
            if (path == "/v2/account")
            {
                return new WebResponse { Status = 200, Body = """{"user": {"id": "user-1", "username": "abc"}}""" };
            }
            string rpc = path["/v2/rpc/".Length..];
            string payload = JsonNode.Parse(request.Body)!.GetValue<string>();
            JsonNode? result = rpc switch
            {
                "healthcheck" => new JsonObject { ["ok"] = true, ["version"] = Version },
                "config" => Config.DeepClone(),
                "echo" => JsonNode.Parse(payload),
                _ => null,
            };
            if (result == null)
            {
                return new WebResponse { Status = 404, Body = """{"message": "RPC function not found"}""" };
            }
            return new WebResponse { Status = 200, Body = new JsonObject { ["payload"] = result.ToJsonString() }.ToJsonString() };
        }
    }

    private static void Frames(Online online, int count = 5)
    {
        for (int i = 0; i < count; i++)
        {
            online.Update();
        }
    }

    [Fact]
    public void NoServerIsOffline()
    {
        var web = new FakeWeb();
        var online = new Online(web);
        online.Connect("", "test-key", "device-0123456789");
        Assert.Equal(OnlineState.Off, online.State);
        Assert.Equal("", online.Status);
        Assert.Equal("", online.Account);
        Assert.Empty(web.Sent);
        JsonNode? answer = new JsonObject();
        online.Call("echo", new JsonObject(), a => answer = a);
        Assert.Null(answer);
    }

    [Fact]
    public void SignsInWithTheDevice()
    {
        var web = new FakeWeb();
        var online = new Online(web);
        online.Connect("http://127.0.0.1:7350/", "test-key", "device-0123456789");
        Assert.Equal(OnlineState.Connecting, online.State);
        Assert.Equal("Connecting to the server...", online.Status);
        WebRequest first = web.Sent[0];
        Assert.Equal("http://127.0.0.1:7350/v2/account/authenticate/device?create=true", first.Url);
        Assert.Equal("POST", first.Method);
        Assert.Equal("device-0123456789", JsonNode.Parse(first.Body)!["id"]!.GetValue<string>());

        Frames(online);
        Assert.Equal(OnlineState.SignedIn, online.State);
        Assert.Equal("Online (server 0.1.3)", online.Status);
        Assert.Equal("user-1", online.UserId);
        Assert.Equal("user-1", online.Account);
        Assert.Equal(1, online.ConfigVersion);
        Assert.NotNull(online.Config["ai"]);
    }

    [Fact]
    public void PayloadsTravelAsStrings()
    {
        var web = new FakeWeb();
        var online = new Online(web);
        online.Connect("http://server:7350", "test-key", "device-0123456789");
        Frames(online);
        JsonNode? answer = null;
        online.Call("echo", new JsonObject { ["id"] = "ser-ada", ["n"] = 3 }, a => answer = a);
        WebRequest sent = web.Sent[^1];
        Assert.Equal("http://server:7350/v2/rpc/echo", sent.Url);
        Assert.Equal("""{"id":"ser-ada","n":3}""", JsonNode.Parse(sent.Body)!.GetValue<string>());
        Frames(online);
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["id"] = "ser-ada", ["n"] = 3 }, answer));

        bool called = false;
        online.Call("nothing", new JsonObject(), a =>
        {
            called = true;
            answer = a;
        });
        Frames(online);
        Assert.True(called);
        Assert.Null(answer);
    }

    [Fact]
    public void SaysWhyItIsOffline()
    {
        var web = new FakeWeb();
        var online = new Online(web);
        online.Connect("http://server:7350", "wrong", "device-0123456789");
        Frames(online);
        Assert.Equal(OnlineState.Failed, online.State);
        Assert.Equal("Offline: the server didn't accept this game's key", online.Status);
        Assert.Equal("", online.Account);

        online.Connect("http://server:7350", "test-key", "short");
        Frames(online);
        Assert.Equal("Offline: Device ID invalid, must be 10-128 bytes.", online.Status);

        web.Up = false;
        online.Connect("http://server:7350", "test-key", "device-0123456789");
        Frames(online);
        Assert.Equal("Offline: No connection could be made", online.Status);

        web.Up = true;
        web.Version = "0.2.0";
        online.Connect("http://server:7350", "test-key", "device-0123456789");
        Frames(online);
        Assert.Equal("Offline: this build can't talk to server 0.2.0", online.Status);
        Assert.Equal("", online.Account);
    }

    [Fact]
    public void AnExpiredSignInIsRenewed()
    {
        var web = new FakeWeb();
        var online = new Online(web);
        online.Connect("http://server:7350", "test-key", "device-0123456789");
        Frames(online);
        web.TokenExpired = true;
        JsonNode? answer = new JsonObject();
        online.Call("echo", new JsonObject { ["a"] = 1 }, a => answer = a);
        Frames(online);
        Assert.Null(answer);
        Assert.Equal(2, web.SignIns);
        Assert.Equal(OnlineState.SignedIn, online.State);
        online.Call("echo", new JsonObject { ["a"] = 1 }, a => answer = a);
        Frames(online);
        Assert.Equal(1, answer!["a"]!.GetValue<int>());
    }

    [Fact]
    public void ConfigChangesAreCounted()
    {
        var web = new FakeWeb();
        var online = new Online(web);
        online.Connect("http://server:7350", "test-key", "device-0123456789");
        Frames(online);
        Assert.Equal(1, online.ConfigVersion);
        online.RefreshConfig();
        Frames(online);
        Assert.Equal(1, online.ConfigVersion);
        web.Config = new JsonObject { ["ai"] = new JsonObject { ["creatures"] = new JsonObject { ["goblin"] = "mindless" } } };
        online.RefreshConfig();
        Frames(online);
        Assert.Equal(2, online.ConfigVersion);
        web.Up = false;
        online.RefreshConfig();
        Frames(online);
        Assert.Equal(2, online.ConfigVersion);
        Assert.Equal("mindless", online.Config["ai"]!["creatures"]!["goblin"]!.GetValue<string>());
    }

    [Fact]
    public void ServerAiIsTheLastLayer()
    {
        using WorldFixture world = WorldFixture.Load("chapters/chapter-one");
        int goblin = Enumerable.Range(0, world.World.Creatures.Count).First(i => world.World.Creatures[i].CreatureId == "goblin");
        double before = world.World.AiFor(goblin).FleeHp;

        // "coward" builds on "timid", listed after it; a broken profile and a number are skipped
        world.World.ApplyServerAi(JsonNode.Parse("""
            {"profiles": {"coward": {"base": "timid", "random": 0.25}, "timid": {"base": "animal", "fleeHp": 0.9}, "broken": {"base": "nobody"}},
             "creatures": {"goblin": "coward", "rat": 3}}
            """));
        AiProfile now = world.World.AiFor(goblin);
        Assert.Equal("coward", now.Base);
        Assert.Equal(0.9, now.FleeHp);
        Assert.Equal(0.25, now.Random);

        world.World.ApplyServerAi(JsonNode.Parse("""{"creatures": {"goblin": {"fleeHp": 0.5}}}"""));
        Assert.Equal(0.5, world.World.AiFor(goblin).FleeHp);
        world.World.ApplyServerAi(null);
        Assert.Equal(before, world.World.AiFor(goblin).FleeHp);
    }

    [Fact]
    public void ServerSettingsUseTheCppNames()
    {
        string longest = new('d', 200);
        GameSettings settings = GameSettings.Parse($$"""{"server": "http://127.0.0.1:7350", "serverKey": "k", "deviceId": "{{longest}}"}""");
        Assert.Equal("http://127.0.0.1:7350", settings.Server);
        Assert.Equal("k", settings.ServerKey);
        Assert.Equal(128, settings.DeviceId.Length);
        JsonObject written = JsonNode.Parse(settings.ToJson())!.AsObject();
        Assert.Equal("http://127.0.0.1:7350", written["server"]!.GetValue<string>());
        Assert.Equal("k", written["serverKey"]!.GetValue<string>());
        Assert.Equal(128, written["deviceId"]!.GetValue<string>().Length);
        Assert.Equal("", new GameSettings().Server);
    }

    [Fact]
    public void SyncRunsOnceSignedIn()
    {
        using var scratch = new Scratch();
        scratch.Write("characters/ser-ada.json", """{"format": "yorehold.character", "version": 1, "data": {}}""");
        var web = new FakeWeb();
        var online = new Online(web);
        var sync = new AccountSync(online);
        sync.SetFolders(new SyncFolders(Path.Combine(scratch.Folder, "saves"), Path.Combine(scratch.Folder, "characters"), scratch.Folder));
        sync.Update(0.01);
        Assert.Equal(0, sync.Passes);
        online.Connect("http://server:7350", "test-key", "device-0123456789");
        for (int i = 0; i < 10; i++)
        {
            online.Update();
            sync.Update(0.01);
        }
        // the fake has no characters_list, so the pass ends at once with nothing changed
        Assert.Equal(1, sync.Passes);
        Assert.Equal(0, sync.Last.Uploaded);
        Assert.True(File.Exists(Path.Combine(scratch.Folder, "characters", "ser-ada.json")));
    }
}
