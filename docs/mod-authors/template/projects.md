# Projects

Copier creates a C# code mod for Vintage Story 1.22.7. The project uses VSMK's
NuGet packages for game references, settings and staging, and its XMake addon
for packaging and optional deployment.

## First build

Requires Copier, Git, XMake 3.1.1 or newer, the .NET 10 SDK and a Vintage Story
1.22.7 installation.

```powershell
copier copy --defaults -d project_name=MyMod https://github.com/gabriel-andreescu/VintageStoryModKit.git MyMod
cd MyMod
xmake f -y --game_path=C:/Games/Vintagestory
xmake package
```

The ZIP at `build/dist/MyMod/mymod-0.1.0.zip` contains `modinfo.json`,
`MyMod.dll` and the generated menu descriptors under `assets/mymod/config/`.

To deploy the same files while building, create `.xmake/vsmk/deploy.json`:

```json
{
  "MyMod": ["C:/Users/Me/AppData/Roaming/VintagestoryData/Mods/MyMod"]
}
```

Run `xmake` and check the destination's `MyMod.dll`. See
[deployment](../tooling/packaging.md#deployment) for ownership and cleanup
behavior.

## Build a generated project

The generated `xmake.lua` registers VSMK and points its mod target at the C#
project. From the project root:

```sh
xmake f -y
xmake
xmake package
```

`VINTAGE_STORY` supplies the game path when `--game_path` is unset. `xmake`
builds and stages the mod. `xmake package` writes the ZIP under
`build/dist/<target>/`. See [deployment and packaging](../tooling/packaging.md)
for output selection and [template defaults](defaults.md) for formatting and
editor configuration.

Plain `dotnet build` also compiles the mod and generates the settings
integration files.

## What the template selects

- The C# project targets .NET 10 and references `VintageStoryModKit.Build`. With
  settings, it also references the `VintageStoryModKit.Settings` runtime.
- The project root mirrors the mod ZIP. `modinfo.json` owns the mod ID, name and
  version. It starts at version 0.1.0, requires game 1.22.7 and takes its side
  from the `side` answer.
- With settings, `src/<project_name>/Settings/settings.schema.json` is embedded
  as `VintageStoryModKit.Settings.Schema` and its generated descriptors enter
  the staged payload.

See [template defaults](defaults.md) for the generated configuration and
[settings](settings.md) for the runtime API.

## Options

Pass answers with `-d name=value`, or use the interactive prompts.

| Answer                  | Default        | Purpose                                                              |
| ----------------------- | -------------- | -------------------------------------------------------------------- |
| `project_name`          | `MyMod`        | Project name, mod name, C# root namespace and mod-system class stem. |
| `mod_id`                | Lowercase name | Vintage Story mod ID and default settings filename.                  |
| `side`                  | `Universal`    | `Universal`, `Client` or `Server` mod metadata.                      |
| `settings`              | `true`         | Include [settings](settings.md) and configuration menus.             |
| `description`, `author` | Empty          | `modinfo.json` metadata.                                             |
| `pre_commit`            | `true`         | Include [formatting hooks](defaults.md#formatting).                  |
| `deploy`                | Empty          | Initial deployment destinations, separated by `;`. Stored locally.   |

Put code in `src/<project_name>/` and files to include unchanged in `assets/`.
Add `modicon.png` beside `modinfo.json` and declare it with `add_installfiles`
in `xmake.lua`.

## Adding settings and updating

Keep `.copier-answers.yml` in Git. From a clean working tree:

```sh
copier update
```

To add settings to a project created without them:

```sh
copier update -d settings=true
```

Copier merges template changes with project edits. Review any merge conflicts.
It updates project files but does not update installed tools or restore NuGet
packages. Use the separate
[tool and dependency update commands](../tooling/updating.md) for those changes.
