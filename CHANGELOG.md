# Changelog

All notable changes to this project are documented here. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.0.0] - 2026-08-14

### Fixed
- `Mo2LoadOrderMaterializer` never included the game's own implicit base masters (`Skyrim.esm`,
  `Update.esm`, `Dawnguard.esm`, `HearthFires.esm`, `Dragonborn.esm`) since MO2's `plugins.txt`
  never lists them — every run was silently missing the entire vanilla base game, the single
  biggest source of "why does this patch so little" reports.
- Alternate Texture-carrying shapes were only ever considered if the *mesh's own embedded
  (no-override) diffuse* also happened to resolve to a `statics`/`blending` sibling, so a record's
  own valid Alternate Texture was ignored whenever the shared mesh's default texture didn't match
  — even though Alternate Texture is exactly the mechanism meant to let different records display
  different textures from one shared mesh. Alpha-property shapes are now classified per record;
  meshes needing divergent per-record treatment get a physical duplicate (reusing the existing
  blacklist-duplication machinery, now keyed on treatment signature instead of just blacklist
  status) so a shape needing alpha-blend for one record doesn't affect another record that must
  keep the original alpha-test blend.
- `LandscapeFolderDetector.Detect()` treated a path already sitting inside a configured rule folder
  (e.g. an Alternate Texture that already points straight at the `statics` variant) as unresolved,
  since it always looked for a *second*, nested `statics/statics/...` folder. Recognized as
  already-resolved instead.
- Derived TextureSets only ever set the Diffuse and Normal/Gloss slots — leaving parallax/Complex
  Material/PBR slots for a downstream tool like PGPatcher, instead of copying whatever the source
  texture set happened to carry (which may not apply to the detected variant at all).
- A Static/MoveableStatic override was created even when nothing about that specific record ended
  up changing (no matching Alternate Texture, benefits purely from the shared/duplicate mesh file)
  — cluttering the output plugin with no-op override records.
- The tool refused to guard against scanning its own previous output if left enabled in the mod
  manager, compounding each run on top of the last one's already-derived textures.
- Archive folder lookups (`IArchiveReader.TryGetFolder`) were case-sensitive even though the final
  file match is not, missing real files in archives that mix casing across plugin records.
- `AlternateTexture` matching only used the (possibly stale) CK-authored `Name` label; `Index` —
  the shape's actual position in the NIF, which the engine resolves by — is now an authoritative
  fallback.

### Added
- Native wxWidgets shell (this directory), forked from AutoSeasons' own native shell/build
  toolchain, wired to AutoBlend's existing `AutoBlend.Core` patch pipeline via a DNNE-exported
  `AutoBlend.NativeExport` assembly.
- Landscape texture folder detection (`statics`/`blending` subfolders under any `*/landscape/`
  path), mesh scanning, `NiAlphaProperty` alpha-blend patching, and derived TextureSet/Alternate
  Texture output plugin generation — all in `AutoBlend.Core` (C#), unchanged by this native port.
- Mod Organizer 2 and Vortex/manual-install support, including launching directly from MO2's
  executable list.
- `autoblend.ini` (content "Empty", same convention as AutoSeasons' `AS.ini`) shipped next to the
  exe on every build, so dropping the build output into a `mods/` folder is enough for MO2 to
  recognize it as a mod.

### Changed
- Default mesh blacklist now also covers `*\trees\*` and `*\actors\*`, alongside the original
  `*\glass\*` and `*\ice\*`.
