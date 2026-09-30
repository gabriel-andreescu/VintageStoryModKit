# VSMK development

Requires Python 3.11+, [uv](https://docs.astral.sh/uv/) and the .NET 10 SDK.
From the VSMK root:

```powershell
uv sync --locked
dotnet tool restore
uv run pre-commit install
```

## Formatting and lint

The [commit hooks](../../.pre-commit-config.yaml) format staged files with
Prettier, StyLua, CSharpier and Ruff, and apply Ruff lint fixes. Prettier uses
[`proseWrap: "always"`](https://prettier.io/docs/options#prose-wrap). StyLua
uses four-space indentation and verifies its output. Run the hooks on all
tracked files with:

```powershell
uv run pre-commit run --all-files
```

Builds enforce the C# code style in `.editorconfig`: `var` only where the type
is apparent, and braces on every block.

CI runs the same checks and validates workflows with
[actionlint](https://github.com/rhysd/actionlint).

## XMake

Requires [XMake 3.1.1](https://github.com/xmake-io/xmake/releases/tag/v3.1.1).

## Tests

Set `VINTAGE_STORY` to a Vintage Story 1.22.7 installation, then run:

```powershell
dotnet build
dotnet run --project dotnet/VintageStoryModKit.Tests
```

These cover schema validation, generated descriptors, settings persistence and
the game lifecycle boundaries.

The generated-project integration check also requires Git, PowerShell 7, uv and
XMake:

```powershell
pwsh -NoProfile -File tests/integration/Test-Template.ps1
```

It covers generation, the generated formatting hooks, customized Copier updates,
installed packages, package composition, local deployment, test registration and
loading two mods built against different VSMK builds on the dedicated server,
using disposable projects. `VSMK_TEST_ROOT` selects the temporary directory for
these and the unit tests.

The workflow integration tests require PowerShell 7:

```powershell
uv run pytest tests/integration/workflows
```

These exercise the workflow scripts without publishing releases.

## Mods

Generate a mod from the local checkout, then validate the installed packages and
addon through it as described in
[Contributing](../../CONTRIBUTING.md#validate-package-and-rule-changes):

```powershell
uv run copier copy --defaults --vcs-ref HEAD C:/path/to/VintageStoryModKit scratch/MyMod
```

`--vcs-ref HEAD` selects the checkout instead of the latest release tag, and
includes uncommitted changes. Keep generated projects under the ignored
`scratch/` directory.
