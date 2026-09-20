namespace TwitchVault.Api.Common.Endpoints;

public interface IEndpoint { void MapEndpoint(IEndpointRouteBuilder app); }

/// <summary>
/// Marker interface for endpoints that should only be registered in the
/// Development environment. Classes implementing this are skipped in production.
/// </summary>
public interface IDevOnlyEndpoint : IEndpoint { }