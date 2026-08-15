namespace TwitchVault.Api.CloudStorage;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class StorageProviderAttribute(CloudProviderType providerType, Type optionsType) : Attribute
{
    public CloudProviderType ProviderType => providerType;
    public Type OptionsType => optionsType;
}