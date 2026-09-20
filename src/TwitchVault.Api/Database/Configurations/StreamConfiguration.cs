using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TwitchVault.Api.Database.Configurations;

public class StreamConfiguration : IEntityTypeConfiguration<TwitchVault.Api.Features.Streams.Stream>
{
    public void Configure(EntityTypeBuilder<TwitchVault.Api.Features.Streams.Stream> builder)
    {
        builder.HasIndex(s => s.ChannelId);

        builder.HasOne<Channel>()
            .WithMany()
            .HasForeignKey(s => s.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.Folder)
            .HasValueJsonConverter()
            .IsRequired();

        builder.OwnsMany(s => s.Chapters, chapters =>
        {
            chapters.ToJson();
        });
    }
}