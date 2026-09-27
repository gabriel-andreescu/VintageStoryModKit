[CmdletBinding()]
param(
    [string] $GamePath = $env:VINTAGE_STORY,
    [string] $WorkRoot = $env:VSMK_TEST_ROOT
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$version = (Select-Xml -Path (Join-Path $root "Directory.Build.props") -XPath "//VersionPrefix").Node.InnerText
if ([string]::IsNullOrWhiteSpace($GamePath) -or -not (Test-Path (Join-Path $GamePath "VintagestoryAPI.dll"))) {
    throw "Pass -GamePath or set VINTAGE_STORY to a Vintage Story installation."
}
if ([string]::IsNullOrWhiteSpace($WorkRoot)) {
    $WorkRoot = Join-Path $root "scratch"
}
$scratchRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($WorkRoot))
New-Item -ItemType Directory -Force -Path $scratchRoot | Out-Null
$work = [IO.Path]::GetFullPath((Join-Path $scratchRoot ("template-" + [Guid]::NewGuid().ToString("N"))))
if (-not $work.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Test work directory must remain under $scratchRoot."
}
$template = Join-Path $work "template"
$kit = Join-Path $work "kit"
$consumer = Join-Path $work "consumer"
$clientConsumer = Join-Path $work "client-consumer"
$plainConsumer = Join-Path $work "plain-consumer"
$otherConsumer = Join-Path $work "other-consumer"
$server = Join-Path $work "server"
$feed = Join-Path $work "feed"
$deployment = Join-Path $work "deployment"
$previousNugetPackages = $env:NUGET_PACKAGES
$previousUvCache = $env:UV_CACHE_DIR
$previousXmakeGlobal = $env:XMAKE_GLOBALDIR

function Invoke-External {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(ValueFromRemainingArguments)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Invoke-ExternalFailure {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(ValueFromRemainingArguments)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -eq 0) {
        throw "$FilePath unexpectedly succeeded."
    }
}

function Assert-EqualFiles {
    param(
        [Parameter(Mandatory)]
        [string[]] $Actual,

        [Parameter(Mandatory)]
        [string[]] $Expected,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $difference = Compare-Object ($Expected | Sort-Object) ($Actual | Sort-Object)
    if ($difference) {
        throw "$Description contains unexpected files:`n$($difference | Out-String)"
    }
}

function Copy-SourceTree {
    param(
        [Parameter(Mandatory)]
        [string] $Source,

        [Parameter(Mandatory)]
        [string] $Destination
    )

    $files = git -C $Source ls-files --cached --others --exclude-standard
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list the files under $Source."
    }
    foreach ($relative in $files) {
        $file = Join-Path $Source $relative
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            $output = Join-Path $Destination $relative
            New-Item -ItemType Directory -Force -Path (Split-Path $output) | Out-Null
            Copy-Item -LiteralPath $file $output
        }
    }
}

function Restore-EnvironmentVariable {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [AllowNull()]
        [string] $Value
    )

    if ($null -eq $Value) {
        Remove-Item "Env:$Name" -ErrorAction SilentlyContinue
    }
    else {
        Set-Item "Env:$Name" $Value
    }
}

New-Item -ItemType Directory -Force -Path $template, $kit, $feed | Out-Null

try {
    $env:NUGET_PACKAGES = Join-Path $work "nuget-packages"
    $env:UV_CACHE_DIR = Join-Path $work "uv-cache"
    $env:XMAKE_GLOBALDIR = Join-Path $work "xmake-global"

    Copy-Item (Join-Path $root "copier.yml") $template
    Copy-Item (Join-Path $root "templates") $template -Recurse
    Invoke-External git -C $template init
    Invoke-External git -C $template config user.name "VSMK tests"
    Invoke-External git -C $template config user.email "vsmk-tests@example.invalid"
    Invoke-External git -C $template add .
    Invoke-External git -C $template commit -m "template v0.1.0"
    Invoke-External git -C $template tag v0.1.0

    Invoke-External uv run --project $root copier copy --trust --defaults --vcs-ref v0.1.0 `
        --data "vsmk_repository=$($root.Replace('\', '/'))" $template $consumer
    $serverSchema = Get-Content -Raw (Join-Path $consumer "src/MyMod/Settings/settings.schema.json") | ConvertFrom-Json
    if ($serverSchema.'x-vsmk'.side -ne "Server") {
        throw "The default template did not render server-owned settings."
    }
    Invoke-External uv run --project $root copier copy --trust --defaults --vcs-ref v0.1.0 `
        --data "vsmk_repository=$($root.Replace('\', '/'))" --data side=Client `
        --data "deploy=$deployment" $template $clientConsumer
    $clientSchema = Get-Content -Raw (Join-Path $clientConsumer "src/MyMod/Settings/settings.schema.json") | ConvertFrom-Json
    $clientModInfo = Get-Content -Raw (Join-Path $clientConsumer "modinfo.json") | ConvertFrom-Json
    if ($clientSchema.'x-vsmk'.side -ne "Client" -or $clientModInfo.side -ne "Client") {
        throw "The client template did not render client-owned settings and metadata."
    }
    $clientDeployment = Get-Content -Raw (Join-Path $clientConsumer ".xmake/vsmk/deploy.json") | ConvertFrom-Json
    if (@($clientDeployment.MyMod) -join "|" -ne $deployment -or
        (Get-Content -Raw (Join-Path $clientConsumer ".copier-answers.yml")).Contains("deploy")) {
        throw "The template did not keep the deployment destination local."
    }

    Invoke-External git -C $consumer init
    Invoke-External git -C $consumer config user.name "VSMK tests"
    Invoke-External git -C $consumer config user.email "vsmk-tests@example.invalid"
    Invoke-External git -C $consumer add .
    Push-Location $consumer
    try {
        Invoke-External uv tool run --from pre-commit==4.6.2 pre-commit run --all-files --show-diff-on-failure
    }
    finally {
        Pop-Location
    }
    Add-Content (Join-Path $consumer "README.md") "`nConsumer customization."
    Invoke-External git -C $consumer add .
    Invoke-External git -C $consumer commit -m "customize generated project"

    Add-Content (Join-Path $template "templates/README.md.jinja") "`nTemplate update marker."
    Invoke-External git -C $template add .
    Invoke-External git -C $template commit -m "template v0.1.1"
    Invoke-External git -C $template tag v0.1.1

    Push-Location $consumer
    try {
        Invoke-External uv run --project $root copier update --trust --defaults --vcs-ref v0.1.1
    }
    finally {
        Pop-Location
    }

    $readme = Get-Content -Raw (Join-Path $consumer "README.md")
    if (-not $readme.Contains("Consumer customization.") -or -not $readme.Contains("Template update marker.")) {
        throw "Copier update did not preserve the customization and apply the template update."
    }

    foreach ($name in @("Directory.Build.props", "global.json", "LICENSE", "README.md")) {
        Copy-Item (Join-Path $root $name) $kit
    }
    Copy-SourceTree (Join-Path $root "dotnet") (Join-Path $kit "dotnet")
    Copy-SourceTree (Join-Path $root "licenses") (Join-Path $kit "licenses")
    Copy-SourceTree (Join-Path $root "addons") (Join-Path $kit "addons")
    Copy-SourceTree (Join-Path $root "xmake") (Join-Path $kit "xmake")
    Copy-Item (Join-Path $root "addon.lua") $kit
    foreach ($project in @("VintageStoryModKit.Settings", "VintageStoryModKit", "VintageStoryModKit.Build")) {
        Invoke-External dotnet pack (Join-Path $kit "dotnet/$project/$project.csproj") `
            --configuration Release --output $feed "--property:VsmkGamePath=$GamePath"
    }

    Push-Location $consumer
    try {
        Invoke-External dotnet new nugetconfig --force
        Invoke-External dotnet nuget add source $feed --name vsmk-local --configfile nuget.config
        Invoke-External xmake repo --add --global vsmk $root
        Invoke-External xrepo install --addon -y "--debugdir=$root" "vsmk $version"
        Invoke-External dotnet build "--property:VsmkGamePath=$GamePath"
        Invoke-External xmake f -y "--game_path=$GamePath"
        $archivePath = Join-Path $consumer "build/dist/MyMod/mymod-0.1.0.zip"
        New-Item -ItemType Directory -Force -Path (Split-Path $archivePath) | Out-Null
        Set-Content -LiteralPath $archivePath -Value "unowned archive" -NoNewline
        Invoke-ExternalFailure xmake package
        if ((Get-Content -Raw $archivePath) -ne "unowned archive") {
            throw "Packaging changed an unowned archive."
        }
        Remove-Item -LiteralPath $archivePath
        Invoke-External xmake package

        $stage = Join-Path $consumer "build/vsmk/MyMod/stage"
        Set-Content (Join-Path $stage "stale.txt") "must not ship"
        Invoke-External xmake package

        $manifest = Get-Content (Join-Path $stage ".vsmk-files") | ForEach-Object {
            $_.Trim().Replace("\", "/")
        } | Where-Object { $_ } | Sort-Object -Unique
        if ("stale.txt" -in $manifest) {
            throw "The stage manifest included a stale file."
        }
        $runtime = Join-Path $consumer "build/intermediates/dotnet/bin/MyMod/release"
        if (-not (Test-Path (Join-Path $runtime "VintageStoryModKit.dll"))) {
            throw "The fixture build output does not contain the VSMK runtime it must embed."
        }
        $expected = @(
            "MyMod.dll"
            "assets/mymod/config/configlib-patches.json"
            "assets/mymod/config/imm.json"
            "licenses/VintageStoryModKit.txt"
            "licenses/json-everything.txt"
            "modinfo.json"
        )
        Assert-EqualFiles $manifest ($expected | Where-Object { $_ -ne "modinfo.json" }) "Stage manifest"
        $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            Assert-EqualFiles ($archive.Entries.FullName) $expected "Package"
        }
        finally {
            $archive.Dispose()
        }

        $modInfoPath = Join-Path $consumer "modinfo.json"
        $modInfo = Get-Content -Raw $modInfoPath
        $updatedModInfo = $modInfo.Replace('"version": "0.1.0"', '"version": "0.2.0"')
        if ($updatedModInfo -eq $modInfo) {
            throw "Could not update the fixture mod version."
        }
        [IO.File]::WriteAllText($modInfoPath, $updatedModInfo)
        Invoke-External xmake package
        $updatedArchivePath = Join-Path $consumer "build/dist/MyMod/mymod-0.2.0.zip"
        if ((Test-Path $archivePath) -or -not (Test-Path $updatedArchivePath)) {
            throw "Packaging did not replace the previously owned archive after a version change."
        }
        $updatedArchive = [IO.Compression.ZipFile]::OpenRead($updatedArchivePath)
        try {
            Assert-EqualFiles ($updatedArchive.Entries.FullName) $expected "Updated package"
        }
        finally {
            $updatedArchive.Dispose()
        }

        $xmakePath = Join-Path $consumer "xmake.lua"
        $xmakeProject = Get-Content -Raw $xmakePath
        $renamedXmakeProject = $xmakeProject.Replace(
            '{ project = "src/MyMod/MyMod.csproj" }',
            '{ project = "src/MyMod/MyMod.csproj", package_name = "Renamed" }'
        )
        if ($renamedXmakeProject -eq $xmakeProject) {
            throw "Could not update the fixture package name."
        }
        [IO.File]::WriteAllText($xmakePath, $renamedXmakeProject)
        Invoke-External xmake f -y "--game_path=$GamePath"
        Invoke-External xmake package
        $renamedArchivePath = Join-Path $consumer "build/dist/MyMod/Renamed-0.2.0.zip"
        if ((Test-Path $updatedArchivePath) -or -not (Test-Path $renamedArchivePath)) {
            throw "Packaging did not replace the previously owned archive after a package-name change."
        }
        $metadataFiles = @(Get-ChildItem (Join-Path $consumer '.xmake/vsmk/packages') -Filter *.json -File)
        if ($metadataFiles.Count -ne 1) { throw 'Expected one package metadata record.' }
        $metadata = Get-Content -Raw $metadataFiles[0].FullName | ConvertFrom-Json
        if ($metadata.name -ne 'Renamed' -or $metadata.version -ne '0.2.0' -or
            $metadata.archive -ne 'MyMod/Renamed-0.2.0.zip') {
            throw 'Package metadata does not identify the current release archive.'
        }

        if (Test-Path $deployment) {
            throw "The default build deployed files without an explicit destination."
        }

        New-Item -ItemType Directory -Force -Path ".xmake/vsmk" | Out-Null
        @{ MyMod = @($deployment) } | ConvertTo-Json | Set-Content ".xmake/vsmk/deploy.json"
        Invoke-External xmake
        $deployed = Get-ChildItem $deployment -File -Recurse | ForEach-Object {
            $_.FullName.Substring($deployment.Length + 1).Replace("\", "/")
        }
        Assert-EqualFiles $deployed $expected "Deployment"

        New-Item -ItemType Directory -Force -Path "extras", (Join-Path $deployment "tools") | Out-Null
        Set-Content "extras/notes.txt" "package" -NoNewline
        Set-Content "extras/companion-notes.txt" "companion" -NoNewline
        Set-Content "extras/unselected.txt" "unselected" -NoNewline
        $composedXmakeProject = $renamedXmakeProject.Replace(
            'package_name = "Renamed" }',
            "package_name = ""Renamed"", targets = { ""Companion"" } })`n" +
            "    add_deps(""Unselected"")`n" +
            "    add_installfiles(""extras/notes.txt"""
        ) + @'

target("Unselected")
    set_kind("phony")
    add_installfiles("extras/unselected.txt")

target("Companion")
    set_kind("phony")
    on_build(function (target)
        io.writefile(path.join(os.projectdir(), "build/companion/companion.bin"), "generated")
    end)
    add_installfiles("build/companion/companion.bin", { prefixdir = "tools" })
    add_installfiles("extras/companion-notes.txt", { filename = "notes.txt" })
'@
        [IO.File]::WriteAllText($xmakePath, $composedXmakeProject)
        Set-Content (Join-Path $deployment "tools/companion.bin") "unowned" -NoNewline
        Invoke-ExternalFailure xmake
        if ((Get-Content -Raw (Join-Path $deployment "tools/companion.bin")) -ne "unowned") {
            throw "Deployment changed an unowned file."
        }
        Remove-Item (Join-Path $deployment "tools/companion.bin")

        Invoke-External xmake
        Invoke-External xmake package
        $composed = @($expected) + @("notes.txt", "tools/companion.bin")
        $deployed = Get-ChildItem $deployment -File -Recurse | ForEach-Object {
            $_.FullName.Substring($deployment.Length + 1).Replace("\", "/")
        }
        Assert-EqualFiles $deployed $composed "Composed deployment"
        if ((Get-Content -Raw (Join-Path $deployment "notes.txt")) -ne "package") {
            throw "The package's own mapping did not replace the selected target's file."
        }
        $composedArchive = [IO.Compression.ZipFile]::OpenRead($renamedArchivePath)
        try {
            Assert-EqualFiles ($composedArchive.Entries.FullName) $composed "Composed package"
        }
        finally {
            $composedArchive.Dispose()
        }

        [IO.File]::WriteAllText($xmakePath, $renamedXmakeProject)
        Invoke-External xmake
        Invoke-External xmake package
        $deployed = Get-ChildItem $deployment -File -Recurse | ForEach-Object {
            $_.FullName.Substring($deployment.Length + 1).Replace("\", "/")
        }
        Assert-EqualFiles $deployed $expected "Deployment after removing declared files"
        $reducedArchive = [IO.Compression.ZipFile]::OpenRead($renamedArchivePath)
        try {
            Assert-EqualFiles ($reducedArchive.Entries.FullName) $expected "Package after removing declared files"
        }
        finally {
            $reducedArchive.Dispose()
        }
    }
    finally {
        Pop-Location
    }

    Invoke-External uv run --project $root copier copy --trust --defaults --vcs-ref v0.1.0 `
        --data "vsmk_repository=$($root.Replace('\', '/'))" --data settings=false $template $plainConsumer
    Push-Location $plainConsumer
    try {
        if ((Test-Path "src/MyMod/Settings") -or (Get-Content -Raw "src/MyMod/MyMod.csproj").Contains('Include="VintageStoryModKit"')) {
            throw "The settings-free template rendered settings files or the VSMK runtime reference."
        }
        Invoke-External git init
        Invoke-External git config user.name "VSMK tests"
        Invoke-External git config user.email "vsmk-tests@example.invalid"
        Invoke-External git add .
        Invoke-External uv tool run --from pre-commit==4.6.2 pre-commit run --all-files --show-diff-on-failure
        Invoke-External git commit -m "generate settings-free project"
        Invoke-External dotnet new nugetconfig --force
        Invoke-External dotnet nuget add source $feed --name vsmk-local --configfile nuget.config
        Invoke-External xmake f -y "--game_path=$GamePath"
        Invoke-External xmake package
        $plainPackage = [IO.Compression.ZipFile]::OpenRead((Join-Path $plainConsumer "build/dist/MyMod/mymod-0.1.0.zip"))
        try {
            Assert-EqualFiles ($plainPackage.Entries.FullName) @("MyMod.dll", "modinfo.json") "Settings-free package"
        }
        finally {
            $plainPackage.Dispose()
        }

        Invoke-External git add .
        Invoke-External git commit -m "add local package source"
        Invoke-External uv run --project $root copier update --trust --defaults --data settings=true --vcs-ref v0.1.1
        if (-not (Test-Path "src/MyMod/Settings/settings.schema.json")) {
            throw "Copier update did not add settings to the settings-free project."
        }
        Invoke-External xmake package
        $settingsPackage = [IO.Compression.ZipFile]::OpenRead((Join-Path $plainConsumer "build/dist/MyMod/mymod-0.1.0.zip"))
        try {
            Assert-EqualFiles ($settingsPackage.Entries.FullName) $expected "Package after adding settings"
        }
        finally {
            $settingsPackage.Dispose()
        }

        New-Item -ItemType Directory -Force -Path "tests/MyMod.Tests" | Out-Null
        Set-Content "tests/MyMod.Tests/MyMod.Tests.csproj" @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    <ProjectReference Include="../../src/MyMod/MyMod.csproj" />
  </ItemGroup>
</Project>
'@
        $testSource = "tests/MyMod.Tests/SmokeTests.cs"
        Set-Content $testSource @'
namespace MyMod.Tests;

public sealed class SmokeTests
{
    [Xunit.Fact]
    public void Passes() => Xunit.Assert.True(new Vintagestory.API.MathTools.Vec3d(1, 2, 3).X == 1);
}
'@
        Add-Content "xmake.lua" @'

target("MyMod.Tests")
    add_rules("@addon/vsmk/test", { project = "tests/MyMod.Tests/MyMod.Tests.csproj" })
'@
        $testOutput = xmake test 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0 -or $testOutput -notmatch "Passed:\s+1\b") {
            throw "xmake test did not run the .NET test suite:`n$testOutput"
        }
        (Get-Content -Raw $testSource).Replace("X == 1", "X == 2") | Set-Content $testSource
        Invoke-ExternalFailure xmake test
    }
    finally {
        Pop-Location
    }

    # A second pack with the same assembly version builds different assemblies, which collide when two mods ship them unmerged.
    foreach ($project in @("VintageStoryModKit.Settings", "VintageStoryModKit", "VintageStoryModKit.Build")) {
        Invoke-External dotnet pack (Join-Path $kit "dotnet/$project/$project.csproj") `
            --configuration Release --output $feed --version-suffix other "--property:VsmkGamePath=$GamePath"
    }
    Invoke-External uv run --project $root copier copy --trust --defaults --vcs-ref v0.1.1 `
        --data "vsmk_repository=$($root.Replace('\', '/'))" --data project_name=OtherMod $template $otherConsumer
    $otherStage = Join-Path $work "other-stage"
    Push-Location $otherConsumer
    try {
        $otherProject = "src/OtherMod/OtherMod.csproj"
        (Get-Content -Raw $otherProject).Replace("Version=""$version""", "Version=""$version-other""") |
            Set-Content $otherProject
        Invoke-External dotnet new nugetconfig --force
        Invoke-External dotnet nuget add source $feed --name vsmk-local --configfile nuget.config
        Invoke-External dotnet build $otherProject --target:VsmkStage "--property:VsmkStagePath=$otherStage" `
            "--property:VsmkGamePath=$GamePath"
        Copy-Item "modinfo.json" $otherStage
    }
    finally {
        Pop-Location
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $server "Mods"), (Join-Path $server "ModConfig") | Out-Null
    Copy-Item $renamedArchivePath (Join-Path $server "Mods")
    Copy-Item -Recurse $otherStage (Join-Path $server "Mods/OtherMod")
    $otherConfig = Join-Path $server "ModConfig/othermod.json"
    Set-Content $otherConfig '{ "Enabled": 3 }'
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    $start = [Diagnostics.ProcessStartInfo]::new("dotnet")
    foreach ($argument in @((Join-Path $GamePath "VintagestoryServer.dll"), "--dataPath=$server", "--port=$port")) {
        $start.ArgumentList.Add($argument)
    }
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $serverProcess = [Diagnostics.Process]::Start($start)
    $serverOutput = $serverProcess.StandardOutput.ReadToEndAsync()
    $serverErrors = $serverProcess.StandardError.ReadToEndAsync()
    $serverLog = Join-Path $server "Logs/server-main.log"
    try {
        $deadline = [DateTime]::UtcNow.AddMinutes(5)
        while (-not ((Test-Path $serverLog) -and (Select-String -LiteralPath $serverLog -SimpleMatch "Dedicated Server now running" -Quiet))) {
            if ($serverProcess.HasExited -or [DateTime]::UtcNow -gt $deadline) {
                throw "The dedicated server did not start with both mods."
            }
            Start-Sleep -Seconds 1
        }
        $serverProcess.StandardInput.WriteLine("/stop")
        if (-not $serverProcess.WaitForExit(60000)) {
            throw "The dedicated server did not stop."
        }
    }
    catch {
        # The server keeps its logs open, so it must exit before the work directory can be removed.
        if (-not $serverProcess.HasExited) {
            $serverProcess.Kill($true)
            $serverProcess.WaitForExit()
        }
        $console = ($serverOutput.Result + $serverErrors.Result) -split "`r?`n" | Select-Object -Last 60
        throw "$($_.Exception.Message)`n$($console -join "`n")"
    }
    $modErrors = Select-String -LiteralPath $serverLog -Pattern '\[Error\] \[(mymod|othermod)\]'
    if ($modErrors) {
        throw "Mods built against different VSMK builds failed to load together:`n$($modErrors | Out-String)"
    }
    $defaults = Get-Content -Raw (Join-Path $server "ModConfig/mymod.json") | ConvertFrom-Json
    if ($defaults.Enabled -ne $true) {
        throw "The embedded runtime did not write the default settings."
    }
    if (-not (Select-String -LiteralPath $serverLog -SimpleMatch 'othermod.json''. Keeping the last valid values. Settings validation failed at /Enabled' -Quiet) -or
        (Get-Content -Raw $otherConfig).Trim() -ne '{ "Enabled": 3 }') {
        throw "The embedded runtime did not reject invalid settings and leave the file untouched."
    }
}
finally {
    Restore-EnvironmentVariable NUGET_PACKAGES $previousNugetPackages
    Restore-EnvironmentVariable UV_CACHE_DIR $previousUvCache
    Restore-EnvironmentVariable XMAKE_GLOBALDIR $previousXmakeGlobal
    if (Test-Path $work) {
        $deletePath = [IO.Path]::GetFullPath($work)
        if (-not $deletePath.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove test work outside $scratchRoot."
        }
        Remove-Item $deletePath -Recurse -Force
    }
}
