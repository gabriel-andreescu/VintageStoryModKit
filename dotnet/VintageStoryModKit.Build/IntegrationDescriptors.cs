using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using VintageStoryModKit.Settings;

namespace VintageStoryModKit.Build;

internal static class IntegrationDescriptors
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IReadOnlyDictionary<string, string> Generate(SettingsSchema schema, string modId)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        JsonObject rootMetadata = GetMetadata(schema.Document, "$");
        Dictionary<string, string> descriptors = new(StringComparer.Ordinal);

        if (
            TryGetProviderOverride(
                rootMetadata,
                "configlib",
                "$.x-vsmk.configlib",
                out JsonObject? configLibOverride
            )
        )
        {
            RejectOverrides(configLibOverride, "$.x-vsmk.configlib", "file", "settings", "patches");
            JsonObject configLib = GenerateConfigLib(schema, modId, configLibOverride);
            descriptors[$"assets/{modId}/config/configlib-patches.json"] = configLib.ToJsonString(
                SerializerOptions
            );
        }

        if (
            TryGetProviderOverride(rootMetadata, "imm", "$.x-vsmk.imm", out JsonObject? immOverride)
        )
        {
            RejectOverrides(
                immOverride,
                "$.x-vsmk.imm",
                "ConfigFile",
                "ConfigSide",
                "ConfigSource",
                "Settings"
            );
            JsonObject imm = GenerateImm(schema, modId, immOverride);
            descriptors[$"assets/{modId}/config/imm.json"] = imm.ToJsonString(SerializerOptions);
        }

        return descriptors;
    }

    private static JsonObject GenerateConfigLib(
        SettingsSchema schema,
        string modId,
        JsonObject? rootOverride
    )
    {
        JsonArray settings = new();
        CollectConfigLibSettings(
            GetProperties(schema.Document, "$.properties"),
            schema.Defaults,
            [],
            schema.Side,
            settings
        );

        JsonObject descriptor = new()
        {
            ["version"] = 0,
            ["file"] = schema.ConfigFile(modId),
            ["settings"] = settings,
        };

        Merge(descriptor, rootOverride);
        return descriptor;
    }

    private static JsonObject GenerateImm(
        SettingsSchema schema,
        string modId,
        JsonObject? rootOverride
    )
    {
        JsonArray settings = new();
        CollectImmSettings(
            GetProperties(schema.Document, "$.properties"),
            schema.Defaults,
            [],
            schema.Side,
            settings
        );

        JsonObject block = new()
        {
            ["ConfigFile"] = schema.ConfigFile(modId),
            ["ConfigLabel"] = GetString(schema.Document, "title") ?? modId,
            ["ConfigSource"] = "ModConfig",
            ["ConfigSide"] = schema.Side.ToString(),
            ["Settings"] = settings,
        };

        Merge(block, rootOverride);

        JsonObject descriptor = new() { ["Configuration"] = new JsonArray(block) };
        return descriptor;
    }

    private static void CollectConfigLibSettings(
        JsonObject properties,
        JsonObject defaults,
        IReadOnlyList<string> segments,
        SettingsSide side,
        JsonArray settings
    )
    {
        foreach ((string name, JsonNode? propertyNode) in properties)
        {
            var propertySchema = (JsonObject)propertyNode!;
            JsonNode? defaultValue = defaults[name];
            List<string> propertySegments = [.. segments, name];

            if (
                !TryGetProviderOverride(
                    GetMetadata(propertySchema, SchemaPath(propertySegments)),
                    "configlib",
                    $"{SchemaPath(propertySegments)}.x-vsmk.configlib",
                    out JsonObject? providerOverride
                )
            )
            {
                continue;
            }

            RejectOverrides(
                providerOverride,
                $"{SchemaPath(propertySegments)}.x-vsmk.configlib",
                "clientSide",
                "default"
            );

            if (SettingsSchema.TryGetProperties(propertySchema, out JsonObject? nestedProperties))
            {
                if (providerOverride is not null)
                {
                    throw new ArgumentException(
                        $"{SchemaPath(propertySegments)}.x-vsmk.configlib can only disable a fixed object. Apply native overrides to its leaf properties."
                    );
                }

                if (defaultValue is not JsonObject nestedDefaults)
                {
                    throw new ArgumentException(
                        $"{SchemaPath(propertySegments)} must have an object default."
                    );
                }

                CollectConfigLibSettings(
                    nestedProperties!,
                    nestedDefaults,
                    propertySegments,
                    side,
                    settings
                );
                continue;
            }

            ValidateConfigLibPath(propertySegments, SchemaPath(propertySegments));
            ValidateConfigLibSettingOverride(
                providerOverride,
                $"{SchemaPath(propertySegments)}.x-vsmk.configlib",
                string.Join('/', propertySegments)
            );

            JsonObject setting = new()
            {
                ["code"] = string.Join('/', propertySegments),
                ["type"] = GetConfigLibType(
                    propertySchema,
                    defaultValue,
                    SchemaPath(propertySegments)
                ),
                ["ingui"] = GetTitle(propertySchema, name),
                ["default"] = defaultValue?.DeepClone(),
            };

            string? description = GetString(propertySchema, "description");

            if (!string.IsNullOrWhiteSpace(description))
            {
                setting["comment"] = description;
            }

            if (side == SettingsSide.Client)
            {
                setting["clientSide"] = true;
            }

            AddConfigLibConstraints(setting, propertySchema);
            Merge(setting, providerOverride);
            settings.Add(setting);
        }
    }

    private static void CollectImmSettings(
        JsonObject properties,
        JsonObject defaults,
        IReadOnlyList<string> segments,
        SettingsSide side,
        JsonArray settings
    )
    {
        foreach ((string name, JsonNode? propertyNode) in properties)
        {
            var propertySchema = (JsonObject)propertyNode!;
            List<string> propertySegments = [.. segments, name];

            if (
                !TryGetProviderOverride(
                    GetMetadata(propertySchema, SchemaPath(propertySegments)),
                    "imm",
                    $"{SchemaPath(propertySegments)}.x-vsmk.imm",
                    out JsonObject? providerOverride
                )
            )
            {
                continue;
            }

            RejectOverrides(
                providerOverride,
                $"{SchemaPath(propertySegments)}.x-vsmk.imm",
                "ConfigSide",
                "Map"
            );

            JsonNode? defaultValue = defaults[name];

            if (SettingsSchema.TryGetProperties(propertySchema, out JsonObject? nestedProperties))
            {
                if (providerOverride is not null)
                {
                    throw new ArgumentException(
                        $"{SchemaPath(propertySegments)}.x-vsmk.imm can only disable a fixed object. Apply native overrides to its leaf properties."
                    );
                }

                if (defaultValue is not JsonObject nestedDefaults)
                {
                    throw new ArgumentException(
                        $"{SchemaPath(propertySegments)} must have an object default."
                    );
                }

                CollectImmSettings(
                    nestedProperties!,
                    nestedDefaults,
                    propertySegments,
                    side,
                    settings
                );
                continue;
            }

            JsonObject setting = BuildImmSetting(
                propertySchema,
                defaultValue,
                propertySegments,
                name,
                side
            );
            Merge(setting, providerOverride);
            ValidateImmControl(setting, propertySchema, defaultValue, SchemaPath(propertySegments));
            settings.Add(setting);
        }
    }

    private static JsonObject BuildImmSetting(
        JsonObject propertySchema,
        JsonNode? defaultValue,
        IReadOnlyList<string> segments,
        string name,
        SettingsSide side
    )
    {
        string type = GetImmType(propertySchema, defaultValue, SchemaPath(segments));
        JsonObject setting = new()
        {
            ["Type"] = type,
            ["Label"] = GetTitle(propertySchema, name),
            ["Map"] = ToJPath(segments),
        };

        string? description = GetString(propertySchema, "description");

        if (!string.IsNullOrWhiteSpace(description))
        {
            setting["Description"] = description;
        }

        if (side != SettingsSide.Server)
        {
            setting["ConfigSide"] = side.ToString();
        }

        switch (type)
        {
            case "Slider":
                setting["Min"] = GetNumber(propertySchema, "minimum")!.Value;
                setting["Max"] = GetNumber(propertySchema, "maximum")!.Value;

                if (GetNumber(propertySchema, "multipleOf") is double step)
                {
                    setting["Step"] = step;
                }

                break;
            case "Dropdown":
                setting["Options"] = GetEnumOptions(propertySchema, SchemaPath(segments));
                break;
            case "Array":
                setting["ElementType"] = GetImmArrayElementType(
                    propertySchema,
                    SchemaPath(segments)
                );
                break;
            case "Advanced":
                setting["Advanced"] = ToImmAdvancedSchema(propertySchema, SchemaPath(segments));
                break;
        }

        return setting;
    }

    private static string GetConfigLibType(JsonObject schema, JsonNode? defaultValue, string path)
    {
        return RequireSchemaType(schema, defaultValue, path) switch
        {
            "boolean" => "boolean",
            "integer" => "integer",
            "number" => "float",
            "string" => "string",
            _ => "other",
        };
    }

    private static string GetImmType(JsonObject schema, JsonNode? defaultValue, string path)
    {
        if (schema["enum"] is JsonArray)
        {
            return "Dropdown";
        }

        string type = RequireSchemaType(schema, defaultValue, path);
        return type switch
        {
            "boolean" => "Boolean",
            "integer" when CanRepresentImmSlider(schema, defaultValue) => "Slider",
            "integer" => "Integer",
            "number" when CanRepresentImmSlider(schema, defaultValue) => "Slider",
            "number" => "Decimal",
            "string" => "String",
            "array" when IsPrimitiveArray(schema) => "Array",
            "array" or "object" => "Advanced",
            _ => throw new ArgumentException(
                $"{path} is a {type} setting, which IMM cannot represent. Add an x-vsmk.imm override of false or use a supported JSON Schema type."
            ),
        };
    }

    private static string RequireSchemaType(JsonObject schema, JsonNode? defaultValue, string path)
    {
        return GetSchemaType(schema, defaultValue)
            ?? throw new ArgumentException($"{path} requires a JSON Schema type.");
    }

    private static string? GetSchemaType(JsonObject schema, JsonNode? defaultValue)
    {
        if (
            schema["type"] is JsonValue typeNode
            && typeNode.TryGetValue<string>(out string? type)
            && !string.IsNullOrWhiteSpace(type)
        )
        {
            return type;
        }

        if (schema["type"] is JsonArray types)
        {
            string[] nonNull = types
                .Select(node =>
                    node is JsonValue value && value.TryGetValue(out string? name) ? name : null
                )
                .Where(name => name is not null and not "null")
                .ToArray()!;
            if (nonNull.Length == 1)
            {
                return nonNull[0];
            }
        }

        return defaultValue switch
        {
            JsonObject => "object",
            JsonArray => "array",
            JsonValue value when value.TryGetValue<bool>(out _) => "boolean",
            JsonValue value when value.TryGetValue<int>(out _) || value.TryGetValue<long>(out _) =>
                "integer",
            JsonValue value
                when value.TryGetValue<double>(out _) || value.TryGetValue<decimal>(out _) =>
                "number",
            JsonValue value when value.TryGetValue<string>(out _) => "string",
            _ => null,
        };
    }

    private static void AddConfigLibConstraints(JsonObject setting, JsonObject schema)
    {
        if (schema["enum"] is JsonArray values)
        {
            setting["values"] = values.DeepClone();
        }

        double? minimum = GetNumber(schema, "minimum");
        double? maximum = GetNumber(schema, "maximum");
        double? step = GetNumber(schema, "multipleOf");

        if (minimum is null && maximum is null && step is null)
        {
            return;
        }

        JsonObject range = new();

        if (minimum is double min)
        {
            range["min"] = min;
        }

        if (maximum is double max)
        {
            range["max"] = max;
        }

        if (step is double value)
        {
            range["step"] = value;
        }

        setting["range"] = range;
    }

    private static JsonArray GetEnumOptions(JsonObject schema, string path)
    {
        if (schema["enum"] is not JsonArray values || values.Count == 0)
        {
            throw new ArgumentException(
                $"{path}.enum must contain at least one value for IMM Dropdown."
            );
        }

        JsonArray options = new();

        foreach (JsonNode? value in values)
        {
            if (value is JsonArray or JsonObject || value is null)
            {
                throw new ArgumentException(
                    $"{path}.enum values must be primitive for IMM Dropdown."
                );
            }

            string label =
                value is JsonValue text && text.TryGetValue(out string? name)
                    ? name
                    : value.ToJsonString();
            options.Add(new JsonObject { ["Label"] = label, ["Value"] = value.DeepClone() });
        }

        return options;
    }

    private static string GetImmArrayElementType(JsonObject schema, string path)
    {
        if (schema["items"] is not JsonObject items)
        {
            throw new ArgumentException(
                $"{path}.items must describe a primitive array element for IMM Array."
            );
        }

        return GetSchemaType(items, null) switch
        {
            "boolean" => "Boolean",
            "integer" => "Integer",
            "number" => "Decimal",
            "string" => "String",
            _ => throw new ArgumentException(
                $"{path}.items must be Boolean, Integer, Decimal, or String for IMM Array."
            ),
        };
    }

    private static JsonObject ToImmAdvancedSchema(JsonObject schema, string path)
    {
        string? type = GetSchemaType(schema, null);

        JsonObject result = type switch
        {
            "object" => ToImmAdvancedObject(schema, path),
            "array" => ToImmAdvancedArray(schema, path),
            "boolean" => Leaf("Boolean"),
            "integer" => NumericLeaf(schema, "Integer", path),
            "number" => NumericLeaf(schema, "Decimal", path),
            "string" => StringLeaf(schema, path),
            _ => throw new ArgumentException($"{path} cannot be represented by IMM Advanced."),
        };

        if (schema.TryGetPropertyValue("default", out JsonNode? defaultValue))
        {
            result["InitialValue"] = defaultValue?.DeepClone();
        }

        return result;
    }

    private static JsonObject ToImmAdvancedObject(JsonObject schema, string path)
    {
        if (SettingsSchema.TryGetProperties(schema, out JsonObject? properties))
        {
            if (schema["additionalProperties"] is JsonObject)
            {
                throw new ArgumentException(
                    $"{path} cannot combine fixed properties with typed additionalProperties in IMM Advanced."
                );
            }

            JsonArray fields = new();
            JsonArray required = schema["required"] as JsonArray ?? [];

            foreach ((string name, JsonNode? propertyNode) in properties!)
            {
                string propertyPath = $"{path}.properties[{JsonSerializer.Serialize(name)}]";
                var propertySchema = (JsonObject)propertyNode!;
                JsonObject field = ToImmAdvancedSchema(propertySchema, propertyPath);
                field["Key"] = name;
                field["Label"] = GetTitle(propertySchema, name);

                if (GetString(propertySchema, "description") is string description)
                {
                    field["Description"] = description;
                }

                if (!required.Any(node => node?.GetValue<string>() == name))
                {
                    field["Optional"] = true;
                }

                fields.Add(field);
            }

            return new JsonObject { ["Type"] = "Object", ["Fields"] = fields };
        }

        if (schema["additionalProperties"] is JsonObject valueSchema)
        {
            return new JsonObject
            {
                ["Type"] = "Dictionary",
                ["Value"] = ToImmAdvancedSchema(valueSchema, $"{path}.additionalProperties"),
            };
        }

        throw new ArgumentException(
            $"{path} must declare properties or a typed additionalProperties schema for IMM Advanced."
        );
    }

    private static JsonObject ToImmAdvancedArray(JsonObject schema, string path)
    {
        if (schema["items"] is not JsonObject items)
        {
            throw new ArgumentException(
                $"{path}.items must be a JSON Schema object for IMM Advanced."
            );
        }

        JsonObject result = new()
        {
            ["Type"] = "Array",
            ["Element"] = ToImmAdvancedSchema(items, $"{path}.items"),
        };

        if (GetInteger(schema, "minItems") is int minItems)
        {
            result["MinItems"] = minItems;
        }

        if (GetInteger(schema, "maxItems") is int maxItems)
        {
            result["MaxItems"] = maxItems;
        }

        return result;
    }

    private static JsonObject Leaf(string type) => new() { ["Type"] = type };

    private static JsonObject NumericLeaf(JsonObject schema, string type, string path)
    {
        if (schema["enum"] is JsonArray)
        {
            return new JsonObject
            {
                ["Type"] = "Dropdown",
                ["Options"] = GetEnumOptions(schema, path),
            };
        }

        if (CanRepresentImmSlider(schema, schema["default"]))
        {
            JsonObject slider = new()
            {
                ["Type"] = "Slider",
                ["Min"] = GetNumber(schema, "minimum")!.Value,
                ["Max"] = GetNumber(schema, "maximum")!.Value,
            };

            if (GetNumber(schema, "multipleOf") is double step)
            {
                slider["Step"] = step;
            }

            return slider;
        }

        return Leaf(type);
    }

    private static JsonObject StringLeaf(JsonObject schema, string path)
    {
        return schema["enum"] is JsonArray
            ? new JsonObject { ["Type"] = "Dropdown", ["Options"] = GetEnumOptions(schema, path) }
            : Leaf("String");
    }

    private static bool IsPrimitiveArray(JsonObject schema)
    {
        return schema["items"] is JsonObject items
            && GetSchemaType(items, null) is "boolean" or "integer" or "number" or "string";
    }

    private static bool HasFiniteRange(JsonObject schema)
    {
        return GetNumber(schema, "minimum") is not null && GetNumber(schema, "maximum") is not null;
    }

    private static void ValidateImmControl(
        JsonObject setting,
        JsonObject schema,
        JsonNode? defaultValue,
        string path
    )
    {
        if (GetString(setting, "Type") != "Slider")
        {
            return;
        }

        if (HasFractionalMultipleOf(schema))
        {
            throw new ArgumentException(
                $"{path} has a fractional multipleOf that IMM Slider values cannot match."
            );
        }

        double minimum =
            GetNumber(setting, "Min")
            ?? throw new ArgumentException($"{path} requires Min for an IMM Slider.");
        double maximum =
            GetNumber(setting, "Max")
            ?? throw new ArgumentException($"{path} requires Max for an IMM Slider.");
        double step = GetNumber(setting, "Step") ?? 1;
        string? problem = FindImmSliderProblem(
            minimum,
            maximum,
            step,
            GetSchemaType(schema, defaultValue) == "integer",
            defaultValue
        );

        if (problem is not null)
        {
            throw new ArgumentException($"{path} {problem}");
        }
    }

    private static string? FindImmSliderProblem(
        double minimum,
        double maximum,
        double step,
        bool integer,
        JsonNode? defaultValue
    )
    {
        if (
            !double.IsFinite(minimum)
            || !double.IsFinite(maximum)
            || !double.IsFinite(step)
            || maximum <= minimum
            || step <= 0
        )
        {
            return "has an invalid IMM Slider range.";
        }

        if (integer && (!IsWholeNumber(minimum) || !IsWholeNumber(maximum) || !IsWholeNumber(step)))
        {
            return "requires whole minimum, maximum, and multipleOf values for an IMM integer Slider.";
        }

        double tickCount = (maximum - minimum) / step;

        if (tickCount > 10000 || Math.Abs(tickCount - Math.Round(tickCount)) > 0.000001)
        {
            return "must resolve to no more than 10000 whole IMM Slider intervals.";
        }

        if (defaultValue is JsonValue value && value.TryGetValue<double>(out double number))
        {
            double defaultSteps = (number - minimum) / step;

            if (
                number < minimum
                || number > maximum
                || Math.Abs(defaultSteps - Math.Round(defaultSteps)) > 0.000001
            )
            {
                return "has a default that does not fit the IMM Slider range and step.";
            }
        }

        return null;
    }

    private static bool CanRepresentImmSlider(JsonObject schema, JsonNode? defaultValue)
    {
        if (
            !HasFiniteRange(schema)
            || HasFractionalMultipleOf(schema)
            || (
                GetSchemaType(schema, defaultValue) == "number"
                && GetNumber(schema, "multipleOf") is null
            )
        )
        {
            return false;
        }

        return FindImmSliderProblem(
            GetNumber(schema, "minimum")!.Value,
            GetNumber(schema, "maximum")!.Value,
            GetNumber(schema, "multipleOf") ?? 1,
            GetSchemaType(schema, defaultValue) == "integer",
            defaultValue
        )
            is null;
    }

    // IMM writes Min + tick * Step in binary floating point, so fractional steps miss multipleOf.
    private static bool HasFractionalMultipleOf(JsonObject schema) =>
        GetNumber(schema, "multipleOf") is double step && !IsWholeNumber(step);

    private static bool IsWholeNumber(double value) =>
        Math.Abs(value - Math.Round(value)) <= 0.000001;

    private static JsonObject GetMetadata(JsonObject schema, string path)
    {
        JsonObject result = SettingsSchema.GetMetadata(schema, path);

        if (path != "$" && result.ContainsKey("side"))
        {
            throw new ArgumentException(
                $"{path}.{SettingsSchema.MetadataPropertyName}.side is only valid at the schema root."
            );
        }

        return result;
    }

    private static bool TryGetProviderOverride(
        JsonObject metadata,
        string provider,
        string path,
        out JsonObject? providerOverride
    )
    {
        if (
            !metadata.TryGetPropertyValue(provider, out JsonNode? providerNode)
            || providerNode is null
        )
        {
            providerOverride = null;
            return true;
        }

        if (providerNode is JsonValue value && value.TryGetValue<bool>(out bool enabled))
        {
            providerOverride = null;
            return enabled;
        }

        providerOverride =
            providerNode as JsonObject
            ?? throw new ArgumentException($"{path} must be false or a JSON object.");
        return true;
    }

    private static JsonObject GetProperties(JsonObject schema, string path)
    {
        return schema["properties"] as JsonObject
            ?? throw new ArgumentException($"{path} must be a JSON object.");
    }

    private static string GetTitle(JsonObject schema, string fallback) =>
        GetString(schema, "title") ?? fallback;

    private static string? GetString(JsonObject value, string name)
    {
        return value[name] is JsonValue node && node.TryGetValue<string>(out string? result)
            ? result
            : null;
    }

    private static double? GetNumber(JsonObject value, string name)
    {
        if (value[name] is not JsonValue node)
        {
            return null;
        }

        return node.TryGetValue<double>(out double number) ? number : null;
    }

    private static int? GetInteger(JsonObject value, string name)
    {
        if (value[name] is not JsonValue node)
        {
            return null;
        }

        return node.TryGetValue<int>(out int number) ? number : null;
    }

    // IMM resolves maps with Newtonsoft's SelectToken, which only accepts single-quoted indexers.
    private static string ToJPath(IReadOnlyList<string> segments)
    {
        StringBuilder path = new();

        foreach (string segment in segments)
        {
            if (IsSimpleJPathName(segment))
            {
                path.Append(path.Length == 0 ? segment : $".{segment}");
            }
            else
            {
                path.Append("['")
                    .Append(segment.Replace("\\", "\\\\").Replace("'", "\\'"))
                    .Append("']");
            }
        }

        return path.ToString();
    }

    private static bool IsSimpleJPathName(string value)
    {
        return value.Length > 0
            && (char.IsLetter(value[0]) || value[0] == '_')
            && value.All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static string SchemaPath(IReadOnlyList<string> segments, string? next = null)
    {
        IEnumerable<string> path = next is null ? segments : segments.Append(next);
        return "$.properties"
            + string.Concat(path.Select(segment => $"[{JsonSerializer.Serialize(segment)}]"));
    }

    private static void Merge(JsonObject target, JsonObject? providerOverride)
    {
        if (providerOverride is null)
        {
            return;
        }

        foreach ((string name, JsonNode? value) in providerOverride)
        {
            target[name] = value?.DeepClone();
        }
    }

    private static void ValidateConfigLibPath(IReadOnlyList<string> segments, string path)
    {
        if (segments.Any(segment => !IsConfigLibKey(segment)))
        {
            throw new ArgumentException(
                $"{path} contains a property name that ConfigKit and ConfigLib cannot represent as a literal storage path. Set x-vsmk.configlib to false for this field."
            );
        }
    }

    private static bool IsConfigLibKey(string segment)
    {
        if (
            segment.Length == 0
            || segment.Contains('/')
            || int.TryParse(segment, out _)
            || segment == "-"
            || segment.StartsWith("@@", StringComparison.Ordinal)
            || segment.Count(character => character == '=') == 1
        )
        {
            return false;
        }

        // Both managers parse hyphen ranges before inclusive ranges, with no key escaping.
        string[] range = segment.Contains('-') ? segment.Split('-') : segment.Split("..");
        return range.Length != 2
            || !int.TryParse(range[0], out _)
            || !int.TryParse(range[1], out _);
    }

    private static void ValidateConfigLibSettingOverride(
        JsonObject? providerOverride,
        string path,
        string storagePath
    )
    {
        if (providerOverride is null)
        {
            return;
        }

        foreach (string name in new[] { "code", "name" })
        {
            if (
                providerOverride[name] is JsonValue value
                && value.TryGetValue<string>(out string? overridePath)
                && overridePath != storagePath
            )
            {
                throw new ArgumentException(
                    $"{path}.{name} must remain '{storagePath}' so ConfigLib writes the shared settings file."
                );
            }
        }
    }

    private static void RejectOverrides(
        JsonObject? providerOverride,
        string path,
        params string[] reservedNames
    )
    {
        if (providerOverride is null)
        {
            return;
        }

        foreach (string name in reservedNames)
        {
            if (providerOverride.ContainsKey(name))
            {
                throw new ArgumentException(
                    $"{path}.{name} is owned by the settings schema and cannot be overridden."
                );
            }
        }
    }
}
