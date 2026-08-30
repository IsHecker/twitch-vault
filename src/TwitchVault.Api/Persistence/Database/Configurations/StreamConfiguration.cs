using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;

namespace TwitchVault.Api.Persistence.Database.Configurations;

public class StreamConfiguration : IEntityTypeConfiguration<Domain.Stream>
{
    public void Configure(EntityTypeBuilder<Domain.Stream> builder)
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