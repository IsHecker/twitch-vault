using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace TwitchVault.Api.Configuration;

public class EgyptTimeEnricher(IDateTimeProvider dateTimeProvider) : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var egyptNow = dateTimeProvider.DateTimeNow;
        logEvent.AddOrUpdateProperty(
            propertyFactory.CreateProperty("EgyptTime", egyptNow.ToString("yyyy-MM-dd hh:mm:ss.fff tt")));
    }
}

public static class LoggerEnrichmentConfigurationExtensions
{
    private static readonly IDateTimeProvider provider = new EgyptTimeProvider();

    public static LoggerConfiguration WithEgyptTime(
        this LoggerEnrichmentConfiguration enrich)
    {
        return enrich.With(new EgyptTimeEnricher(provider));
    }
}