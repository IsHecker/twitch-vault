using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;

namespace TwitchVault.Api.Persistence.Database.Configurations;

public class StreamConfiguration : IEntityTypeConfiguration<Domain.Stream>
{
    public void Configure(EntityTypeBuilder<Domain.Stream> builder)
    {
        builder.ToTable("Streams");

        builder.HasKey(s => s.TwitchStreamId);
        builder.Property(s => s.TwitchStreamId)
            .HasMaxLength(50);

        builder.Property(s => s.ChannelId)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(s => s.ChannelId);

        builder.HasOne<Channel>()
            .WithMany()
            .HasForeignKey(s => s.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.Status)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.StorageLocation)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.StorageOperationStatus)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.SizeBytes)
            .IsRequired();

        builder.Property(s => s.StorageInstanceName)
            .HasMaxLength(100);

        builder.Property(s => s.StartedAt)
            .IsRequired();

        builder.Property(s => s.FinishedAt);

        builder.Property(s => s.Folder)
            .HasValueJsonConverter()
            .IsRequired();

        builder.Property(s => s.Chapters)
            .HasValueJsonConverter()
            .IsRequired();

        builder.Ignore(s => s.CurrentChapter);
    }
}