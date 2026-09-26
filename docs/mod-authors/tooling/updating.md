# Updating tools and dependencies

| Part                    | Update                                                       | Recorded in                                    |
| ----------------------- | ------------------------------------------------------------ | ---------------------------------------------- |
| Generated project files | `copier update`                                              | `.copier-answers.yml` and the generated files. |
| VSMK XMake addon        | Change the `add_addons` version, then configure              | `xmake.lua` and `xmake-addons.lock`.           |
| VSMK NuGet packages     | Change the `PackageReference` version, then `dotnet restore` | The consuming `.csproj`.                       |
| GitHub Actions          | Change the reusable workflow's release tag                   | `.github/workflows/build.yml`.                 |

Keep `.copier-answers.yml` and `xmake-addons.lock` in Git. Copier merges project
files. It does not reinstall the addon or restore NuGet packages.

## VSMK release

`copier update` moves a generated project to the template's VSMK release,
including the XMake addon, the NuGet packages, the `vintagestorymodkit`
dependency and the workflow tag. It also moves the `game` dependency in
`modinfo.json` and the workflow's `game-version` to the template's Vintage Story
release.

Other projects update these declarations together:

```lua
add_addons("vsmk X.Y.Z")
```

```xml
<PackageReference Include="VintageStoryModKit.Build" Version="X.Y.Z" />
<PackageReference Include="VintageStoryModKit" Version="X.Y.Z" />
```

With the settings runtime, raise the `vintagestorymodkit` dependency in
`modinfo.json` to the same version. Change the `@vX.Y.Z` tag in the
[caller workflow](github-actions.md) as well.

Then update the repository recipes and configure again:

```sh
xmake repo --update
xmake f -y
dotnet restore
xmake package
```

XMake records the selected addon version in `xmake-addons.lock` and keeps
different versions side by side. An exact version remains fixed until its
declaration changes.

Review the project changes and rebuild before publishing the mod.
