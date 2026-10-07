using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>One question to a story model: what it is told to be, what it is asked, and the JSON schema its answer must fit.</summary>
public sealed record StoryRequest(string System, string Prompt, string SchemaName, string Schema);

/// <summary>What came back: the answer's text (JSON when a schema was asked for) and what it cost in tokens.</summary>
public sealed record StoryAnswer(string Text, int InputTokens, int OutputTokens);

/// <summary>
/// A language model that turns a piece of a book into outline entries. The game never talks to
/// one directly in play; story import asks it, and tests use a stand-in.
/// </summary>
public interface IStoryModel
{
    Task<StoryAnswer> Ask(StoryRequest request, CancellationToken cancel = default);
}

/// <summary>
/// A model behind the chat request OpenRouter, Ollama and most other services take: POST
/// address/v1/chat/completions with the schema as response_format. So which model it is, and
/// where, is an address and a name in settings.
/// </summary>
public sealed class ChatModel : IStoryModel
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };

    public ChatModel(string address, string model, string key = "")
    {
        Address = address.TrimEnd('/');
        Model = model;
        Key = key;
    }

    public string Address { get; }
    public string Model { get; }
    public string Key { get; }

    /// <summary>The request body, apart from sending it, so tests can see what goes out.</summary>
    public JsonObject Body(StoryRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.System },
                new JsonObject { ["role"] = "user", ["content"] = request.Prompt }),
            ["temperature"] = 0,
        };
        if (request.Schema.Length > 0)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = request.SchemaName, ["schema"] = JsonNode.Parse(request.Schema) },
            };
        }
        return body;
    }

    public async Task<StoryAnswer> Ask(StoryRequest request, CancellationToken cancel = default)
    {
        string url = Address.EndsWith("/v1", StringComparison.Ordinal) ? Address + "/chat/completions" : Address + "/v1/chat/completions";
        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(CreateJson.Compact(Body(request)), Encoding.UTF8, "application/json"),
        };
        if (Key.Length > 0)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        }
        using HttpResponseMessage response = await Http.SendAsync(message, cancel);
        string text = await response.Content.ReadAsStringAsync(cancel);
        if (!response.IsSuccessStatusCode)
        {
            throw new IOException($"the story model at {Address} said {(int)response.StatusCode}: {Short(text)}");
        }
        return Read(text);
    }

    /// <summary>The answer in a chat completion's reply.</summary>
    public static StoryAnswer Read(string reply)
    {
        try
        {
            JsonNode root = JsonNode.Parse(reply) ?? throw new JsonException("empty");
            string content = root["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? throw new JsonException("no choices[0].message.content");
            int input = root["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0;
            int output = root["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0;
            return new StoryAnswer(content, input, output);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            throw new IOException($"the story model's reply is not a chat completion ({error.Message}): {Short(reply)}");
        }
    }

    private static string Short(string text) => text.Length <= 300 ? text : text[..300] + "...";
}
