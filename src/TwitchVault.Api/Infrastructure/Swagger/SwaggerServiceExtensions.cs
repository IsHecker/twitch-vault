using Microsoft.OpenApi.Models;
namespace TwitchVault.Api.Infrastructure.Swagger;


public static class SwaggerServiceExtensions
{
    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "TwitchVault API",
                Version = "v1",
                Description = "TwitchVault stream recording and archival API."
            });
            options.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
            options.SchemaFilter<EnumSchemaFilter>();
        });
        return services;
    }
}
