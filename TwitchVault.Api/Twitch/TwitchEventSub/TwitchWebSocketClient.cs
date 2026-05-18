using System.Net.WebSockets;
using System.Text;

namespace TwitchVault.Api.Twitch.TwitchEventSub;

public sealed class TwitchWebSocketClient : IAsyncDisposable
{
    private const string TwitchWssUrl = "wss://eventsub.wss.twitch.tv/ws";
    private const int HeartbeatGraceSeconds = 5;
    private readonly byte[] _buffer = new byte[8192];

    private ClientWebSocket _webSocket = null!;
    private ClientWebSocket? _oldSocket;

    private int _keepaliveTimeoutSeconds;
    private CancellationTokenSource _heartbeatCts = new();

    public WebSocketState State => _webSocket.State;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _webSocket = new ClientWebSocket();
        _webSocket.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;
        await _webSocket.ConnectAsync(new Uri(TwitchWssUrl), cancellationToken);
    }

    public async Task ReconnectAsync(string reconnectUrl, CancellationToken cancellationToken)
    {
        _oldSocket = _webSocket;
        _webSocket = new ClientWebSocket();
        _webSocket.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;

        await _webSocket.ConnectAsync(new Uri(reconnectUrl), cancellationToken);
        ResetHeartbeat();
    }

    public async Task CloseOldConnectionAsync()
    {
        if (_oldSocket is null)
            return;

        try
        {
            if (_oldSocket.State == WebSocketState.Open)
                await _oldSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnected", CancellationToken.None);
        }
        catch { }
        finally
        {
            _oldSocket.Dispose();
            _oldSocket = null;
        }
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(_buffer), cancellationToken);

        if (result.MessageType == WebSocketMessageType.Close)
            return null;

        return Encoding.UTF8.GetString(_buffer, 0, result.Count);
    }

    /// <summary>
    /// Configures the keepalive timeout received from Twitch's session_welcome message
    /// and resets the heartbeat countdown.
    /// </summary>
    public void ConfigureHeartbeat(int timeoutSeconds)
    {
        _keepaliveTimeoutSeconds = timeoutSeconds;
        ResetHeartbeat();
    }

    /// <summary>
    /// Resets the heartbeat countdown. Call this on every received message.
    /// </summary>
    public void ResetHeartbeat()
    {
        var old = _heartbeatCts;
        _heartbeatCts = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();
    }

    /// <summary>
    /// Monitors the heartbeat. Completes normally when cancelled (graceful shutdown),
    /// or throws <see cref="KeepaliveTimeoutException"/> if no message arrives within
    /// the configured timeout + grace period.
    /// </summary>
    public async Task MonitorHeartbeatAsync(CancellationToken cancellationToken)
    {
        var totalTimeout = TimeSpan.FromSeconds(_keepaliveTimeoutSeconds + HeartbeatGraceSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                _heartbeatCts.Token, cancellationToken);

            var isTimedOut = await WaitForTimeoutAsync(totalTimeout, linked.Token);

            if (isTimedOut)
                throw new KeepaliveTimeoutException(totalTimeout);
        }
    }

    private static async Task<bool> WaitForTimeoutAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(timeout, cancellationToken);
            return true;
        }
        catch { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        _heartbeatCts.Cancel();
        _heartbeatCts.Dispose();

        if (_webSocket.State == WebSocketState.Open)
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        _webSocket.Dispose();
        _webSocket = null!;
    }
}