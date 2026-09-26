# Contributing

See [Development](docs/maintainers/development.md) for the environment,
formatting and tests.

## Repository layout

| Directory    | Responsibility                         |
| ------------ | -------------------------------------- |
| `templates/` | Copier project files.                  |
| `xmake/`     | Addon rules, deployment and packaging. |
| `addons/`    | Addon distribution recipes.            |
| `dotnet/`    | .NET helper packages.                  |

Template documentation belongs in `docs/mod-authors/template/` and links to the
independent rule and helper references in `docs/mod-authors/tooling/`.
Contributor setup and checks belong in `docs/maintainers/`.

## .NET package maintenance

Build and inspect the packages before release:

```powershell
dotnet pack -c Release -o build/nuget
```

Release tags publish the packages to NuGet.org through the `ci.yml` trusted
publishing job. Configure its NuGet.org policy for this repository and workflow,
then set the `NUGET_USER` repository secret to the NuGet.org profile name.

Review dependency license changes before updating packages. Preserve upstream
notices in the NuGet packages and the runtime mod.

`VintageStoryModKit` and `VintageStoryModKit.Settings` run from the shared
`vintagestorymodkit` mod. Mods declare a minimum version, and players install
one copy for all of them. Keep runtime API changes backward compatible. Breaking
changes require dependent mods to update.

## Game updates

For a new Vintage Story release, update `game_version` in `copier.yml` and the
`game` dependency in `dotnet/VintageStoryModKit/modinfo.json`, then search the
docs for the previous version. Recheck the ConfigKit, ConfigLib and IMM versions
in the [settings reference](docs/mod-authors/tooling/settings.md) against the
new release.

## Validate package and rule changes

Install the changed packages in a
[consumer project](docs/mod-authors/tooling/building.md), then build and package
it. Use `build/nuget` as a local NuGet source and an isolated package cache when
rebuilding an unpublished version.

For shared XMake rules, register the local checkout as the consumer's `vsmk`
repository and install it with XMake's `--debugdir` option:

```powershell
$env:XMAKE_GLOBALDIR = Join-Path $PWD ".xmake/development"
xmake repo --add --global vsmk C:/path/to/VintageStoryModKit
xrepo install --addon -y --debugdir=C:/path/to/VintageStoryModKit "vsmk X.Y.Z"
xmake
xmake package
```

Keep that global directory for the development session. The source override
installs uncommitted code under the requested version, so it belongs in an
isolated development cache. Consumer builds install the tagged source instead.

With the local rules installed, package the runtime mod from its directory:

```powershell
cd dotnet/VintageStoryModKit
xmake f -y
xmake package
```

Check generated metadata, deployed files and archive contents as appropriate to
the change. Run the [tests](docs/maintainers/development.md#tests) when changing
helpers, the generator or packaging rules.

## CI and releases

Unreleased work lands on `dev`. `main` tracks the latest release.

[CI](.github/workflows/ci.yml) runs the development checks, builds the packages
and validates a generated consumer.

Keep the XMake version in CI and consumer workflows aligned with
[the development setup](docs/maintainers/development.md#xmake).

For VSMK releases, update:

- `Version` in `Directory.Build.props`.
- The version in `dotnet/VintageStoryModKit/modinfo.json` and the `add_addons`
  pin in `dotnet/VintageStoryModKit/xmake.lua`.
- The addon recipe in `addons/v/vsmk/xmake.lua`. Keep existing recipe versions
  so consumers can continue installing older releases.
- `vsmk_version` in `copier.yml`.
- The `@vX.Y.Z` action reference in `.github/workflows/build.yml`.
- The dated changelog entry.

Merge `dev` into `main` through a pull request without squashing, then publish a
matching `vX.Y.Z` tag on `main`. The addon downloads that tag. After checks
pass, CI publishes the GitHub release with the runtime mod ZIP, then pushes the
NuGet packages. Upload that ZIP to the Vintage Story mod database. Do not move
published release tags.

VSMK and [mod releases](docs/mod-authors/tooling/github-actions.md) share
[release.yml](.github/workflows/release.yml), which extracts notes with
[Changelog Reader](https://github.com/mindsers/changelog-reader-action).
