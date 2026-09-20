namespace TwitchVault.Api.Domain;

public class User : Entity<Guid>
{
    public string Username { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string GoogleId { get; private set; } = string.Empty;
    public bool IsAdmin { get; private set; }

    public ICollection<Channel> Channels { get; private set; } = [];

    private User() { }

    public static User Create(Guid id, string username, string email, string googleId, DateTime createdAt, bool isAdmin = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(googleId);

        return new User
        {
            Id = id,
            Username = username.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            GoogleId = googleId.Trim(),
            IsAdmin = isAdmin,
            CreatedAt = createdAt
        };
    }
}