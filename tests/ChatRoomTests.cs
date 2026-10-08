using System.Text.Json.Nodes;
using Xunit;
using Yorehold.Rules;

namespace Yorehold.Rules.Tests;

/// <summary>A chat room against a stand-in server: reading what is new, unread counts, sending, and no sign-in.</summary>
public class ChatRoomTests
{
    private sealed class Server : IAccountServer
    {
        public string Account { get; set; } = "me";
        public List<(string Rpc, JsonNode? Payload)> Calls { get; } = new();
        public JsonNode? Next { get; set; }

        public void Call(string rpc, JsonNode? payload, Action<JsonNode?> answer)
        {
            Calls.Add((rpc, payload?.DeepClone()));
            answer(rpc == "chat_send" ? new JsonObject { ["id"] = 9 } : Next);
            Next = new JsonObject { ["messages"] = new JsonArray() };
        }
    }

    private static JsonObject Messages(params (long Id, string Name, bool Mine)[] lines) => new()
    {
        ["messages"] = new JsonArray(lines.Select(l => (JsonNode?)new JsonObject
        {
            ["id"] = l.Id, ["name"] = l.Name, ["text"] = "hello " + l.Id, ["at"] = 1790000000000L, ["mine"] = l.Mine,
        }).ToArray()),
    };

    [Fact]
    public void NewLinesAreReadAndCountedUntilSeen()
    {
        var server = new Server { Next = Messages((1, "Ana", false), (2, "Bo", false)) };
        var room = new ChatRoom("global", "Global", server);
        room.Update(0, watching: false);
        Assert.Equal(new[] { "hello 1", "hello 2" }, room.Lines.Select(l => l.Text));
        Assert.Equal(2, room.Unread);
        Assert.Equal(0L, server.Calls[0].Payload!["after"]!.GetValue<long>());

        // not yet time to ask again; then it asks for what came after the last one
        room.Update(1, watching: false);
        Assert.Single(server.Calls);
        server.Next = Messages((3, "me", true));
        room.Update(ChatRoom.IdlePollSeconds, watching: true);
        Assert.Equal(2L, server.Calls[1].Payload!["after"]!.GetValue<long>());
        Assert.Equal(2, room.Unread); // one's own line is never unread
        room.Seen();
        Assert.Equal(0, room.Unread);
    }

    [Fact]
    public void SendingNeedsASignInAndWords()
    {
        var server = new Server { Account = "" };
        var room = new ChatRoom("party.x", "Party", server);
        Assert.False(room.Send("hi", out string why));
        Assert.Contains("Sign in", why);
        room.Update(10, true);
        Assert.Empty(server.Calls);

        server.Account = "me";
        Assert.False(room.Send("   ", out _));
        Assert.False(room.Send(new string('a', ChatRoom.Longest + 1), out why));
        Assert.True(room.Send("  on my way ", out _));
        Assert.Equal(("chat_send", "on my way", "party.x"), (server.Calls[0].Rpc, server.Calls[0].Payload!["text"]!.ToString(), server.Calls[0].Payload!["room"]!.ToString()));
    }
}
