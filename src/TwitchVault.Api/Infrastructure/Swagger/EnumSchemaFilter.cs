using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using TwitchVault.Api.Recording.HLS;

using TwitchVault.Api.Events;
namespace TwitchVault.Api.Infrastructure.Swagger;


public sealed class EnumSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (!context.Type.IsEnum)
            return;
        schema.Enum = Enum
            .GetNames(context.Type)
            .Select(name => new OpenApiString(name))
            .ToList<IOpenApiAny>();
    }
}
