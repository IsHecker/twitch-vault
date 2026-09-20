using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TwitchVault.Api.Database.Configurations;

public class BannedChannelConfiguration : IEntityTypeConfiguration<BannedChannel>
{
    public void Configure(EntityTypeBuilder<BannedChannel> builder)
    {
        builder.Property(b => b.Reason).HasMaxLength(500);
        builder.HasIndex(b => b.ChannelName);
    }
}