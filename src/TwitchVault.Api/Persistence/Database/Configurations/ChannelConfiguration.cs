using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Database.Configurations;

public class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.ToTable("Channels");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasMaxLength(50);

        builder.Property(c => c.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(c => c.Name)
            .IsUnique();

        builder.Property(c => c.QualityRank)
            .IsRequired();

        builder.Property(c => c.IsLive)
            .IsRequired();

        builder.Property(c => c.ShouldRecord)
            .IsRequired();

        builder.Property(c => c.LastStreamedAt);
    }
}