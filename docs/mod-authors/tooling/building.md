# Using VSMK in an existing mod

VSMK's build tools require the .NET 10 SDK and a Vintage Story 1.22.7
installation. Its XMake rules require Git and
[XMake 3.1.1](https://github.com/xmake-io/xmake/releases/tag/v3.1.1) or newer.

Copier and its generated project layout are optional.

## .NET setup

Target .NET 10 and add the build package to the mod project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="VintageStoryModKit.Build" Version="X.Y.Z" />
  </ItemGroup>
</Project>
```

The package adds compile-time game references, embeds
`Settings/settings.schema.json` when present and generates settings descriptors
and types before compilation. Game assemblies are not copied to the mod output.
Test projects, marked by `IsTestProject`, copy them so tests can load game
types.

| Property        | Default                                                  | Purpose                                                |
| --------------- | -------------------------------------------------------- | ------------------------------------------------------ |
| `VsmkGamePath`  | `VINTAGE_STORY`                                          | Vintage Story installation used for game references.   |
| `VsmkModInfo`   | Nearest `modinfo.json` in the project directory or above | Mod metadata that supplies the mod ID for descriptors. |
| `VsmkStagePath` | Required by `VsmkStage`                                  | Absolute staging directory.                            |

See [settings](settings.md) for the settings properties and runtime.

To stage the C# output without XMake, invoke the stage target with an absolute
destination:

```sh
dotnet build --target:VsmkStage --property:VsmkStagePath="$PWD/build/stage/MyMod"
```

The target writes `.vsmk-files` beside the staged files. Consumers must use that
manifest rather than scanning the stage directory, because it identifies the
output owned by the current build.

## XMake setup

A complete `xmake.lua`:

```lua
set_xmakever("3.1.1")
set_project("MyMod")
set_policy("package.requires_lock", true)

add_repositories("vsmk https://github.com/gabriel-andreescu/VintageStoryModKit.git")
add_addons("vsmk X.Y.Z")
includes("@addon/vsmk/project")

target("MyMod", function()
    add_rules("@addon/vsmk/mod", { project = "src/MyMod/MyMod.csproj" })
    add_installfiles("$(projectdir)/modinfo.json")
    add_installfiles("$(projectdir)/(assets/**)")
end)
```

The root policy enables XMake's dependency lockfile. `project` declares the
`game_path`, `deploy` and `distdir` options. Without `game_path`, builds use
`VINTAGE_STORY`.

Configure, build and package from the project root:

```sh
xmake f -y --game_path=C:/Games/Vintagestory
xmake
xmake package
```

See [deployment and packaging](packaging.md) for package contents, local
deployment and ZIP output.

## Tests

Register a .NET test project with `xmake test`:

```lua
target("MyMod.Tests", function()
    add_rules("@addon/vsmk/test", { project = "tests/MyMod.Tests/MyMod.Tests.csproj" })
end)
```

| Option          | Default   | Purpose                              |
| --------------- | --------- | ------------------------------------ |
| `project`       | Required  | C# test project.                     |
| `configuration` | `Release` | Build configuration of the test run. |

The target runs `dotnet test` with the configured game path. Plain `xmake`
builds skip it. Set `run-tests: true` in the [build workflow](github-actions.md)
to run `xmake test` before packaging.
