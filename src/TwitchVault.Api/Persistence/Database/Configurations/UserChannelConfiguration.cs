using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Database.Configurations;

public class UserChannelConfiguration : IEntityTypeConfiguration<UserChannel>
{
    public void Configure(EntityTypeBuilder<UserChannel> builder)
    {
        builder.ToTable("UserChannels");

        builder.HasKey(uc => new { uc.UserId, uc.ChannelId });

        builder.Property(uc => uc.ChannelId)
            .HasMaxLength(50);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(uc => uc.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Channel>()
            .WithMany()
            .HasForeignKey(uc => uc.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(uc => uc.AddedAt)
            .IsRequired();
    }
}