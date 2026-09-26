using System.Text.Json.Nodes;
using VintageStoryModKit.Build;
using VintageStoryModKit.Settings;

namespace VintageStoryModKit.Tests;

public sealed class IntegrationDescriptorsTests
{
    [Fact]
    public void GenerateProducesSharedProviderDescriptorsAndMergesOverrides()
    {
        var schema = SettingsSchema.Parse(
            """
            {
              "type": "object",
              "title": "Example settings",
              "x-vsmk": {
                "side": "Client",
                "file": "example-settings.json",
                "configlib": { "version": 3 },
                "imm": { "ConfigLabel": "Example settings" }
              },
              "properties": {
                "enabled": { "type": "boolean", "title": "Enable feature", "default": true },
                "count": { "type": "integer", "minimum": 1, "maximum": 5, "multipleOf": 1, "default": 2 },
                "mode": { "type": "string", "enum": ["easy", "hard"], "default": "easy" },
                "tags": { "type": "array", "items": { "type": "string" }, "default": ["one"] },
                "profile": {
                  "type": "object",
                  "properties": {
                    "multiplier": { "type": "number", "default": 1.5, "x-vsmk": { "configlib": { "hide": true } } },
                    "visible": { "type": "boolean", "default": true }
                  },
                  "default": { "multiplier": 1.5, "visible": true }
                },
                "overrides": {
                  "type": "object",
                  "additionalProperties": { "type": "integer", "default": 1 },
                  "default": { "rare": 2 }
                }
              }
            }
            """
        );

        IReadOnlyDictionary<string, string> generated = IntegrationDescriptors.Generate(
            schema,
            "examplemod"
        );
        JsonObject configLib = JsonNode
            .Parse(generated["assets/examplemod/config/configlib-patches.json"])!
            .AsObject();
        JsonObject imm = JsonNode.Parse(generated["assets/examplemod/config/imm.json"])!.AsObject();
        JsonArray configLibSettings = configLib["settings"]!.AsArray();
        JsonArray immSettings = imm["Configuration"]![0]!["Settings"]!.AsArray();

        Assert.Equal(3, configLib["version"]!.GetValue<int>());
        Assert.Equal("example-settings.json", configLib["file"]!.GetValue<string>());
        Assert.DoesNotContain(configLibSettings, setting => setting!["name"] is not null);
        Assert.Equal("float", configLibSettings[4]!["type"]!.GetValue<string>());
        Assert.True(configLibSettings[4]!["hide"]!.GetValue<bool>());
        Assert.Equal("Client", imm["Configuration"]![0]!["ConfigSide"]!.GetValue<string>());
        Assert.Equal("Slider", immSettings[1]!["Type"]!.GetValue<string>());
        Assert.Equal("Dropdown", immSettings[2]!["Type"]!.GetValue<string>());
        Assert.Equal("easy", immSettings[2]!["Options"]![0]!["Label"]!.GetValue<string>());
        Assert.Equal("Array", immSettings[3]!["Type"]!.GetValue<string>());
        Assert.Equal("Decimal", immSettings[4]!["Type"]!.GetValue<string>());
        Assert.Equal("Advanced", immSettings[6]!["Type"]!.GetValue<string>());
        Assert.Equal("Dictionary", immSettings[6]!["Advanced"]!["Type"]!.GetValue<string>());
    }

    [Fact]
    public void ProviderFalseOmitsItsDescriptorOrSetting()
    {
        var schema = SettingsSchema.Parse(
            """
            {
              "type": "object",
              "x-vsmk": { "configlib": false },
              "properties": {
                "shown": { "type": "boolean", "default": true },
                "hidden": { "type": "string", "default": "no", "x-vsmk": { "imm": false } }
              }
            }
            """
        );

        IReadOnlyDictionary<string, string> generated = IntegrationDescriptors.Generate(
            schema,
            "examplemod"
        );
        JsonObject imm = JsonNode.Parse(generated["assets/examplemod/config/imm.json"])!.AsObject();
        JsonArray settings = imm["Configuration"]![0]!["Settings"]!.AsArray();

        Assert.DoesNotContain("assets/examplemod/config/configlib-patches.json", generated.Keys);
        Assert.Single(settings);
        Assert.Equal("shown", settings[0]!["Map"]!.GetValue<string>());
    }

    [Fact]
    public void NullableSettingsWithNullDefaultsUseTheirNonNullType()
    {
        var schema = SettingsSchema.Parse(
            """
            {
              "type": "object",
              "properties": {
                "note": { "type": ["string", "null"], "default": null }
              }
            }
            """
        );

        IReadOnlyDictionary<string, string> generated = IntegrationDescriptors.Generate(
            schema,
            "examplemod"
        );
        JsonObject imm = JsonNode.Parse(generated["assets/examplemod/config/imm.json"])!.AsObject();

        Assert.Equal(
            "String",
            imm["Configuration"]![0]!["Settings"]![0]!["Type"]!.GetValue<string>()
        );
        Assert.Contains("\"string\"", generated["assets/examplemod/config/configlib-patches.json"]);
    }

    [Theory]
    [InlineData("server-port")]
    [InlineData("max size")]
    [InlineData("2x")]
    [InlineData("it's")]
    [InlineData("a\\b")]
    public void ImmMapsResolveWithTheGamesNewtonsoft(string key)
    {
        var schema = SettingsSchema.Parse(
            new JsonObject
            {
                ["type"] = "object",
                ["x-vsmk"] = new JsonObject { ["configlib"] = false },
                ["properties"] = new JsonObject
                {
                    ["Group"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            [key] = new JsonObject { ["type"] = "integer", ["default"] = 7 },
                        },
                    },
                },
            }.ToJsonString()
        );

        JsonNode imm = JsonNode.Parse(
            IntegrationDescriptors.Generate(schema, "examplemod")[
                "assets/examplemod/config/imm.json"
            ]
        )!;
        string map = imm["Configuration"]![0]!["Settings"]![0]!["Map"]!.GetValue<string>();
        var config = Newtonsoft.Json.Linq.JObject.Parse(schema.Defaults.ToJsonString());

        Assert.Equal(7, config.SelectToken(map)!.ToObject<int>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-")]
    [InlineData("@@*")]
    [InlineData("1-3")]
    [InlineData("1..3")]
    [InlineData("name=value")]
    [InlineData("a/b")]
    public void ConfigLibSelectorsCannotReplaceLiteralPropertyNames(string key)
    {
        var property = new JsonObject { ["type"] = "boolean", ["default"] = true };
        var document = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["Group"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject { [key] = property },
                },
            },
        };
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            IntegrationDescriptors.Generate(
                SettingsSchema.Parse(document.ToJsonString()),
                "examplemod"
            )
        );
        Assert.Contains("Group", error.Message, StringComparison.Ordinal);
        Assert.Contains("x-vsmk.configlib", error.Message, StringComparison.Ordinal);

        property["x-vsmk"] = new JsonObject { ["configlib"] = false };
        var schema = SettingsSchema.Parse(document.ToJsonString());
        IReadOnlyDictionary<string, string> generated = IntegrationDescriptors.Generate(
            schema,
            "examplemod"
        );
        Assert.True(schema.Defaults["Group"]![key]!.GetValue<bool>());
        JsonNode imm = JsonNode.Parse(generated["assets/examplemod/config/imm.json"])!;
        Assert.Single(imm["Configuration"]![0]!["Settings"]!.AsArray());
    }

    [Fact]
    public void BoundedNumberWithoutQuantizationUsesImmDecimal()
    {
        var schema = SettingsSchema.Parse(
            """
            {
              "type": "object",
              "x-vsmk": { "configlib": false },
              "properties": {
                "rate": { "type": "number", "minimum": 0, "maximum": 1, "default": 0.5 }
              }
            }
            """
        );

        JsonObject imm = JsonNode
            .Parse(
                IntegrationDescriptors.Generate(schema, "examplemod")[
                    "assets/examplemod/config/imm.json"
                ]
            )!
            .AsObject();
        JsonObject setting = imm["Configuration"]![0]!["Settings"]![0]!.AsObject();

        Assert.Equal("Decimal", setting["Type"]!.GetValue<string>());
        Assert.Null(setting["Min"]);
        Assert.Null(setting["Max"]);
    }

    [Fact]
    public void UnsafeAutomaticSliderFallsBackWhileExplicitSliderIsValidated()
    {
        var fallbackSchema = SettingsSchema.Parse(
            """
            {
              "type": "object",
              "x-vsmk": { "configlib": false },
              "properties": {
                "count": { "type": "integer", "minimum": 0, "maximum": 1000000, "default": 5 }
              }
            }
            """
        );

        JsonObject fallbackImm = JsonNode
            .Parse(
                IntegrationDescriptors.Generate(fallbackSchema, "examplemod")[
                    "assets/examplemod/config/imm.json"
                ]
            )!
            .AsObject();
        Assert.Equal(
            "Integer",
            fallbackImm["Configuration"]![0]!["Settings"]![0]!["Type"]!.GetValue<string>()
        );

        var explicitSchema = SettingsSchema.Parse(
            """
            {
              "type": "object",
              "x-vsmk": { "configlib": false },
              "properties": {
                "rate": {
                  "type": "number", "minimum": 0, "maximum": 1, "default": 0.5,
                  "x-vsmk": { "imm": { "Type": "Slider", "Min": 0, "Max": 1, "Step": 0.5 } }
                }
              }
            }
            """
        );

        JsonObject explicitImm = JsonNode
            .Parse(
                IntegrationDescriptors.Generate(explicitSchema, "examplemod")[
                    "assets/examplemod/config/imm.json"
                ]
            )!
            .AsObject();
        Assert.Equal(
            "Slider",
            explicitImm["Configuration"]![0]!["Settings"]![0]!["Type"]!.GetValue<string>()
        );

        var fractionalSchema = new JsonObject
        {
            ["type"] = "number",
            ["minimum"] = 0,
            ["maximum"] = 1,
            ["multipleOf"] = 0.1,
            ["default"] = 0.5,
        };
        var fractionalDocument = new JsonObject
        {
            ["type"] = "object",
            ["x-vsmk"] = new JsonObject { ["configlib"] = false },
            ["properties"] = new JsonObject { ["rate"] = fractionalSchema },
        };

        JsonObject fractionalImm = JsonNode
            .Parse(
                IntegrationDescriptors.Generate(
                    SettingsSchema.Parse(fractionalDocument.ToJsonString()),
                    "examplemod"
                )["assets/examplemod/config/imm.json"]
            )!
            .AsObject();
        Assert.Equal(
            "Decimal",
            fractionalImm["Configuration"]![0]!["Settings"]![0]!["Type"]!.GetValue<string>()
        );

        fractionalSchema["x-vsmk"] = new JsonObject
        {
            ["imm"] = new JsonObject
            {
                ["Type"] = "Slider",
                ["Min"] = 0,
                ["Max"] = 1,
                ["Step"] = 0.1,
            },
        };
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            IntegrationDescriptors.Generate(
                SettingsSchema.Parse(fractionalDocument.ToJsonString()),
                "examplemod"
            )
        );
        Assert.Contains("multipleOf", error.Message, StringComparison.Ordinal);
    }
}
