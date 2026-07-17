using System.Net.WebSockets;
using System.Text;

namespace TwitchVault.Api.Twitch.EventSub;

public sealed class TwitchWebSocketClient(ILogger<TwitchWebSocketClient> logger) : IAsyncDisposable
{
    private const string TwitchWssUrl = "wss://eventsub.wss.twitch.tv/ws";
    private const int DefaultKeepaliveTimeoutSeconds = 10;
    private const int HeartbeatGraceSeconds = 15;
    private readonly byte[] _buffer = new byte[8192];
    private ClientWebSocket _webSocket = null!;
    private ClientWebSocket? _oldSocket;
    private int _keepaliveTimeoutSeconds;
    private long _lastMessageTicks = Environment.TickCount64;
    public WebSocketState State => _webSocket.State;

    public Task ConnectAsync(CancellationToken cancellationToken) =>
        ConnectInternalAsync(TwitchWssUrl, cancellationToken);

    public async Task ReconnectAsync(string reconnectUrl, CancellationToken cancellationToken)
    {
        _oldSocket = _webSocket;
        await ConnectInternalAsync(reconnectUrl, cancellationToken);
        ResetHeartbeat();
    }

    public async Task CloseOldConnectionAsync()
    {
        if (_oldSocket is null)
            return;

        if (_oldSocket.State == WebSocketState.Open)
            await _oldSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnected", CancellationToken.None);

        _oldSocket.Dispose();
        _oldSocket = null;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(_buffer), cancellationToken);
        if (result.MessageType == WebSocketMessageType.Close)
            return null;

        return Encoding.UTF8.GetString(_buffer, 0, result.Count);
    }

    public void SetHeartbeat(int? timeoutSeconds)
    {
        _keepaliveTimeoutSeconds = timeoutSeconds ?? DefaultKeepaliveTimeoutSeconds;
        ResetHeartbeat();
    }

    public void ResetHeartbeat()
    {
        Interlocked.Exchange(ref _lastMessageTicks, Environment.TickCount64);
    }

    public async Task MonitorHeartbeatAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_keepaliveTimeoutSeconds == 0)
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }

            var totalTimeout = TimeSpan.FromSeconds(_keepaliveTimeoutSeconds + HeartbeatGraceSeconds);
            await DelayAsync(totalTimeout, cancellationToken);
            var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _lastMessageTicks));

            if (elapsed >= totalTimeout)
            {
                logger.LogWarning("No message received from Twitch within the keepalive timeout ({Timeout}s).",
                    totalTimeout.TotalSeconds);
                break;
            }
        }
    }

    private async Task ConnectInternalAsync(string url, CancellationToken cancellationToken)
    {
        _webSocket = new ClientWebSocket();
        _webSocket.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;
        await _webSocket.ConnectAsync(new Uri(url), cancellationToken);
        ResetHeartbeat();
    }

    private static async Task DelayAsync(TimeSpan totalTimeout, CancellationToken cancellationToken)
    {
        var intervalMs = (int)(totalTimeout.TotalMilliseconds / 2);
        try
        {
            await Task.Delay(intervalMs, cancellationToken);
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_webSocket.State == WebSocketState.Open)
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);

        _webSocket.Dispose();
        _webSocket = null!;
    }
}