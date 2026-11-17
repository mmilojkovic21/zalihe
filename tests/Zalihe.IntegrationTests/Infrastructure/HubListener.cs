using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Shouldly;

namespace Zalihe.IntegrationTests.Infrastructure;

/// <summary>
/// A browser tab listening on the SignalR changes hub: SignalR's negotiation, then a plain WebSocket with its JSON
/// protocol (so the tests need no SignalR client package).
/// </summary>
public sealed class HubListener : IAsyncDisposable
{
    private const char RecordSeparator = '\u001e';
    private readonly WebSocket _socket;
    private readonly Queue<string> _records = new();
    private readonly StringBuilder _partial = new();

    private HubListener(WebSocket socket, string connectionId)
    {
        _socket = socket;
        ConnectionId = connectionId;
    }

    /// <summary>The id the web app sends in <c>X-Connection-Id</c> with its own changes.</summary>
    public string ConnectionId { get; }

    /// <summary>
    /// Connects with <paramref name="cookies"/> (the browser's sign-in) like the SignalR client does: negotiates
    /// (which gives the connection its id), opens the WebSocket and completes the handshake.
    /// </summary>
    public static async Task<HubListener> ConnectAsync(ZaliheApiFactory factory, CookieContainer? cookies)
    {
        using var http = factory.CreateDefaultClient(new CookieContainerHandler(cookies ?? new CookieContainer()));
        var negotiate = await http.PostAsync("/hubs/changes/negotiate?negotiateVersion=1", null);
        negotiate.EnsureSuccessStatusCode();
        var ids = await negotiate.Content.ReadFromJsonAsync<JsonElement>();
        var connectionId = ids.GetProperty("connectionId").GetString()!;
        var token = ids.GetProperty("connectionToken").GetString()!;

        var client = factory.Server.CreateWebSocketClient();
        var cookieHeader = cookies?.GetCookieHeader(factory.Server.BaseAddress);
        client.ConfigureRequest = request =>
        {
            if (!string.IsNullOrEmpty(cookieHeader)) request.Headers.Cookie = cookieHeader;
        };
        var socket = await client.ConnectAsync(new Uri($"ws://localhost/hubs/changes?id={Uri.EscapeDataString(token)}"), CancellationToken.None);

        var listener = new HubListener(socket, connectionId);
        await listener.SendAsync("""{"protocol":"json","version":1}""");
        (await listener.ReceiveRecordAsync(TimeSpan.FromSeconds(5))).ShouldBe("{}");
        return listener;
    }

    /// <summary>The next invocation's target ("dataChanged"), or null when none arrives within <paramref name="timeout"/>.</summary>
    public async Task<string?> NextMessageAsync(TimeSpan timeout)
    {
        while (await ReceiveRecordAsync(timeout) is { } record)
        {
            var message = JsonDocument.Parse(record).RootElement;
            // 6 is the keep-alive ping.
            if (message.GetProperty("type").GetInt32() == 1) return message.GetProperty("target").GetString();
        }

        return null;
    }

    private Task SendAsync(string record) =>
        _socket.SendAsync(Encoding.UTF8.GetBytes(record + RecordSeparator), WebSocketMessageType.Text, true, CancellationToken.None);

    private async Task<string?> ReceiveRecordAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[4096];
        while (_records.Count == 0)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(buffer, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close) return null;
            _partial.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            var text = _partial.ToString();
            var end = text.LastIndexOf(RecordSeparator);
            if (end < 0) continue;
            foreach (var record in text[..end].Split(RecordSeparator)) _records.Enqueue(record);
            _partial.Clear().Append(text[(end + 1)..]);
        }

        return _records.Dequeue();
    }

    public ValueTask DisposeAsync()
    {
        _socket.Abort();
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}

public static class HubListenerExtensions
{
    /// <summary>
    /// Like <see cref="HttpClientExtensions.CreateSignedInClientAsync"/>, but the sign-in cookies stay readable in
    /// <c>Cookies</c>, so a <see cref="HubListener"/> can connect as the same browser.
    /// </summary>
    public static async Task<(HttpClient Client, CookieContainer Cookies)> CreateSignedInBrowserAsync(this ZaliheApiFactory factory)
    {
        var cookies = new CookieContainer();
        var client = factory.CreateDefaultClient(new CookieContainerHandler(cookies));
        await client.RegisterAsync(factory, HttpClientExtensions.UniqueEmail());
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", await client.GetAntiforgeryTokenAsync());
        return (client, cookies);
    }
}
