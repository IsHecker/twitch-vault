namespace TwitchVault.Api.Domain;

public class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
}