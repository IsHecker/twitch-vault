using TwitchVault.Api.ChannelMonitor;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Domain;

public class AppSettings
{
    public VaultOptions Vault { get; set; } = new();
    public TwitchOptions Twitch { get; set; } = new();
    public ChannelMonitorOptions ChannelMonitor { get; set; } = new();
    public TwitchWebhookHealthCheckOptions TwitchWebhookHealthCheck { get; set; } = new();
}