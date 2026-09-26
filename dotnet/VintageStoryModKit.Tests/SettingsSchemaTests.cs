using System.Text.Json.Nodes;
using VintageStoryModKit.Settings;

namespace VintageStoryModKit.Tests;

public sealed class SettingsSchemaTests
{
    [Fact]
    public void ParseAppliesNestedDefaultsAndPreservesUnknownValues()
    {
        var schema = SettingsSchema.Parse(
            """
            {
              "type": "object",
              "x-vsmk": { "side": "Client", "file": "example-settings.json" },
              "properties": {
                "enabled": { "type": "boolean", "default": true },
                "gameplay": {
                  "type": "object",
                  "properties": {
                    "maximum": { "type": "integer", "default": 4 },
                    "title": { "type": "string", "default": "Example" }
                  }
                }
              }
            }
            """
        );

        JsonObject merged = schema.ApplyDefaults(
            JsonNode
                .Parse(
                    """
                    {
                      "gameplay": { "maximum": 7, "futureValue": "kept" },
                      "outsideSchema": 3
                    }
                    """
                )!
                .AsObject()
        );

        Assert.Equal(SettingsSide.Client, schema.Side);
        Assert.Equal("example-settings.json", schema.ConfigFile("examplemod"));
        Assert.Equal(7, merged["gameplay"]!["maximum"]!.GetValue<int>());
        Assert.Equal("Example", merged["gameplay"]!["title"]!.GetValue<string>());
        Assert.Equal("kept", merged["gameplay"]!["futureValue"]!.GetValue<string>());
        Assert.Equal(3, merged["outsideSchema"]!.GetValue<int>());
        Assert.True(merged["enabled"]!.GetValue<bool>());

        JsonObject exportedDefaults = schema.Defaults;
        exportedDefaults["gameplay"]!["maximum"] = 99;
        Assert.Equal(4, schema.Defaults["gameplay"]!["maximum"]!.GetValue<int>());
    }

    [Fact]
    public void ValidateReportsSchemaFailures()
    {
        var schema = SettingsSchema.Parse(
            """
            {
              "type": "object",
              "properties": {
                "Rules": {
                  "type": "array",
                  "default": [],
                  "items": {
                    "type": "object",
                    "properties": { "Name": { "type": "string" } }
                  }
                }
              }
            }
            """
        );

        SettingsValidationException exception = Assert.Throws<SettingsValidationException>(() =>
            schema.Validate(
                new JsonObject { ["Rules"] = new JsonArray(new JsonObject { ["Name"] = null }) }
            )
        );

        Assert.Contains("/Rules/0/Name", exception.Message, StringComparison.Ordinal);
        Assert.Contains("null", exception.Message, StringComparison.Ordinal);
        Assert.Contains("string", exception.Message, StringComparison.Ordinal);
    }
}
