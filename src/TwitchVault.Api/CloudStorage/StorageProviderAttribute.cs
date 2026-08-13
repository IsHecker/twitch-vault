namespace TwitchVault.Api.CloudStorage;

[AttributeUsage(AttributeTargets.Class)]
public class StorageProviderAttribute(CloudProviderType providerType, Type optionsType) : Attribute
{
    public CloudProviderType ProviderType => providerType;
    public Type OptionsType => optionsType;
}