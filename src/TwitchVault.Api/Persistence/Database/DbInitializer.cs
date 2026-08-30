using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;
using TwitchVault.Api.Domain;

// Run this ONCE against a freshly-migrated (empty) database, then delete it
// or move it somewhere it won't accidentally run again.
//
// Why Newtonsoft.Json here specifically: Channel, Stream, User and
// UserChannel only expose PRIVATE parameterless constructors. Newtonsoft.Json
// handles that out of the box (it can call non-public constructors).
// System.Text.Json will NOT, by default, and will throw at deserialize time
// unless you add [JsonConstructor]/[JsonInclude] attributes to these classes.
//
// IMPORTANT: constructors aren't the whole story. Properties like
// Channel.Name / User.Username have PRIVATE setters with no [JsonProperty]
// attribute. Newtonsoft's *default* resolver silently refuses to write to
// those (it only writes public setters unless told otherwise) — it won't
// throw, it just leaves the property at its default value. That's what
// PrivateSetterContractResolver below fixes: it forces any property with a
// private setter to be treated as writable during deserialization.
public static class JsonDataMigrator
{
    private sealed class PrivateSetterContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            if (!property.Writable && member is PropertyInfo propertyInfo)
            {
                property.Writable = propertyInfo.GetSetMethod(nonPublic: true) is not null;
            }
            return property;
        }
    }

    public static async Task RunAsync(string jsonFilePath, AppDbContext db)
    {
        if (await db.Users.AnyAsync() || await db.Channels.AnyAsync() || await db.Streams.AnyAsync())
        {
            throw new InvalidOperationException(
                "Target database already has data. Aborting to avoid duplicating/corrupting it.");
        }

        var json = await File.ReadAllTextAsync(jsonFilePath);

        var settings = new JsonSerializerSettings
        {
            ContractResolver = new PrivateSetterContractResolver(),
            // If you originally wrote the JSON with enums-as-strings
            // (StringEnumConverter) or any custom converters, add them here
            // too so the values round-trip identically.
        };

        var oldDb = JsonConvert.DeserializeObject<AppDatabase>(json, settings)
            ?? throw new InvalidOperationException("Old database file was empty or could not be parsed.");

        // Sanity check before touching the DB: catch a bad deserialization
        // early instead of finding out via a constraint violation again.
        var blankChannelNames = oldDb.Channels.Count(c => string.IsNullOrEmpty(c.Name));
        var blankUsernames = oldDb.Users.Count(u => string.IsNullOrEmpty(u.Username));
        if (blankChannelNames > 0 || blankUsernames > 0)
        {
            throw new InvalidOperationException(
                $"Deserialization looks wrong: {blankChannelNames} channel(s) with blank Name, " +
                $"{blankUsernames} user(s) with blank Username. Fix the mapping before inserting.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // Users and Channels have no dependencies on other tables — insert first.
            await db.Users.AddRangeAsync(oldDb.Users);
            await db.Channels.AddRangeAsync(oldDb.Channels);
            await db.SaveChangesAsync();

            // UserChannels needs both of the above to already exist.
            // Streams needs Channels to exist, and brings its owned
            // Folder/Chapters along automatically.
            await db.UserChannels.AddRangeAsync(oldDb.UserChannels);
            await db.Streams.AddRangeAsync(oldDb.Streams);
            await db.SaveChangesAsync();

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        Console.WriteLine(
            $"Migrated {oldDb.Users.Count} users, {oldDb.Channels.Count} channels, " +
            $"{oldDb.UserChannels.Count} user-channel links, {oldDb.Streams.Count} streams " +
            $"({oldDb.Streams.Sum(s => s.Chapters.Count)} chapters total).");
    }
}