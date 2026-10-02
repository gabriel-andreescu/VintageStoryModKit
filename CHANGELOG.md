# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/2.0.0/).

## [Unreleased]

### Added

- Generate only tooling configuration for an existing project with
  `tooling_only=true`.
- The build workflow installs npm dependencies for each committed
  `package-lock.json` before it runs pre-commit hooks.

### Changed

- Generated README lists the documentation links and describes CI.
- Generated VS Code settings recommend and configure Prettier, StyLua and the
  Lua language server.
- Generated projects' `.luarc.json` loads XMake declarations for the Lua
  language server from xmake-luals, which `xmake f` installs, instead of listing
  the generated calls.
- Builds reuse the staged payload when its files are unchanged.
- The build workflow builds with XMake from `gabriel-andreescu/xmake` at a
  pinned commit instead of the XMake 3.1.1 release.

### Removed

- Remove the `vsmk_repository` template option.
- The template no longer asks for the initial mod version. New projects start at
  0.1.0 in `modinfo.json`.

## [0.2.0] - 2026-09-27

### Changed

- Mods now embed the settings runtime, so players no longer install a separate
  mod. Remove the `vintagestorymodkit` dependency from `modinfo.json`.
  `copier update` removes it from generated projects.
- Staging fails when several assemblies of a mod reference the settings runtime,
  its dependencies or libraries built on them.
- Package libraries built on the settings runtime's dependencies are merged into
  the mod assembly with it, so they no longer ship as separate files.
- The settings runtime package `VintageStoryModKit` is now
  `VintageStoryModKit.Settings`, and `SettingsHost` moved to the
  `VintageStoryModKit.Settings` namespace. The game-independent schema and
  store, previously `VintageStoryModKit.Settings`, are now
  `VintageStoryModKit.Settings.Core`.
- The embedded settings schema resource `VintageStoryModKit.SettingsSchema` is
  now `VintageStoryModKit.Settings.Schema`.

### Removed

- The `vintagestorymodkit` runtime mod.

## [0.1.0] - 2026-09-26

### Added

- Initial release.
