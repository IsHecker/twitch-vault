using TwitchVault.Api.Domain;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Recording;

public interface IRecordingOrchestrator
{
    Task HandleStreamOnlineAsync(string channelId, string channelName);
    Task StartAsync(Channel channel, StreamMetadata metadata);
    Task StopRecordingAsync(string channelId);
    Task ToggleStreamDeletionAsync(string channelId, bool markForDeletion);
    Task ResumeStreamAsync(Domain.Stream stream, Channel channel);
}