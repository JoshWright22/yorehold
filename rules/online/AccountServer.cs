using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// The part of the account server that sync needs: who is signed in, and a way to call the
/// server's functions. Online is the real one; tests put a stand-in behind it.
/// </summary>
public interface IAccountServer
{
    /// <summary>The signed-in account's id, "" while offline.</summary>
    string Account { get; }

    /// <summary>
    /// Calls one of the server's functions as the signed-in player. The answer is null when the
    /// call failed. It may arrive inside the call or on a later frame.
    /// </summary>
    void Call(string rpc, JsonNode? payload, Action<JsonNode?> answer);
}
