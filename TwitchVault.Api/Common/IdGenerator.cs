namespace TwitchVault.Api.Common;

public static class IdGenerator
{
    public static int Generate() => Random.Shared.Next(10_000_000, 100_000_000);
}