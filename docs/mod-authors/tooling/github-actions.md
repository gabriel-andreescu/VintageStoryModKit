# GitHub Actions

VSMK's reusable [build workflow](../../../.github/workflows/build.yml) builds
the mod and uploads its ZIPs as Actions artifacts. Tag pushes also publish a
GitHub release. Generated projects include the caller workflow.

## Workflow setup

Add `.github/workflows/build.yml`:

```yaml
name: Build and release

on:
  push:
    branches: [main]
    tags: ["v[0-9]*", "[0-9]*"]
  pull_request:
  workflow_dispatch:

permissions:
  contents: read

jobs:
  build:
    permissions:
      contents: write
    uses: gabriel-andreescu/VintageStoryModKit/.github/workflows/build.yml@vX.Y.Z
    with:
      game-version: "1.22.7"
```

Select the VSMK release used by the project. See [updating](updating.md) for the
separate workflow, addon and dependency pins.

| Input                 | Default        | Purpose                                                  |
| --------------------- | -------------- | -------------------------------------------------------- |
| `project-directory`   | `.`            | Directory containing `xmake.lua` and `CHANGELOG.md`.     |
| `modinfo`             | `modinfo.json` | Primary mod metadata, relative to the project directory. |
| `game-version`        | Required       | Game release used for build references.                  |
| `run-tests`           | `false`        | Run [`xmake test`](building.md#tests) before packaging.  |
| `dist-directory`      | `build/dist`   | ZIP output directory relative to the project.            |
| `configure-arguments` | Empty          | Additional XMake configure arguments, one per line.      |

Builds use Linux, the project's .NET SDK and the
[XMake build](https://github.com/gabriel-andreescu/xmake) pinned as
`XMAKE_COMMIT` in the workflow. Game references come from the official Linux
server distribution for `game-version`, independent of the mod's `modinfo.json`
dependencies. Deployment is disabled.

If the repository has a root `.pre-commit-config.yaml`, the workflow runs its
checks after building, so hooks can use dependencies the build installs. NuGet
packages and pre-commit environments are cached between runs.

## Releases

Push an `X.Y.Z` or `vX.Y.Z` tag matching the primary mod version and a dated
`## [X.Y.Z] - YYYY-MM-DD` entry in `CHANGELOG.md`. The workflow attaches all
built ZIPs and uses that entry as the release notes. Other mod targets use the
version in their own `modinfo.json`.

Actions artifacts retain the target folders. In GitHub releases, duplicate ZIP
filenames receive the target path as a prefix, with directory separators
replaced by hyphens. The target name is omitted from the prefix when the ZIP
already starts with `<target>-`. Unique ZIP filenames remain unchanged.

Missing, empty or undated entries block publication. Existing releases are not
overwritten.

### Target changelogs

Targets use the root release entry unless they declare a separate changelog:

```lua
target("MyModCompat", function()
    add_rules("@addon/vsmk/mod", {
        project = "src/MyModCompat/MyModCompat.csproj",
        changelog = "src/MyModCompat/CHANGELOG.md"
    })
    add_installfiles("src/MyModCompat/modinfo.json")
end)
```

The declared changelog supplies the entry matching that mod's version. Paths are
relative to the project root. The release body starts with the root entry,
followed by target entries under package-name and version headings. Targets
sharing a changelog entry share one section. A missing target entry blocks
publication.

### Custom builds

Call [release.yml](../../../.github/workflows/release.yml) after your own build
job, from a workflow that runs on tag pushes, to reuse publication without the
build workflow. Pass `artifact` with the Actions artifact containing the ZIPs,
and `metadata` when uploading XMake's `.xmake/vsmk/packages/*.json` records.
`project-directory` and `changelog` locate the root changelog, and
`expected-version` requires the tag to match. Leave `artifact` empty for a
source release.
