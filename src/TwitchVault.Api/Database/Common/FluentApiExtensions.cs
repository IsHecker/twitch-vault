using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TwitchVault.Api.Database.Common;

public static class FluentApiExtensions
{
    public static PropertyBuilder<T> HasValueJsonConverter<T>(this PropertyBuilder<T> propertyBuilder)
    {
        return propertyBuilder.HasConversion(
            new ValueJsonConverter<T>(),
            new ValueJsonComparer<T>());
    }

    public static void StoreAllEnumsAsNames(this ModelBuilder modelBuilder)
    {
        var enumConverterType = typeof(EnumToStringConverter<>);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties()
                .Where(p => p.ClrType.IsEnum || (Nullable.GetUnderlyingType(p.ClrType)?.IsEnum ?? false)))
            {
                if (property.GetValueConverter() != null)
                    continue;

                var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                var converter = (ValueConverter)Activator.CreateInstance(enumConverterType.MakeGenericType(enumType))!;
                property.SetValueConverter(converter);
            }
        }
    }
}