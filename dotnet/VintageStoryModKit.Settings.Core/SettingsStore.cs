using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VintageStoryModKit.Settings;

public sealed class SettingsStore
{
    // Players edit this file, so relaxed escaping keeps apostrophes, markup and non-ASCII text readable.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // JSON Schema accepts 5.0 as an integer, which System.Text.Json refuses for integral types.
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        Converters = { new IntegralConverter<int>(), new IntegralConverter<long>() },
    };
    private readonly SettingsSchema schema;
    private JsonObject current;
    private string? observedText;

    public SettingsStore(SettingsSchema schema, string path)
    {
        this.schema = schema;
        FilePath = Path.GetFullPath(path);
        current = schema.Defaults;
    }

    public string FilePath { get; }
    public JsonObject Current => (JsonObject)current.DeepClone();
    public event Action? Changed;

    public T Get<T>(string path)
    {
        JsonNode? value = current;
        string[] segments = path.StartsWith('/') ? path[1..].Split('/') : [path];
        foreach (string segment in segments)
        {
            string key = segment
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (value is JsonObject obj && obj.TryGetPropertyValue(key, out JsonNode? child))
            {
                value = child;
            }
            else if (
                value is JsonArray array
                && int.TryParse(key, out int index)
                && index >= 0
                && index < array.Count
            )
            {
                value = array[index];
            }
            else
            {
                throw new KeyNotFoundException($"Setting '{path}' does not exist.");
            }
        }
        return value is null
            ? JsonSerializer.Deserialize<T>("null", ReadOptions)!
            : value.Deserialize<T>(ReadOptions)!;
    }

    public bool Reload(bool onlyIfChanged = false)
    {
        if (!File.Exists(FilePath))
        {
            Save(current);
            return false;
        }

        string text = File.ReadAllText(FilePath);
        if (onlyIfChanged && text == observedText)
        {
            return false;
        }

        JsonObject loaded;
        JsonObject merged;
        string mergedText;
        try
        {
            loaded =
                JsonNode.Parse(text, documentOptions: SettingsSchema.DocumentOptions) as JsonObject
                ?? throw new JsonException("Settings must contain a JSON object.");
            merged = schema.ApplyDefaults(loaded);
            schema.Validate(merged);
            // Parsing defers unescaping, so serializing here rejects escapes such as lone surrogates.
            mergedText = Serialize(merged);
        }
        catch (Exception exception)
        {
            observedText = text;
            if (exception is JsonException or SettingsValidationException)
            {
                throw;
            }

            // Malformed values also fail inside the JSON and schema libraries, for example out-of-range numbers.
            throw new JsonException(exception.Message, exception);
        }
        if (!JsonNode.DeepEquals(loaded, merged))
        {
            WriteText(mergedText);
        }
        else
        {
            observedText = text;
        }

        return Adopt(merged);
    }

    public T Snapshot<T>() => current.Deserialize<T>(ReadOptions)!;

    public bool Save(JsonObject value)
    {
        JsonObject merged = schema.ApplyDefaults(value);
        schema.Validate(merged);
        Write(merged);
        return Adopt(merged);
    }

    // Typed values carry only modeled properties, so they merge into the current document to keep unknown fields.
    public bool Save<T>(T values) =>
        Save(
            schema.Merge(
                current,
                JsonSerializer.SerializeToNode(values) as JsonObject
                    ?? throw new ArgumentException(
                        "Typed settings must serialize to a JSON object.",
                        nameof(values)
                    )
            )
        );

    public bool ApplySnapshot(JsonObject value)
    {
        JsonObject merged = schema.ApplyDefaults(value);
        schema.Validate(merged);
        return Adopt(merged);
    }

    private bool Adopt(JsonObject value)
    {
        if (JsonNode.DeepEquals(current, value))
        {
            return false;
        }

        current = (JsonObject)value.DeepClone();
        Changed?.Invoke();
        return true;
    }

    private void Write(JsonObject value) => WriteText(Serialize(value));

    private static string Serialize(JsonObject value) =>
        value.ToJsonString(JsonOptions) + Environment.NewLine;

    private void WriteText(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text);
            File.Move(temporary, FilePath, true);
            observedText = text;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}

internal sealed class IntegralConverter<T> : System.Text.Json.Serialization.JsonConverter<T>
    where T : struct, System.Numerics.IBinaryInteger<T>
{
    public override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        decimal value = reader.GetDecimal();
        if (decimal.Truncate(value) != value)
        {
            throw new JsonException($"{value} is not an integer.");
        }

        try
        {
            return T.CreateChecked(value);
        }
        catch (OverflowException exception)
        {
            throw new JsonException(
                $"{value} is outside the range of {typeof(T).Name}.",
                exception
            );
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(long.CreateChecked(value));
}
