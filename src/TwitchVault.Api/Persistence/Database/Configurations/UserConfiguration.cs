using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Database.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // builder.HasIndex(u => u.GoogleId)
        //     .IsUnique();

        builder.Property(u => u.GoogleId)
            .IsRequired(false);

        builder.Property(u => u.Email)
            .IsRequired(false);

        builder.Property(u => u.Username)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasMany(x => x.Channels)
            .WithMany(x => x.Users)
            .UsingEntity<UserChannel>(
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
                    uc.HasKey(x => new { x.UserId, x.ChannelId });

                    uc.Property(x => x.AddedAt)
                        .ValueGeneratedOnAdd()
                        .HasDefaultValueSql("GETUTCDATE()");
                });
    }
}