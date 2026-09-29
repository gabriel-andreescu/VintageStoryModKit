# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Changed

- Generated projects' `.luarc.json` loads XMake declarations for the Lua
  language server from xmake-luals, which `xmake f` installs, instead of listing
  the generated calls.
- Builds reuse the staged payload when its files are unchanged.

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
