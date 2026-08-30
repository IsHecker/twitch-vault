// using Microsoft.EntityFrameworkCore;
// using Microsoft.EntityFrameworkCore.Metadata.Builders;
// using TwitchVault.Api.Domain;

// namespace TwitchVault.Api.Persistence.Database.Configurations;

// public class UserChannelConfiguration : IEntityTypeConfiguration<UserChannel>
// {
//     public void Configure(EntityTypeBuilder<UserChannel> builder)
//     {
//         builder.HasKey(uc => new { uc.UserId, uc.ChannelId });

//         builder.HasOne<User>()
//             .WithMany()
//             .HasForeignKey(uc => uc.UserId)
//             .OnDelete(DeleteBehavior.Cascade);

//         builder.HasOne(uc => uc.Channel)
//             .WithMany()
//             .HasForeignKey(uc => uc.ChannelId)
//             .OnDelete(DeleteBehavior.Cascade);
//     }
// }