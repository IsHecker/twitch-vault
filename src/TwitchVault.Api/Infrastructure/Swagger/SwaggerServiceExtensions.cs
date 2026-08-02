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

            // Add JWT Bearer authentication to Swagger UI
            var securityScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "Enter your JWT token: **Bearer {token}**",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Reference = new OpenApiReference
                {
                    Id = "Bearer",
                    Type = ReferenceType.SecurityScheme
                }
            };

            options.AddSecurityDefinition("Bearer", securityScheme);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { securityScheme, Array.Empty<string>() }
            });
        });

        return services;
    }
}
