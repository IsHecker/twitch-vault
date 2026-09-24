using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TwitchVault.Api.Database.Configurations;

public class StreamConfiguration : IEntityTypeConfiguration<Features.Streams.Stream>
{
    public void Configure(EntityTypeBuilder<Features.Streams.Stream> builder)
    {
        builder.HasIndex(s => s.ChannelId);

        builder.HasOne(s => s.Channel)
            .WithMany()
            .HasForeignKey(s => s.ChannelId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(s => s.Folder)
            .HasConversion(
                folder => folder.RelativePath,
                path => new Features.Streams.StreamFolder(path))
            .IsRequired();

        builder.OwnsMany(s => s.Chapters, chapters =>
        {
            chapters.ToJson();
        });
    }
}