namespace TwitchVault.Api.Common;

public interface ICurrentUser
{
    Guid Id { get; }
    bool IsAdmin { get; }
}