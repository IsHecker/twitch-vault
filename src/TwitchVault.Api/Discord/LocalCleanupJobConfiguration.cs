namespace TwitchVault.Api.Discord;

// public sealed class LocalCleanupJobConfiguration : IConfigureOptions<QuartzOptions>
// {
//     public void Configure(QuartzOptions options)
//     {
//         string jobName = typeof(LocalCleanupJob).FullName!;
//         options
//             .AddJob<LocalCleanupJob>(configure => configure.WithIdentity(jobName))
//             .AddTrigger(configure =>
//                 configure
//                     .ForJob(jobName)
//                     .StartNow()
//                     .WithCronSchedule("0 0 3 * * ?"));
//     }
// }