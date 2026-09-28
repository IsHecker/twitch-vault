namespace TwitchVault.Api.Features.Streams;

public enum StorageOperationStatus
{
    None,
    Uploading,
    Uploaded,
    UploadFailed,
    DeleteRequest,
    Deleting,
    DeleteFailed
}