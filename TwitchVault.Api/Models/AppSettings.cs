using TwitchVault.Api.ChannelMonitor;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.StoppedStreamCheck;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Models;

public class AppSettings
{
    public VaultOptions Vault { get; set; } = new();
    public TwitchOptions Twitch { get; set; } = new();
    public ChannelMonitorOptions ChannelMonitor { get; set; } = new();
    public StoppedStreamCheckOptions StoppedStreamCheck { get; set; } = new();
}