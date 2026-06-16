using System.Reflection;
using System.Text.Json;
using Xunit.Sdk;

namespace TwitchVault.Api.Tests.Unit.Common;

public class HlsJsonDataAttribute : DataAttribute
{
    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Samples", "HlsTagReaderTestInputs.json");

        if (!File.Exists(jsonPath))
            throw new FileNotFoundException("Test data JSON not found", jsonPath);

        using var jsonDocument = JsonDocument.Parse(File.ReadAllText(jsonPath));

        if (!jsonDocument.RootElement.TryGetProperty(testMethod.Name, out var testCases))
            throw new KeyNotFoundException($"No test data found in JSON for method: {testMethod.Name}");

        foreach (var testCase in testCases.EnumerateArray())
        {
            var result = new List<object>
            {
                testCase.GetProperty("tag").GetString()!,
                testCase.GetProperty("endChar").GetString()![0]
            };

            if (testCase.TryGetProperty("expected", out var expected))
                result.Add(expected.GetString()!);

            yield return result.ToArray();
        }
    }
}