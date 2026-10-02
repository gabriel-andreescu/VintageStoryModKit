# Documentation

## Mod authors

Start with [the project template](mod-authors/template/projects.md) for a
generated mod, or [use VSMK in an existing mod](mod-authors/tooling/building.md)
to add the NuGet packages and build rules yourself.

### Project template

| Guide                                                 | Covers                                                  |
| ----------------------------------------------------- | ------------------------------------------------------- |
| [Projects](mod-authors/template/projects.md)          | Copier options, first build and project updates.        |
| [Template defaults](mod-authors/template/defaults.md) | Generated editor, formatting and project configuration. |
| [Settings](mod-authors/template/settings.md)          | Generated schema and runtime settings access.           |

### Build tools and helpers

| Guide                                                            | Covers                                                   |
| ---------------------------------------------------------------- | -------------------------------------------------------- |
| [Using VSMK in an existing mod](mod-authors/tooling/building.md) | NuGet and XMake integration without the template.        |
| [Settings reference](mod-authors/tooling/settings.md)            | Schema metadata, runtime API, generated types and menus. |
| [Deployment and packaging](mod-authors/tooling/packaging.md)     | Staged contents, local deployment and ZIPs.              |
| [GitHub Actions](mod-authors/tooling/github-actions.md)          | Mod builds and releases.                                 |
| [Updating](mod-authors/tooling/updating.md)                      | Copier, addon, NuGet package and workflow updates.       |

## VSMK maintainers

- [Development](maintainers/development.md): environment, formatting and tests.
- [Contributing](../CONTRIBUTING.md): repository layout, package maintenance and
  VSMK releases.
