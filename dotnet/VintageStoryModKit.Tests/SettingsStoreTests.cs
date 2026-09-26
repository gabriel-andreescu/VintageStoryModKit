using System.Text.Json;
using System.Text.Json.Nodes;
using VintageStoryModKit.Settings;

namespace VintageStoryModKit.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Environment.GetEnvironmentVariable("VSMK_TEST_ROOT")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "scratch"),
        "settings-" + Guid.NewGuid().ToString("N")
    );
    private static SettingsSchema Schema =>
        SettingsSchema.Parse(
            """
            {
              "type": "object",
              "properties": {
                "Enabled": { "type": "boolean", "default": true },
                "Tuning": { "type": "object", "properties": {
                  "Count": { "type": "integer", "default": 5, "minimum": 0 },
                  "Rate": { "type": "number", "default": 0.5 }
                }}
              }
            }
            """
        );

    private SettingsStore CreateStore() => new(Schema, Path.Combine(directory, "example.json"));

    [Fact]
    public void CreatesNestedDefaultsAndPreservesPlayerValuesAndUnknownFields()
    {
        SettingsStore store = CreateStore();
        store.Reload();
        File.WriteAllText(
            store.FilePath,
            """{"Enabled":false,"Tuning":{"Count":12},"Extra":"keep me"}"""
        );
        store.Reload();
        Assert.False(store.Get<bool>("Enabled"));
        Assert.Equal(12, store.Get<int>("/Tuning/Count"));
        Assert.Equal(0.5, store.Get<double>("/Tuning/Rate"));
        JsonNode persisted = JsonNode.Parse(File.ReadAllText(store.FilePath))!;
        Assert.Equal("keep me", persisted["Extra"]!.GetValue<string>());
        Assert.Equal(0.5, persisted["Tuning"]!["Rate"]!.GetValue<double>());
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"Enabled\":\"not a boolean\"}")]
    [InlineData("{\"Tuning\":{\"Count\":-1}}")]
    [InlineData("{\"Enabled\":true,\"Enabled\":false}")]
    [InlineData("{\"Tuning\":{\"Count\":1e30}}")]
    [InlineData("{\"Extra\":\"\\uD800\"}")]
    public void InvalidEditsPreserveFileAndLastValidValues(string invalid)
    {
        SettingsStore store = CreateStore();
        store.Reload();
        JsonObject valid = store.Current;
        valid["Enabled"] = false;
        store.Save(valid);
        File.WriteAllText(store.FilePath, invalid);
        Exception? exception = Record.Exception(() => store.Reload());
        Assert.True(
            exception is JsonException or SettingsValidationException,
            exception?.ToString()
        );
        Assert.Equal(invalid, File.ReadAllText(store.FilePath));
        Assert.False(store.Get<bool>("Enabled"));
    }

    [Fact]
    public void PollingDetectsSameTimestampEditsAndOnlyNotifiesOnChangedValues()
    {
        SettingsStore store = CreateStore();
        store.Reload();
        DateTime timestamp = File.GetLastWriteTimeUtc(store.FilePath);
        int notifications = 0;
        store.Changed += () => notifications++;
        Assert.False(store.Reload(true));
        JsonObject value = store.Current;
        value["Enabled"] = false;
        File.WriteAllText(store.FilePath, value.ToJsonString());
        File.SetLastWriteTimeUtc(store.FilePath, timestamp);
        Assert.True(store.Reload(true));
        Assert.False(store.Reload(true));
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void ApplyingServerSnapshotNeverWritesClientFile()
    {
        SettingsStore store = CreateStore();
        JsonObject snapshot = store.Current;
        snapshot["Enabled"] = false;
        snapshot["FutureValues"] = new JsonArray(2, 4);
        snapshot["FutureNullable"] = null;
        Assert.True(store.ApplySnapshot(snapshot));
        Assert.False(store.Get<bool>("Enabled"));
        Assert.Equal(4, store.Get<int>("/FutureValues/1"));
        Assert.Null(store.Get<string?>("FutureNullable"));
        Assert.Throws<KeyNotFoundException>(() => store.Get<string>("Missing"));
        Assert.False(File.Exists(store.FilePath));
        snapshot["Enabled"] = true;
        Assert.False(store.Get<bool>("Enabled"));
    }

    [Fact]
    public void SaveValidatesBeforeReplacingFileAndSnapshotsCannotMutateState()
    {
        SettingsStore store = CreateStore();
        store.Reload();
        string original = File.ReadAllText(store.FilePath);
        JsonObject invalid = store.Current;
        invalid["Enabled"] = "invalid";
        Assert.Throws<SettingsValidationException>(() => store.Save(invalid));
        Assert.Equal(original, File.ReadAllText(store.FilePath));
        Assert.True(store.Get<bool>("Enabled"));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
