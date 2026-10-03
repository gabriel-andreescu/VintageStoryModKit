# VintageStoryModKit

An opinionated toolkit for Vintage Story modding, with project generation, build
and packaging rules, and a settings runtime embedded in each mod.

Use the [project template](docs/mod-authors/template/projects.md), or
[add VSMK to an existing mod](docs/mod-authors/tooling/building.md). See the
[documentation](docs/README.md) for settings, builds and packaging.

## Create a project

Requires [Copier 9](https://copier.readthedocs.io/en/stable/), Git,
[XMake 3.1.1 or newer](https://github.com/xmake-io/xmake/releases/tag/v3.1.1)
and the .NET 10 SDK.

```powershell
copier copy https://github.com/gabriel-andreescu/VintageStoryModKit.git MyMod
```

[First build and project options](docs/mod-authors/template/projects.md)

## Update a project

Requires a clean Git working tree and the project's `.copier-answers.yml`.

```powershell
copier update
```

[Adding settings and updating customized projects](docs/mod-authors/template/projects.md#adding-settings-and-updating)

[Updating build tools and dependencies](docs/mod-authors/tooling/updating.md)

## Development

See [development setup and tests](docs/maintainers/development.md) and
[contribution guidelines](CONTRIBUTING.md).

## License

[MIT](LICENSE)
