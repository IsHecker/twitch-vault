using System.Net.WebSockets;
using System.Text;

namespace TwitchVault.Api.Twitch.TwitchEventSub;

public sealed class TwitchWebSocketClient : IAsyncDisposable
{
    private const string TwitchWssUrl = "wss://eventsub.wss.twitch.tv/ws";
    private readonly byte[] _buffer = new byte[8192];

    private ClientWebSocket _webSocket = null!;
    private readonly MemoryStream _memoryStream = new();

    public WebSocketState State => _webSocket.State;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _webSocket = new ClientWebSocket();
        _webSocket.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;
        await _webSocket.ConnectAsync(new Uri(TwitchWssUrl), cancellationToken);
    }

    public async Task ReconnectAsync(string reconnectUrl, CancellationToken cancellationToken)
    {
        var oldSocket = _webSocket;
        _webSocket = new ClientWebSocket();
        _webSocket.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;

        await _webSocket.ConnectAsync(new Uri(reconnectUrl), cancellationToken);

        _ = Task.Run(async () =>
        {
            try
            {
                await oldSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", CancellationToken.None);
            }
            catch { }
            finally { oldSocket.Dispose(); }
        }, CancellationToken.None);
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        _memoryStream.SetLength(0);

        while (true)
        {
            var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(_buffer), cancellationToken);
            _memoryStream.Write(_buffer, 0, result.Count);

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(_memoryStream.ToArray());
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_webSocket.State == WebSocketState.Open)
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        _webSocket.Dispose();
        _webSocket = null!;
    }
}