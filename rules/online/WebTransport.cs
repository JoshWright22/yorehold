using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;

namespace Yorehold.Rules;

public sealed record WebRequest
{
    public string Method { get; init; } = "GET";
    public string Url { get; init; } = "";
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = Array.Empty<KeyValuePair<string, string>>();
    public string Body { get; init; } = "";
    public int TimeoutMs { get; init; } = 5000;
}

public sealed record WebResponse
{
    /// <summary>The HTTP status, 0 when nothing came back (no server, a timeout).</summary>
    public int Status { get; init; }
    public string Body { get; init; } = "";
    /// <summary>Why nothing came back, when Status is 0.</summary>
    public string Error { get; init; } = "";
    public bool Ok => Status is >= 200 and < 300;
}

/// <summary>
/// Sends requests and hands the answers back on the caller's thread when it calls Poll, so the
/// game never waits on the network and nothing it holds is touched from another thread.
/// </summary>
public interface IWebTransport
{
    void Send(WebRequest request, Action<WebResponse> answer);
    void Poll();
}

/// <summary>The real transport, on .NET's HttpClient.</summary>
public sealed class HttpTransport : IWebTransport
{
    private static readonly HttpClient Client = new();
    private readonly ConcurrentQueue<Action> _done = new();

    public void Send(WebRequest request, Action<WebResponse> answer)
    {
        _ = Run(request, answer);
    }

    public void Poll()
    {
        while (_done.TryDequeue(out Action? next))
        {
            next();
        }
    }

    private async Task Run(WebRequest request, Action<WebResponse> answer)
    {
        WebResponse response;
        try
        {
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
            string type = "";
            foreach (KeyValuePair<string, string> header in request.Headers)
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    type = header.Value;
                }
                else
                {
                    message.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            if (request.Method != "GET")
            {
                message.Content = new StringContent(request.Body, Encoding.UTF8);
                if (type.Length > 0)
                {
                    message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
                }
            }
            using var timeout = new CancellationTokenSource(request.TimeoutMs);
            using HttpResponseMessage reply = await Client.SendAsync(message, timeout.Token).ConfigureAwait(false);
            string body = await reply.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            response = new WebResponse { Status = (int)reply.StatusCode, Body = body };
        }
        catch (OperationCanceledException)
        {
            response = new WebResponse { Error = "the server didn't answer in time" };
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or UriFormatException)
        {
            response = new WebResponse { Error = Plain(error) };
        }
        _done.Enqueue(() => answer(response));
    }

    // The title shows this, so the common cases get a short line instead of the system's sentence.
    private static string Plain(Exception error)
    {
        if (error.InnerException is System.Net.Sockets.SocketException socket)
        {
            switch (socket.SocketErrorCode)
            {
                case System.Net.Sockets.SocketError.ConnectionRefused:
                    return "nothing answers at that address";
                case System.Net.Sockets.SocketError.HostNotFound or System.Net.Sockets.SocketError.NoData:
                    return "the server's name isn't known";
                case System.Net.Sockets.SocketError.NetworkUnreachable or System.Net.Sockets.SocketError.HostUnreachable:
                    return "no network";
            }
        }
        return error.Message.Length > 0 ? error.Message : "couldn't reach the server";
    }
}
