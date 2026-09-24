namespace TwitchVault.Api.Features.Recording;

public interface IRecordingOrchestrator
{
    Task TryStartRecordingAsync(string channelId, string channelName);
    Task StopRecordingAsync(string channelId);
    Task<IReadOnlyList<string>> FinishAllRecordingsAsync(IReadOnlyCollection<string>? channelIds = null);
    Task StopAllRecordingsAsync();
}