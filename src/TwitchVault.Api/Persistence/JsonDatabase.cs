using System.Reflection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence;

public class JsonDatabase
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly AppDatabase _cache;

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new PrivateSetterContractResolver(),
        Converters = { new StringEnumConverter() }
    };

    public JsonDatabase(IOptions<PathsOptions> options)
    {
        _filePath = options.Value.Database;
        _cache = Load();
    }

    public async Task<AppDatabase> ReadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task WriteAsync(Action<AppDatabase> mutation)
    {
        await _lock.WaitAsync();
        try
        {
            mutation(_cache);
            await FlushAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private AppDatabase Load()
    {
        if (!File.Exists(_filePath))
            return new AppDatabase();

        var json = File.ReadAllText(_filePath);
        return JsonConvert.DeserializeObject<AppDatabase>(json, JsonSettings) ?? new AppDatabase();
    }

    private async Task FlushAsync()
    {
        var tempFilePath = $"{_filePath}.tmp";
        var json = JsonConvert.SerializeObject(_cache, JsonSettings);

        await File.WriteAllTextAsync(tempFilePath, json);

        File.Move(tempFilePath, _filePath, overwrite: true);
    }
}

public class PrivateSetterContractResolver : DefaultContractResolver
{
    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        var prop = base.CreateProperty(member, memberSerialization);
        if (!prop.Writable && member is PropertyInfo propInfo)
        {
            var hasPrivateSetter = propInfo.GetSetMethod(true) != null;
            prop.Writable = hasPrivateSetter;
        }
        return prop;
    }
}