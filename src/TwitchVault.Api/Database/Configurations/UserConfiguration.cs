using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TwitchVault.Api.Database.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasIndex(u => u.GoogleId)
            .IsUnique();

        builder.Property(u => u.GoogleId)
            .IsRequired();

        builder.Property(u => u.Email)
            .IsRequired();

        builder.Property(u => u.Username)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasMany(x => x.Channels)
            .WithMany(x => x.Users)
            .UsingEntity<Subscription>(
                uc => uc
                    .HasOne(x => x.Channel)
                    .WithMany()
                    .HasForeignKey(x => x.ChannelId),

                uc => uc
                    .HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId),

                uc =>
                {
                    uc.ToTable("Subscriptions");
                    uc.HasKey(x => new { x.UserId, x.ChannelId });

                    uc.Property(x => x.AddedAt)
                        .ValueGeneratedOnAdd()
                        .HasDefaultValueSql("GETUTCDATE()");
                });
    }
}