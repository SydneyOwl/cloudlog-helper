using System.Text.Json;
using System.Collections.Generic;
using CloudlogHelper.LogService;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Tests;

public class LogServiceSerializationTests
{
    [Fact]
    public void SettingsJson_RoundTripsLogServicePolymorphism()
    {
        var settings = new ApplicationSettings
        {
            LogServices = new List<ThirdPartyLogService>
            {
                new QRZThirdPartyLogService
                {
                    ApiKey = "test-api-key"
                }
            }
        };

        var json = JsonSerializer.Serialize(settings, AppJsonSerializerOptions.Settings);

        Assert.Contains("\"$type\": \"QRZThirdPartyLogService\"", json);

        var restored = JsonSerializer.Deserialize<ApplicationSettings>(json, AppJsonSerializerOptions.Settings);

        var service = Assert.Single(restored!.LogServices);
        var qrzService = Assert.IsType<QRZThirdPartyLogService>(service);
        Assert.Equal("test-api-key", qrzService.ApiKey);
    }

    [Fact]
    public void SettingsJson_ReadsExistingClassNameDiscriminator()
    {
        const string json = "{\n" +
                            "  \"LogServices\": [\n" +
                            "    {\n" +
                            "      \"$type\": \"QRZThirdPartyLogService\",\n" +
                            "      \"ApiKey\": \"existing-api-key\"\n" +
                            "    }\n" +
                            "  ]\n" +
                            "}";

        var restored = JsonSerializer.Deserialize<ApplicationSettings>(json, AppJsonSerializerOptions.Settings);

        var service = Assert.Single(restored!.LogServices);
        var qrzService = Assert.IsType<QRZThirdPartyLogService>(service);
        Assert.Equal("existing-api-key", qrzService.ApiKey);
    }
}
