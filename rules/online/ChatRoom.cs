using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>One line in a chat room: a player's message, or a line of the game's own ("Bo joined").</summary>
public sealed record ChatLine(long Id, string Name, string Text, DateTime At, bool Mine, bool System);

/// <summary>
/// One chat room (the server's "global", or a party's): the lines so far, what is unread, and
/// sending. It asks the server for what is new every PollSeconds while it is being watched and less
/// often when not, so the unread count still moves. Without a sign-in it says so instead.
/// </summary>
public sealed class ChatRoom
{
    public const double PollSeconds = 3;
    public const double IdlePollSeconds = 15;
    public const int Keep = 200;
    public const int Longest = 300;

    private readonly IAccountServer _server;
    private double _wait;
    private bool _asking;
    private long _last;

    public ChatRoom(string room, string title, IAccountServer server)
    {
        Room = room;
        Title = title;
        _server = server;
    }

    private bool SignedIn => _server.Account.Length > 0;

    /// <summary>The server's name for it: "global", or "party.&lt;id&gt;".</summary>
    public string Room { get; }
    /// <summary>What its tab says: "Global", "Party".</summary>
    public string Title { get; }
    public List<ChatLine> Lines { get; } = new();
    /// <summary>Lines that came in since the room was last looked at.</summary>
    public int Unread { get; private set; }
    /// <summary>Why nothing can be sent ("Sign in to chat"), "" when it can.</summary>
    public string Closed => SignedIn ? "" : "Sign in to chat: Options, Account.";
    /// <summary>Goes up when lines change, for a view to know when to redraw.</summary>
    public int Changes { get; private set; }

    /// <summary>The room is in front of the player: what is there is read.</summary>
    public void Seen()
    {
        if (Unread > 0)
        {
            Unread = 0;
            Changes++;
        }
    }

    /// <summary>Time passing: asks for new lines when it is time. watching = the room is open in front of the player.</summary>
    public void Update(double seconds, bool watching)
    {
        _wait -= seconds;
        if (_wait > 0 || _asking || !SignedIn)
        {
            return;
        }
        _wait = watching ? PollSeconds : IdlePollSeconds;
        _asking = true;
        _server.Call("chat_read", new JsonObject { ["room"] = Room, ["after"] = _last }, answer =>
        {
            _asking = false;
            foreach (JsonNode? m in answer?["messages"] as JsonArray ?? new JsonArray())
            {
                long id = m?["id"]?.GetValue<long>() ?? 0;
                if (id <= _last)
                {
                    continue;
                }
                _last = id;
                bool mine = m!["mine"]?.GetValue<bool>() == true;
                Add(new ChatLine(id, m["name"]?.ToString() ?? "", m["text"]?.ToString() ?? "",
                    DateTimeOffset.FromUnixTimeMilliseconds(m["at"]?.GetValue<long>() ?? 0).LocalDateTime, mine, false), counts: !mine && !watching);
            }
        });
    }

    /// <summary>Sends a message. False with why when it can't go: not signed in, empty, too long.</summary>
    public bool Send(string text, out string why)
    {
        text = text.Trim();
        why = Closed;
        if (why.Length > 0 || text.Length == 0)
        {
            return false;
        }
        if (text.Length > Longest)
        {
            why = $"A message is {Longest} letters at most.";
            return false;
        }
        _server.Call("chat_send", new JsonObject { ["room"] = Room, ["text"] = text }, answer =>
        {
            if (answer == null)
            {
                Say("That message didn't go: the server didn't take it.");
            }
            _wait = 0; // the sent line comes back on the next read, with its place among the others
        });
        return true;
    }

    /// <summary>A line of the game's own in this room ("Connected", "Bo joined the party").</summary>
    public void Say(string text) => Add(new ChatLine(0, "", text, DateTime.Now, false, true), counts: false);

    private void Add(ChatLine line, bool counts)
    {
        Lines.Add(line);
        if (Lines.Count > Keep)
        {
            Lines.RemoveRange(0, Lines.Count - Keep);
        }
        if (counts)
        {
            Unread++;
        }
        Changes++;
    }
}
