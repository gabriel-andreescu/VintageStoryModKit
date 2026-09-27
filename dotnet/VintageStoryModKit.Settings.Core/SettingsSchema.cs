using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace VintageStoryModKit.Settings;

public sealed class SettingsSchema
{
    internal const string MetadataPropertyName = "x-vsmk";

    // Duplicate keys otherwise parse and fail later as ArgumentException, outside JSON error handling.
    internal static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowDuplicateProperties = false,
    };
    private readonly JsonObject document;
    private readonly JsonObject defaults;
    private readonly JsonSchema validator;

    private SettingsSchema(
        JsonObject document,
        JsonSchema validator,
        JsonObject defaults,
        SettingsSide side,
        string? file
    )
    {
        this.document = document;
        this.validator = validator;
        this.defaults = defaults;
        Side = side;
        File = file;
    }

    public JsonObject Defaults => (JsonObject)defaults.DeepClone();

    public SettingsSide Side { get; }

    public string? File { get; }

    internal JsonObject Document => document;

    public static SettingsSchema Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(json, documentOptions: DocumentOptions);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                $"The settings schema is not valid JSON. {exception.Message}",
                exception
            );
        }

        if (parsed is not JsonObject document)
        {
            throw new ArgumentException("The settings schema root must be a JSON object.");
        }

        JsonObject metadata = GetMetadata(document, "$");
        SettingsSide side = GetSide(metadata, "$.x-vsmk.side");
        string? file = GetOptionalFile(metadata, "$.x-vsmk.file");
        ValidateModeledSchemas(document, "$");
        JsonObject defaults = BuildDefaults(document, "$");

        JsonSchema validator;

        try
        {
            var validationDocument = (JsonObject)document.DeepClone();
            RemoveVsmkMetadata(validationDocument);
            validator =
                validationDocument.Deserialize<JsonSchema>()
                ?? throw new JsonException("The settings document is empty.");
        }
        catch (Exception exception)
        {
            throw new ArgumentException(
                $"The settings document is not a valid JSON Schema. {exception.Message}",
                exception
            );
        }

        SettingsSchema schema = new(
            (JsonObject)document.DeepClone(),
            validator,
            defaults,
            side,
            file
        );
        schema.Validate(defaults);
        return schema;
    }

    public string ConfigFile(string modId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        return File ?? $"{modId}.json";
    }

    public void Validate(JsonObject value)
    {
        ArgumentNullException.ThrowIfNull(value);

        EvaluationResults result = validator.Evaluate(
            value,
            new EvaluationOptions { OutputFormat = OutputFormat.List }
        );

        if (!result.IsValid)
        {
            throw new SettingsValidationException(FormatValidationError(result));
        }
    }

    public JsonObject ApplyDefaults(JsonObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ApplyDefaults(document, value, defaults);
    }

    internal JsonObject Merge(JsonObject current, JsonObject changes) =>
        Merge(document, current, changes);

    private static JsonObject Merge(JsonObject schema, JsonObject current, JsonObject changes)
    {
        var result = (JsonObject)current.DeepClone();
        TryGetProperties(schema, out JsonObject? properties);

        foreach ((string name, JsonNode? value) in changes)
        {
            if (
                properties?[name] is JsonObject propertySchema
                && TryGetProperties(propertySchema, out _)
                && value is JsonObject changedObject
                && result[name] is JsonObject currentObject
            )
            {
                result[name] = Merge(propertySchema, currentObject, changedObject);
            }
            else
            {
                result[name] = value?.DeepClone();
            }
        }

        return result;
    }

    internal static JsonObject GetMetadata(JsonObject document, string path)
    {
        if (!document.TryGetPropertyValue(MetadataPropertyName, out JsonNode? node) || node is null)
        {
            return new JsonObject();
        }

        return node as JsonObject
            ?? throw new ArgumentException($"{path}.{MetadataPropertyName} must be a JSON object.");
    }

    private static SettingsSide GetSide(JsonObject metadata, string path)
    {
        if (!metadata.TryGetPropertyValue("side", out JsonNode? node) || node is null)
        {
            return SettingsSide.Server;
        }

        return node is JsonValue value && value.TryGetValue(out string? side)
            ? side switch
            {
                "Server" => SettingsSide.Server,
                "Client" => SettingsSide.Client,
                _ => throw new ArgumentException($"{path} must be 'Server' or 'Client'."),
            }
            : throw new ArgumentException($"{path} must be 'Server' or 'Client'.");
    }

    private static string? GetOptionalFile(JsonObject metadata, string path)
    {
        if (!metadata.TryGetPropertyValue("file", out JsonNode? node) || node is null)
        {
            return null;
        }

        if (
            node is not JsonValue value
            || !value.TryGetValue(out string? file)
            || string.IsNullOrWhiteSpace(file)
            || Path.IsPathRooted(file)
            || file.Contains(':')
            || file.Split('/', '\\').Any(segment => segment is "." or "..")
            || !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new ArgumentException($"{path} must be a relative JSON filename.");
        }

        return file.Replace('\\', '/');
    }

    private static JsonObject BuildDefaults(JsonObject document, string path)
    {
        RequireObjectSchema(document, path);

        if (
            !document.TryGetPropertyValue("properties", out JsonNode? propertiesNode)
            || propertiesNode is null
        )
        {
            throw new ArgumentException($"{path}.properties is required for a settings schema.");
        }

        if (propertiesNode is not JsonObject properties)
        {
            throw new ArgumentException($"{path}.properties must be a JSON object.");
        }

        return BuildObjectDefaults(properties, path);
    }

    private static bool TryBuildDefault(JsonObject schema, string path, out JsonNode? value)
    {
        if (schema.TryGetPropertyValue("default", out JsonNode? declaredDefault))
        {
            value = declaredDefault?.DeepClone();

            if (
                value is JsonObject declaredObject
                && TryGetProperties(schema, out JsonObject? properties)
            )
            {
                value = ApplyDefaults(
                    schema,
                    declaredObject,
                    BuildObjectDefaults(properties!, path)
                );
            }

            return true;
        }

        if (TryGetProperties(schema, out JsonObject? objectProperties))
        {
            value = BuildObjectDefaults(objectProperties!, path);
            return true;
        }

        value = null;
        return false;
    }

    private static JsonObject BuildObjectDefaults(JsonObject properties, string path)
    {
        JsonObject defaults = new();

        foreach ((string name, JsonNode? propertyNode) in properties)
        {
            if (
                !TryBuildDefault(
                    (JsonObject)propertyNode!,
                    PropertyPath(path, name),
                    out JsonNode? propertyDefault
                )
            )
            {
                throw new ArgumentException(
                    $"{PropertyPath(path, name)} requires a default value."
                );
            }

            defaults[name] = propertyDefault;
        }

        return defaults;
    }

    private static JsonObject ApplyDefaults(
        JsonObject schema,
        JsonObject value,
        JsonObject defaults
    )
    {
        var result = (JsonObject)value.DeepClone();

        if (!TryGetProperties(schema, out JsonObject? properties))
        {
            return result;
        }

        foreach ((string name, JsonNode? propertySchemaNode) in properties!)
        {
            if (propertySchemaNode is not JsonObject propertySchema)
            {
                continue;
            }

            if (!result.TryGetPropertyValue(name, out JsonNode? existing))
            {
                result[name] = defaults[name]?.DeepClone();
                continue;
            }

            if (
                existing is JsonObject existingObject
                && defaults[name] is JsonObject nestedDefaults
            )
            {
                result[name] = ApplyDefaults(propertySchema, existingObject, nestedDefaults);
            }
        }

        return result;
    }

    internal static bool TryGetProperties(JsonObject schema, out JsonObject? properties)
    {
        if (
            schema.TryGetPropertyValue("properties", out JsonNode? node)
            && node is JsonObject objectProperties
        )
        {
            properties = objectProperties;
            return true;
        }

        properties = null;
        return false;
    }

    private static void ValidateModeledSchemas(JsonObject schema, string path)
    {
        string[] unsupportedKeywords =
        [
            "$ref",
            "allOf",
            "anyOf",
            "oneOf",
            "not",
            "if",
            "then",
            "else",
            "patternProperties",
        ];

        foreach (string keyword in unsupportedKeywords)
        {
            if (schema.ContainsKey(keyword))
            {
                throw new ArgumentException(
                    $"{path}.{keyword} is not supported in a VSMK settings definition. Inline the property schema."
                );
            }
        }

        if (TryGetProperties(schema, out JsonObject? properties))
        {
            foreach ((string name, JsonNode? property) in properties!)
            {
                if (property is not JsonObject propertySchema)
                {
                    throw new ArgumentException(
                        $"{PropertyPath(path, name)} must be a JSON Schema object."
                    );
                }

                ValidateModeledSchemas(propertySchema, PropertyPath(path, name));
            }
        }

        if (schema["items"] is JsonObject itemSchema)
        {
            ValidateModeledSchemas(itemSchema, $"{path}.items");
        }

        if (schema["additionalProperties"] is JsonObject additionalProperties)
        {
            ValidateModeledSchemas(additionalProperties, $"{path}.additionalProperties");
        }
    }

    private static void RemoveVsmkMetadata(JsonObject schema)
    {
        schema.Remove(MetadataPropertyName);

        if (TryGetProperties(schema, out JsonObject? properties))
        {
            foreach (JsonNode? property in properties!.Select(pair => pair.Value))
            {
                if (property is JsonObject propertySchema)
                {
                    RemoveVsmkMetadata(propertySchema);
                }
            }
        }

        if (schema["items"] is JsonObject itemSchema)
        {
            RemoveVsmkMetadata(itemSchema);
        }

        if (schema["additionalProperties"] is JsonObject additionalProperties)
        {
            RemoveVsmkMetadata(additionalProperties);
        }
    }

    private static void RequireObjectSchema(JsonObject schema, string path)
    {
        if (
            schema["type"] is not JsonValue typeNode
            || !typeNode.TryGetValue(out string? type)
            || type != "object"
        )
        {
            throw new ArgumentException($"{path}.type must be 'object'.");
        }
    }

    private static string PropertyPath(string parent, string name) =>
        $"{parent}.properties[{JsonSerializer.Serialize(name)}]";

    private static string FormatValidationError(EvaluationResults result)
    {
        string[] failures = (result.Details ?? [])
            .Prepend(result)
            .Where(detail => detail.Errors is { Count: > 0 })
            .Select(detail =>
                $"{(detail.InstanceLocation.Count == 0 ? "/" : detail.InstanceLocation.ToString())}: {string.Join(", ", detail.Errors!.Values)}"
            )
            .Distinct()
            .ToArray();

        return failures.Length > 0
            ? $"Settings validation failed at {string.Join(" | ", failures)}"
            : "Settings validation failed: the values do not satisfy the settings schema.";
    }
}

public sealed class SettingsValidationException : Exception
{
    public SettingsValidationException(string message)
        : base(message) { }
}
