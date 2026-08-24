namespace TwitchVault.Api.Domain;

public class User
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Username { get; private set; } = string.Empty;
    public string Password { get; private set; } = string.Empty;
    public bool IsAdmin { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Parameterless constructor for EF Core & JSON deserialization
    private User() { }

    public static User Create(string username, string password, DateTime createdAt, bool isAdmin = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        return new User
        {
            Id = Guid.NewGuid(),
            Username = username.Trim(),
            Password = password,
            IsAdmin = isAdmin,
            CreatedAt = createdAt
        };
    }

    public static User CreateAdmin(string username, string password, DateTime createdAt) =>
        Create(username, password, createdAt, isAdmin: true);

    public void UpdatePassword(string newPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);
        Password = newPassword;
    }

    public void SetAdminStatus(bool isAdmin)
    {
        IsAdmin = isAdmin;
    }
}