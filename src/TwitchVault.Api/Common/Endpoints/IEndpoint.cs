namespace TwitchVault.Api.Common.Endpoints;

public interface IEndpoint { void MapEndpoint(IEndpointRouteBuilder app); }

public interface IDevOnlyEndpoint : IEndpoint { }