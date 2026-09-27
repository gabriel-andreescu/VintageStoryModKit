using System.Text.Json.Nodes;
using VintageStoryModKit.Build;
using VintageStoryModKit.Settings;

const string Usage = """
    Usage:
      VintageStoryModKit.Build generate-settings <schema.json> <modinfo.json> <output-directory>
      VintageStoryModKit.Build generate-settings-types <schema.json> <output.cs> <namespace> <type-name>
      VintageStoryModKit.Build embed-runtime <request.txt> <output-directory>
    """;

try
{
    return args switch
    {
        ["generate-settings", var schema, var modInfo, var output] => GenerateSettings(
            schema,
            modInfo,
            output
        ),
        ["generate-settings-types", var schema, var output, var ns, var typeName] =>
            GenerateSettingsTypes(schema, output, ns, typeName),
        ["embed-runtime", var request, var output] => EmbedRuntime(request, output),
        _ => Fail(Usage),
    };
}
catch (SourceException exception)
{
    return Fail($"{exception.Path}: error VSMK: {exception.Message}");
}
catch (Exception exception)
{
    return Fail($"error VSMK: {exception.Message}");
}

static int GenerateSettings(string schemaPath, string modInfoPath, string outputDirectory)
{
    SettingsSchema schema = FromFile(
        schemaPath,
        () => SettingsSchema.Parse(File.ReadAllText(schemaPath))
    );
    string modId = FromFile(
        modInfoPath,
        () =>
            ReadModInfo(modInfoPath)["modid"]?.GetValue<string>()
            ?? throw new ArgumentException("modinfo.json must declare modid.")
    );
    IReadOnlyDictionary<string, string> files = FromFile(
        schemaPath,
        () => IntegrationDescriptors.Generate(schema, modId)
    );
    foreach ((string relative, string contents) in files)
    {
        string output = Path.Combine(outputDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (!File.Exists(output) || File.ReadAllText(output) != contents)
        {
            File.WriteAllText(output, contents);
        }
    }
    Directory.CreateDirectory(outputDirectory);
    File.WriteAllLines(Path.Combine(outputDirectory, "generated-files.txt"), files.Keys);
    return 0;
}

static int GenerateSettingsTypes(
    string schemaPath,
    string outputPath,
    string @namespace,
    string typeName
)
{
    SettingsTypes.ValidateNames(@namespace, typeName);
    string contents = FromFile(
        schemaPath,
        () =>
            SettingsTypes.Generate(
                SettingsSchema.Parse(File.ReadAllText(schemaPath)),
                @namespace,
                typeName
            )
    );
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
    if (!File.Exists(outputPath) || File.ReadAllText(outputPath) != contents)
    {
        File.WriteAllText(outputPath, contents);
    }

    return 0;
}

// Each request line holds tab-separated fields: "file", path, destination and package ID,
// or "embed", "omit" or "lib" followed by one value.
static int EmbedRuntime(string requestPath, string outputDirectory)
{
    List<StagedFile> files = [];
    HashSet<string> embedded = new(StringComparer.OrdinalIgnoreCase);
    HashSet<string> omitted = new(StringComparer.OrdinalIgnoreCase);
    List<string> libraries = [];
    foreach (string line in File.ReadAllLines(requestPath).Where(line => line.Length > 0))
    {
        string[] fields = line.Split('\t');
        switch (fields)
        {
            case ["file", var path, var destination, var packageId]:
                files.Add(new StagedFile(path, destination.Replace('\\', '/'), packageId));
                break;
            case ["embed", var packageId]:
                embedded.Add(packageId);
                break;
            case ["omit", var packageId]:
                omitted.Add(packageId);
                break;
            case ["lib", var directory]:
                libraries.Add(directory);
                break;
            default:
                throw new SourceException(
                    Path.GetFullPath(requestPath),
                    $"Unrecognized request line '{line}'."
                );
        }
    }

    EmbeddingPlan plan = RuntimeEmbedding.Plan(files, embedded, omitted);
    string merged = Path.Combine(outputDirectory, "merged");
    if (Directory.Exists(merged))
    {
        Directory.Delete(merged, true);
    }
    Directory.CreateDirectory(outputDirectory);
    if (plan.Target is not null)
    {
        RuntimeEmbedding.Merge(
            plan.Target,
            plan.Inputs,
            libraries,
            Path.Combine(merged, plan.Target.Destination),
            Path.Combine(outputDirectory, "ilrepack.log"),
            Environment.ProcessPath!,
            Path.Combine(AppContext.BaseDirectory, "ilrepack", "ILRepack.exe")
        );
    }

    File.WriteAllLines(
        Path.Combine(outputDirectory, "excluded.txt"),
        plan.Excluded.Select(file => file.Path)
    );
    File.WriteAllLines(
        Path.Combine(outputDirectory, "merged.txt"),
        plan.Target is null ? [] : [plan.Target.Destination]
    );
    return 0;
}

static JsonObject ReadModInfo(string path) =>
    JsonNode.Parse(File.ReadAllText(path)) as JsonObject
    ?? throw new ArgumentException("modinfo.json must contain an object.");

// MSBuild attributes errors in "<file>: error <code>: <text>" form to that file.
static T FromFile<T>(string path, Func<T> read)
{
    try
    {
        return read();
    }
    catch (Exception exception)
    {
        throw new SourceException(Path.GetFullPath(path), exception.Message);
    }
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

internal sealed class SourceException(string path, string message) : Exception(message)
{
    public string Path { get; } = path;
}
