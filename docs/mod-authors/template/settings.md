# Settings

With the `settings` option, the template includes
`src/<project_name>/Settings/settings.schema.json` with an `Enabled` boolean
that defaults to `true` and references the `VintageStoryModKit` runtime, which
the build merges into the mod. VSMK embeds the schema, validates it during the
build and generates the integration descriptors shipped with the mod.

Client-only projects create client-owned settings. Universal and server-only
projects create server-owned settings. Universal mods synchronize their current
values to clients. The config filename defaults to `<modid>.json` under
`ModConfig/`.

The generated mod system opens the embedded schema with
`SettingsHost.Open(api, this)`, applies ready values, reacts to `Changed` and
disposes the host with the mod system. Extend `ApplySettings` to update your
mod's runtime state. It reads the generated `ModSettings` type, which follows
the schema's properties.

Every schema property needs a default. Unknown properties in existing files are
kept.

See the [settings reference](../tooling/settings.md) for `Current`, `Changed`,
`Reload()`, schema metadata, validation, file ownership and generated
integration behavior.
