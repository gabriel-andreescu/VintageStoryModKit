# Template defaults

See [the template's build choices](projects.md#what-the-template-selects) for
runtime, metadata and packaged-output defaults.

## Editor and Git settings

Projects include `.editorconfig` and `.gitattributes` for consistent
indentation, LF line endings and binary mod files. VS Code recommendations cover
XMake, C#, Lua, CSharpier, StyLua, Prettier and EditorConfig. `.luarc.json`
loads the XMake declarations and plugin from
[xmake-luals](https://github.com/gabriel-andreescu/xmake-luals), which `xmake f`
installs into `.xmake/luals`. The generated editor settings select CSharpier for
C# and XML and enable formatting on save.

## Formatting

Pre-commit formats staged files with:

- **Prettier:** Markdown (`.md`), YAML (`.yaml`, `.yml`) and JSON (`.json`,
  `.jsonc`), using `proseWrap: "always"`.
- **StyLua:** Lua (`.lua`), using four-space indentation.
- **[CSharpier](https://csharpier.com/docs/About):** C# and XML (`.cs`, `.csx`,
  `.csproj`, `.props`, `.targets`, `.slnx`, `.xml`, `.config`). The hook
  restores the pinned .NET tool before formatting.

After initializing the project's Git repository, install the hooks with:

```sh
uv tool install pre-commit
pre-commit install
```

Run formatting on all tracked files with `pre-commit run --all-files`. CI runs
the same hooks and fails if they change files or report errors. Restore and run
CSharpier independently with:

```sh
dotnet tool restore
dotnet csharpier format .
```

Use `dotnet csharpier check .` to check formatting without editing files.

## C# project

`Directory.Build.props` enables nullable reference types, implicit usings, the
SDK's recommended analyzers and warnings as errors, and places .NET build output
under `build/intermediates/dotnet`. Builds also enforce the `.editorconfig` code
style: `var` only where the type is apparent, following
[Microsoft's conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions#implicitly-typed-local-variables),
and braces on every block. `dotnet format style` and `dotnet format analyzers`
can apply available code fixes. CSharpier owns whitespace formatting.
`global.json` selects the .NET 10 SDK, which the mod project also targets. A
root `.slnx` solution lists the C# projects.

See [build integration](../tooling/building.md) for the MSBuild properties and
targets.

## GitHub Actions

The generated workflow calls VSMK's
[reusable build workflow](../tooling/github-actions.md) on pushes to `main` and
`dev`, pull requests and manual runs. Version tags also publish a GitHub
release.
