namespace TwitchVault.Api.Domain;

public class User : Entity<Guid>
{
    public string Username { get; private set; } = string.Empty;
    public string Password { get; private set; } = string.Empty;
    public bool IsAdmin { get; private set; }

    public ICollection<Channel> Channels { get; private set; } = [];

    private User() { }

    public static User Create(Guid id, string username, string password, DateTime createdAt, bool isAdmin = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        return new User
        {
            Id = id,
            Username = username.Trim(),
            Password = password,
            IsAdmin = isAdmin,
            CreatedAt = createdAt
        };
    }
}