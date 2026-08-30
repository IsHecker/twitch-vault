using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Database.Configurations;

public class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.HasIndex(c => c.Name)
            .IsUnique();
    }
}