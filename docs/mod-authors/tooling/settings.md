# Settings reference

`VintageStoryModKit.Settings` loads a mod's JSON settings and generates optional
menus for ConfigKit, ConfigLib and Integrated Mod Manager (IMM). Players can use
a menu or edit the same file directly.

## Reference the runtime

Reference the runtime package next to `VintageStoryModKit.Build`:

```xml
<PackageReference Include="VintageStoryModKit.Settings" Version="X.Y.Z" />
```

Players don't install anything else. Staging merges the runtime and its
dependencies into the mod assembly that uses them, so mods built against
different VSMK versions can be installed together. The runtime's license notices
ship under `licenses/`.

Keep the code that uses VSMK or its dependencies in one assembly of your mod.
Package libraries built on the same dependencies merge along with the runtime.
Staging fails when several of your mod's assemblies reference any of them.

Merged types become internal unless your mod's public API exposes them. An
exposed type stays public as your mod's own copy, so don't expose VSMK types in
an API other mods call.

The runtime leaves out Humanizer, which JsonPointer.Net needs only for its
property name resolvers. Don't call those from your mod. Staging keeps Humanizer
when an assembly of your mod references it.

## Define settings

Add `Settings/settings.schema.json` to your project:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "title": "My Mod",
  "properties": {
    "Enabled": {
      "type": "boolean",
      "title": "Enabled",
      "default": true
    },
    "Multiplier": {
      "type": "number",
      "title": "Multiplier",
      "description": "Adjust the effect strength.",
      "default": 1,
      "minimum": 0,
      "maximum": 5
    }
  }
}
```

Use JSON Schema `properties` for settings and give each value a `default`.
Nested objects collect their children's defaults. Existing player values take
precedence when defaults change. New settings are added without removing unknown
fields.

Describe fields inline with `properties`, `items` and `additionalProperties`.
Settings schemas cannot use `$ref`, `allOf`, `anyOf`, `oneOf`, `not`,
`if`/`then`/`else` or `patternProperties`.

The build embeds the schema and generates the configuration assets, using the
mod ID from the nearest `modinfo.json` in the project directory or above. Do not
maintain copies of the generated menus.

## Load and use settings

Open the helper in `ModSystem.Start` so the JSON file exists before
configuration managers load their assets:

```csharp
using VintageStoryModKit.Settings;

private SettingsHost? settings;

public override void Start(ICoreAPI api)
{
    settings = SettingsHost.Open(api, this);
    settings.Changed += ApplySettings;
    if (settings.IsReady)
    {
        ApplySettings();
    }
}

private void ApplySettings()
{
    ModSettings values = settings!.Snapshot<ModSettings>();
    bool enabled = values.Enabled;
    double multiplier = values.Multiplier;
}

public override void Dispose()
{
    settings?.Dispose();
    settings = null;
    base.Dispose();
}
```

`Snapshot<T>()` returns an independent typed copy of the current values. Keep it
until `Changed` fires rather than reading it for every use. To save from mod
code, change a snapshot with a `with` expression and pass it to `Save` on the
owning side. Saving keeps fields the type does not model, such as unknown
properties.

`Get<T>` reads one value, such as `Get<int>("/Wolves/PopulationCap")`, and
`Current` returns the JSON document for untyped access. Use the host from the
game thread.

The default file is `ModConfig/<modid>.json`. Set `file` in the root `x-vsmk`
object to use another name relative to `ModConfig`. Changes are checked every
two seconds on the game thread and when a supported manager reports a change.
`Reload()` checks immediately. A malformed or invalid file is left untouched.
The helper logs the error and keeps the last valid values, or defaults on the
first load.

## Generated types

The build generates `ModSettings` in the project's root namespace from the
schema. Members use their JSON names for serialization. Defaults fill every
property reached through `properties` from the root, so those members are
required. Members of array items and dictionary values are nullable unless the
schema lists them in `required`.

| Schema                                        | C# type                          |
| --------------------------------------------- | -------------------------------- |
| `boolean`                                     | `bool`                           |
| `integer` with both bounds in the `int` range | `int`                            |
| Other `integer`                               | `long`                           |
| `number`                                      | `double`                         |
| `string`                                      | `string`                         |
| Object with `properties`                      | Nested `<Name>Settings` record   |
| Object with `additionalProperties`            | `IReadOnlyDictionary<string, T>` |
| Array with `items`                            | `IReadOnlyList<T>`               |
| A type combined with `null`                   | Nullable form of that type       |
| Anything else                                 | `JsonNode?`                      |

Property names become PascalCase identifiers, so `server-port` becomes
`ServerPort`. Array items and dictionary values that are objects become
`<Name>Item` and `<Name>Value` records. The build reports names that collide.
Integer members also read integral decimals such as `5.0`.

A shared library can generate the types by pointing `VsmkSettingsSchema` at the
mod's schema. Set an empty `<VsmkModInfo />` property in its project file to
skip the menu descriptors.

| MSBuild property            | Default                         | Purpose                            |
| --------------------------- | ------------------------------- | ---------------------------------- |
| `VsmkSettingsSchema`        | `Settings/settings.schema.json` | Schema to embed and generate from. |
| `VsmkGenerateSettingsTypes` | `true`                          | Generate the types.                |
| `VsmkSettingsTypeName`      | `ModSettings`                   | Root type name.                    |
| `VsmkSettingsNamespace`     | `$(RootNamespace)`              | Namespace of the generated types.  |

## Client and server settings

Settings belong to the server by default. VSMK sends them to clients when they
join and whenever accepted values change. A remote client's local file cannot
override server settings. `IsReady` becomes true after the first valid server
snapshot, and `Changed` fires even when that snapshot equals the defaults.
Server-only mods keep settings on the server and do not open a client channel.

For client settings, add this at the schema root:

```json
"x-vsmk": {
  "side": "Client"
}
```

`side` applies to the whole document. Client settings stay on that client.

A host only receives values from the side that owns them. With client settings
in a Universal mod, the server host never becomes ready. Client-only mods need
client settings and server-only mods need server settings, or
`SettingsHost.Open` throws.

## Settings without the game host

`VintageStoryModKit.Settings.Core` works without `ICoreAPI`, for code that runs
outside a loaded world. Reference it instead of `VintageStoryModKit.Settings`
when that is the only settings code in the assembly. Staging embeds it like the
rest of the runtime, and its types share the `VintageStoryModKit.Settings`
namespace. The build embeds the schema as the manifest resource
`VintageStoryModKit.Settings.Schema`. `SettingsSchema.Parse` reads a schema, and
`SettingsStore` manages one settings file with it:

- `Reload(onlyIfChanged)` reads the file, applies defaults and validates it.
  Invalid files throw `JsonException` or `SettingsValidationException`, stay
  untouched and leave the last valid values in place.
- `Snapshot<T>()`, `Get<T>` and `Current` read the current values.
- `Save` validates and writes values. `Save<T>` keeps fields the type does not
  model.
- `ApplySnapshot` validates values received from elsewhere and adopts them
  without writing the file.
- `Changed` fires when the accepted values change.

The store does not synchronize access. Call it from one thread or guard it with
a lock.

## Optional configuration menus

Mods work without a configuration manager. VSMK generates
`configlib-patches.json` for ConfigKit and ConfigLib, and `imm.json` for IMM.
All use the same settings file.

When several managers are installed, their ownership checks choose the menu.
ConfigLib takes precedence over ConfigKit. IMM yields to either manager for mods
they handle. The descriptors target
[ConfigKit](https://mods.vintagestory.at/configkit) 1.4.1,
[ConfigLib](https://mods.vintagestory.at/configlib) 1.13.2 and
[IMM](https://mods.vintagestory.at/show/mod/66824) 1.1.3 on Vintage Story
1.22.7.

Scalar settings become native controls. Numeric bounds and primitive enums
become ranges and choices. Fixed objects become individual fields. Dynamic
objects and arrays use the managers' available editors. When an IMM slider
cannot represent the range and step, VSMK uses a numeric field. JSON Schema
remains the authority for validation, including constraints a menu cannot
display.

## Menu overrides

Add provider metadata to the setting that needs a different presentation:

```json
"x-vsmk": {
  "configlib": { "ingui": "Effect strength" },
  "imm": { "Label": "Effect strength" }
}
```

The `configlib` entry also applies to ConfigKit because both consume that
descriptor. Set a provider to `false` to leave a value editable only through the
other menus or JSON. At the document root, this disables that provider's
descriptor.

Overrides use the provider's native fields. The schema owns file ownership,
storage paths, defaults and side, and the build rejects overrides of them. If a
provider cannot represent a schema, the build reports the affected path. Disable
that provider for the value or supply a compatible native control.
