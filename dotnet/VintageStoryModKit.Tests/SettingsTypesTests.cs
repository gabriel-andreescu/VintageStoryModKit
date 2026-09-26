using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using VintageStoryModKit.Build;
using VintageStoryModKit.Settings;

namespace VintageStoryModKit.Tests;

public sealed class SettingsTypesTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Environment.GetEnvironmentVariable("VSMK_TEST_ROOT")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "scratch"),
        "types-" + Guid.NewGuid().ToString("N")
    );

    private static SettingsSchema Schema =>
        SettingsSchema.Parse(
            """
            {
              "type": "object",
              "properties": {
                "Enabled": { "type": "boolean", "default": true },
                "Count": { "type": "integer", "default": 5, "minimum": 0, "maximum": 100 },
                "Seed": { "type": "integer", "default": 1 },
                "Rate": { "type": "number", "default": 0.5 },
                "Label": { "type": "string", "default": "wolf" },
                "Note": { "type": ["string", "null"], "default": null },
                "server-port": { "type": "integer", "default": 8941, "minimum": 1, "maximum": 65535 },
                "Tuning": {
                  "type": "object",
                  "properties": { "Radius": { "type": "number", "default": 2 } }
                },
                "Tags": { "type": "array", "items": { "type": "string" }, "default": ["a"] },
                "Rules": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": { "Name": { "type": "string", "default": "" } }
                  },
                  "default": [{ "Name": "first" }]
                },
                "Weights": {
                  "type": "object",
                  "additionalProperties": { "type": "number" },
                  "default": { "a": 1 }
                },
                "Raw": { "default": {} }
              }
            }
            """
        );

    [Fact]
    public void GeneratedTypesMapSchemaShapes()
    {
        Type type = Compile(Schema);

        Assert.Equal(typeof(bool), PropertyType(type, "Enabled"));
        Assert.Equal(typeof(int), PropertyType(type, "Count"));
        Assert.Equal(typeof(long), PropertyType(type, "Seed"));
        Assert.Equal(typeof(double), PropertyType(type, "Rate"));
        Assert.Equal(typeof(string), PropertyType(type, "Label"));
        Assert.Equal(typeof(string), PropertyType(type, "Note"));
        Assert.Equal(typeof(int), PropertyType(type, "ServerPort"));
        Assert.Equal("Example.ModSettings+TuningSettings", PropertyType(type, "Tuning").FullName);
        Assert.Equal(typeof(IReadOnlyList<string>), PropertyType(type, "Tags"));
        Assert.Equal(
            "Example.ModSettings+RulesItem",
            PropertyType(type, "Rules").GetGenericArguments()[0].FullName
        );
        Assert.Equal(typeof(IReadOnlyDictionary<string, double>), PropertyType(type, "Weights"));
        Assert.Equal(typeof(JsonNode), PropertyType(type, "Raw"));
    }

    [Fact]
    public void TypedSavesReplaceModeledValuesAndKeepUnknownFields()
    {
        Type type = Compile(Schema);
        var store = new SettingsStore(Schema, Path.Combine(directory, "example.json"));
        store.Reload();
        File.WriteAllText(
            store.FilePath,
            """{"Count":12,"Tuning":{"Radius":3,"Extra":"nested"},"Extra":"root"}"""
        );
        store.Reload();

        object snapshot = Invoke(store, nameof(SettingsStore.Snapshot), type)!;
        Assert.Equal(12, type.GetProperty("Count")!.GetValue(snapshot));
        Assert.Equal(8941, type.GetProperty("ServerPort")!.GetValue(snapshot));
        type.GetProperty("Count")!.SetValue(snapshot, 7);
        type.GetProperty("Weights")!
            .SetValue(snapshot, new Dictionary<string, double> { ["b"] = 2 });
        Invoke(store, nameof(SettingsStore.Save), type, snapshot);

        JsonNode persisted = JsonNode.Parse(File.ReadAllText(store.FilePath))!;
        Assert.Equal(7, persisted["Count"]!.GetValue<int>());
        Assert.Equal("root", persisted["Extra"]!.GetValue<string>());
        Assert.Equal("nested", persisted["Tuning"]!["Extra"]!.GetValue<string>());
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse("""{"b":2}"""), persisted["Weights"]),
            persisted["Weights"]!.ToJsonString()
        );
    }

    [Fact]
    public void SnapshotsReadValidFilesThatDefaultsDoNotComplete()
    {
        Type type = Compile(Schema);
        var store = new SettingsStore(Schema, Path.Combine(directory, "example.json"));
        store.Reload();
        File.WriteAllText(store.FilePath, """{"Count":7.0,"Rules":[{}]}""");
        store.Reload();

        object snapshot = Invoke(store, nameof(SettingsStore.Snapshot), type)!;
        Assert.Equal(7, type.GetProperty("Count")!.GetValue(snapshot));
        object? rule = Assert.Single(
            (System.Collections.IEnumerable)type.GetProperty("Rules")!.GetValue(snapshot)!
        );
        Assert.Null(rule!.GetType().GetProperty("Name")!.GetValue(rule));

        Invoke(store, nameof(SettingsStore.Save), type, snapshot);
        JsonNode persisted = JsonNode.Parse(File.ReadAllText(store.FilePath))!;
        Assert.True(JsonNode.DeepEquals(new JsonArray(new JsonObject()), persisted["Rules"]));
    }

    [Fact]
    public void RejectsPropertiesThatMapToTheSameMember()
    {
        var schema = SettingsSchema.Parse(
            """
            {
              "type": "object",
              "properties": {
                "my-key": { "type": "string", "default": "" },
                "myKey": { "type": "string", "default": "" }
              }
            }
            """
        );

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            SettingsTypes.Generate(schema, "Example", "ModSettings")
        );
        Assert.Contains("MyKey", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private static Type Compile(SettingsSchema schema)
    {
        string source = SettingsTypes.Generate(schema, "Example", "ModSettings");
        IEnumerable<PortableExecutableReference> references = (
            (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!
        )
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "Generated" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
        using var image = new MemoryStream();
        EmitResult result = compilation.Emit(image);
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics) + Environment.NewLine + source
        );
        return Assembly.Load(image.ToArray()).GetType("Example.ModSettings", true)!;
    }

    private static Type PropertyType(Type type, string name) =>
        type.GetProperty(name)?.PropertyType
        ?? throw new InvalidOperationException($"{type.Name} has no property {name}.");

    private static object? Invoke(
        SettingsStore store,
        string name,
        Type type,
        params object[] arguments
    ) =>
        typeof(SettingsStore)
            .GetMethods()
            .Single(method => method.Name == name && method.IsGenericMethodDefinition)
            .MakeGenericMethod(type)
            .Invoke(store, arguments);
}
