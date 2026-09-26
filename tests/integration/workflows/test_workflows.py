import hashlib
import json
import os
import subprocess
import tarfile
from pathlib import Path

import pytest
import yaml

ROOT = Path(__file__).resolve().parents[3]


def run_step(
    tmp_path,
    name,
    environment,
    *,
    prefix="",
    definition=".github/workflows/release.yml",
):
    workflow = yaml.safe_load((ROOT / definition).read_text(encoding="utf-8"))
    steps = (
        [step for job in workflow["jobs"].values() for step in job.get("steps", [])]
        if "jobs" in workflow
        else workflow["runs"]["steps"]
    )
    script = next(step["run"] for step in steps if step.get("name") == name)
    return subprocess.run(
        [
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            "$ErrorActionPreference = 'Stop'\n" + prefix + script,
        ],
        cwd=tmp_path,
        env={**os.environ, **environment},
        capture_output=True,
        text=True,
        check=False,
    )


@pytest.mark.parametrize(
    "tag,version,valid",
    [
        ("v1.2.3", "1.2.3", True),
        ("v1.2.3", "", True),
        ("1.2.4", "1.2.4", True),
        ("v1.2.4", "1.2.3", False),
        ("main", "1.2.3", False),
    ],
)
def test_release_version(tmp_path, tag, version, valid):
    output = tmp_path / "output"
    result = run_step(
        tmp_path,
        "Validate release version",
        {"RELEASE_TAG": tag, "EXPECTED_VERSION": version, "GITHUB_OUTPUT": str(output)},
    )
    assert (result.returncode == 0) == valid, result.stderr
    if valid:
        assert output.read_text().strip() == f"version={tag.removeprefix('v')}"
    else:
        assert not output.exists()


@pytest.mark.parametrize(
    "date,status,notes,valid",
    [
        ("2026-09-14", "released", "### Fixed\n\n- A fix.\n", True),
        ("2026-02-30", "released", "- A fix.", False),
        ("", "unreleased", "- A fix.", False),
        ("2026-09-14", "yanked", "- A fix.", False),
        ("2026-09-14", "released", " \n", False),
    ],
)
def test_release_notes(tmp_path, date, status, notes, valid):
    source = tmp_path / "notes.md"
    source.write_text(notes)
    result = run_step(
        tmp_path,
        "Prepare release notes",
        {
            "RELEASE_DATE": date,
            "RELEASE_STATUS": status,
            "RELEASE_NOTES": str(source),
            "NOTES_ID": "0000",
            "PACKAGE_NAMES": "",
        },
    )
    assert (result.returncode == 0) == valid, result.stderr
    output = tmp_path / "release-notes/0000.md"
    if valid:
        assert output.read_text() == notes
    else:
        assert not output.exists()


def test_target_changelog_selection(tmp_path):
    directory = tmp_path / "package-metadata"
    directory.mkdir()
    records = [
        ("Main", "2.3.0", None),
        ("Inherited", "1.0.0", None),
        ("Root", "2.3.0", "CHANGELOG.md"),
        ("Textures", "1.1.0", "Server/Optional/Textures/CHANGELOG.md"),
        ("TexturesLite", "1.1.0", "Server/Optional/Textures/CHANGELOG.md"),
        ("OlderTextures", "1.0.0", "Server/Optional/Textures/CHANGELOG.md"),
        ("Config", "1.0.0", "Server/Misc/Config/CHANGELOG.md"),
    ]
    for name, version, changelog in records:
        (directory / f"{name}.json").write_text(
            json.dumps(
                {
                    "target": f"Server::{name}",
                    "name": name,
                    "version": version,
                    "changelog": changelog,
                }
            )
        )
    output = tmp_path / "output"
    result = run_step(
        tmp_path,
        "Select changelog entries",
        {
            "RELEASE_VERSION": "2.3.0",
            "PROJECT_DIRECTORY": "project",
            "CHANGELOG_PATH": "CHANGELOG.md",
            "PACKAGE_METADATA": "package-metadata",
            "GITHUB_OUTPUT": str(output),
        },
    )
    assert result.returncode == 0, result.stderr
    entries = json.loads(output.read_text().removeprefix("entries="))
    assert len(entries) == 4
    root = entries[0]
    assert (Path(root["path"]).as_posix(), root["version"], root["names"]) == (
        "project/CHANGELOG.md",
        "2.3.0",
        "",
    )
    assert {
        (
            Path(entry["path"]).as_posix(),
            entry["version"],
            frozenset(entry["names"].split(", ")),
        )
        for entry in entries[1:]
    } == {
        ("project/Server/Misc/Config/CHANGELOG.md", "1.0.0", frozenset({"Config"})),
        (
            "project/Server/Optional/Textures/CHANGELOG.md",
            "1.0.0",
            frozenset({"OlderTextures"}),
        ),
        (
            "project/Server/Optional/Textures/CHANGELOG.md",
            "1.1.0",
            frozenset({"Textures", "TexturesLite"}),
        ),
    }


def test_combined_release_notes(tmp_path):
    source = tmp_path / "notes.md"
    for identifier, name, version, notes in [
        ("0001", "Textures", "1.1.0", "### Added\n\n- New textures.\n"),
        ("0000", "", "2.3.0", "### Fixed\n\n- A fix.\n"),
    ]:
        source.write_text(notes)
        result = run_step(
            tmp_path,
            "Prepare release notes",
            {
                "RELEASE_DATE": "2026-09-15",
                "RELEASE_STATUS": "released",
                "RELEASE_NOTES": str(source),
                "NOTES_ID": identifier,
                "PACKAGE_NAMES": name,
                "PACKAGE_VERSION": version,
            },
        )
        assert result.returncode == 0, result.stderr
    result = run_step(tmp_path, "Combine release notes", {})
    assert result.returncode == 0, result.stderr
    assert (tmp_path / "release-notes.md").read_text(encoding="utf-8") == (
        "### Fixed\n\n- A fix.\n\n## Textures - v1.1.0\n\n### Added\n\n- New textures.\n"
    )


@pytest.mark.parametrize(
    "packages,attachments",
    [
        (None, []),
        ([], []),
        (
            ["MyMod/Main-1.0.0.zip", "MyMod/Option-2.0.0.zip"],
            ["Main-1.0.0.zip", "Option-2.0.0.zip"],
        ),
        (
            [
                "Server/MyMod-1.0.0.zip",
                "Client/MyMod-1.0.0.zip",
                "Server/Optional-2.0.0.zip",
            ],
            [
                "Server-MyMod-1.0.0.zip",
                "Client-MyMod-1.0.0.zip",
                "Optional-2.0.0.zip",
            ],
        ),
        (
            ["Server/MyMod/Alternate.zip", "Client/MyMod/Alternate.zip"],
            ["Server-MyMod-Alternate.zip", "Client-MyMod-Alternate.zip"],
        ),
        (
            ["Server/MyMod/MyMod-1.0.0.zip", "Client/MyMod/MyMod-1.0.0.zip"],
            ["Server-MyMod-1.0.0.zip", "Client-MyMod-1.0.0.zip"],
        ),
        (
            ["MyMod/MyMod-1.0.0.zip", "Server/MyMod/MyMod-1.0.0.zip"],
            ["MyMod-1.0.0.zip", "Server-MyMod-1.0.0.zip"],
        ),
    ],
)
def test_release_attachments(tmp_path, packages, attachments):
    if packages is not None:
        directory = tmp_path / "packages"
        directory.mkdir()
        for name in packages:
            archive = directory / name
            archive.parent.mkdir(parents=True, exist_ok=True)
            archive.write_bytes(b"zip")
    result = run_step(
        tmp_path,
        "Publish GitHub release",
        {
            "RELEASE_TAG": "v1.0.0",
            "PACKAGE_ARTIFACT": "packages" if packages is not None else "",
        },
        prefix=(
            "function gh {\n"
            "    foreach ($arg in $args) {\n"
            "        if ($arg.EndsWith('.zip') -and [IO.File]::ReadAllText($arg) -ne 'zip') { throw 'Invalid ZIP' }\n"
            "    }\n"
            "    $args | ConvertTo-Json -Compress\n"
            "}\n"
        ),
    )
    if packages == []:
        assert result.returncode != 0
        assert "contains no ZIPs" in result.stderr
    else:
        assert result.returncode == 0, result.stderr
        assert '"--verify-tag"' in result.stdout
        assert '"--notes-file","release-notes.md"' in result.stdout
        arguments = json.loads(result.stdout)
        assert {
            Path(argument).name for argument in arguments if argument.endswith(".zip")
        } == set(attachments)
        for name in packages or []:
            assert (tmp_path / "packages" / name).read_bytes() == b"zip"


def test_release_rejects_colliding_prefixed_names(tmp_path):
    for name in ("Server/Mod.zip", "Client/Mod.zip", "Other/Server-Mod.zip"):
        archive = tmp_path / "packages" / name
        archive.parent.mkdir(parents=True, exist_ok=True)
        archive.write_bytes(b"zip")
    result = run_step(
        tmp_path,
        "Publish GitHub release",
        {"RELEASE_TAG": "v1.0.0", "PACKAGE_ARTIFACT": "packages"},
        prefix="function gh { throw 'Unexpected GitHub call' }\n",
    )
    assert result.returncode != 0
    assert "Duplicate release filename" in result.stderr
    assert "Unexpected GitHub call" not in result.stderr


def test_consumer_configuration_cannot_enable_deployment(tmp_path):
    result = run_step(
        tmp_path,
        "Configure",
        {
            "CONFIGURE_ARGUMENTS": "--deploy=y\n--example=value with spaces",
            "DIST_DIRECTORY": "build/release output",
            "VINTAGE_STORY": "C:/Game Files",
        },
        prefix="function xmake { ConvertTo-Json -InputObject $args -Compress }\n",
        definition=".github/workflows/build.yml",
    )
    assert result.returncode == 0, result.stderr
    arguments = json.loads(result.stdout)
    assert arguments[-3:] == [
        "--game_path=C:/Game Files",
        "--deploy=n",
        "--distdir=build/release output",
    ]
    assert "--example=value with spaces" in arguments


@pytest.mark.parametrize("valid_checksum", [True, False])
def test_game_references_verify_the_archive_and_clean_downloads(
    tmp_path, valid_checksum
):
    assembly = tmp_path / "VintagestoryAPI.dll"
    assembly.write_bytes(b"reference fixture")
    archive = tmp_path / "server.tar.gz"
    with tarfile.open(archive, "w:gz") as contents:
        contents.add(assembly, arcname="server/VintagestoryAPI.dll")
    checksum = (
        hashlib.md5(archive.read_bytes()).hexdigest() if valid_checksum else "0" * 32
    )
    catalog = tmp_path / "catalog.json"
    catalog.write_text(
        json.dumps(
            {
                "1.22.7": {
                    "linuxserver": {
                        "urls": {"cdn": "https://example.invalid/server.tar.gz"},
                        "md5": checksum,
                    }
                }
            }
        )
    )
    runner = tmp_path / "runner"
    runner.mkdir()
    output = tmp_path / "output"
    environment = tmp_path / "environment"
    result = run_step(
        tmp_path,
        "Install game references",
        {
            "GAME_VERSION": "1.22.7",
            "RUNNER_TEMP": str(runner),
            "GITHUB_OUTPUT": str(output),
            "GITHUB_ENV": str(environment),
            "TEST_CATALOG": str(catalog),
            "TEST_ARCHIVE": str(archive),
        },
        definition=".github/actions/setup-game/action.yml",
        prefix=(
            "function Invoke-RestMethod { Get-Content -LiteralPath $env:TEST_CATALOG -Raw | ConvertFrom-Json }\n"
            "function Invoke-WebRequest { param($Uri, $OutFile) Copy-Item -LiteralPath $env:TEST_ARCHIVE -Destination $OutFile }\n"
        ),
    )
    assert (result.returncode == 0) == valid_checksum, result.stderr
    assert not list(runner.glob("*.tar.gz"))
    if valid_checksum:
        game = Path(output.read_text().strip().removeprefix("path="))
        assert (game / "VintagestoryAPI.dll").read_bytes() == b"reference fixture"
        assert environment.read_text().strip() == f"VINTAGE_STORY={game}"
    else:
        assert not output.exists()
        assert not list(runner.iterdir())
