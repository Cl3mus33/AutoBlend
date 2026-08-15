# Changelog

All notable changes to this project are documented here. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.0.0] - 2026-08-14

First release.

### Added
- Scans every `*/landscape/` texture path (vanilla, DLC, and mod-added) for a `statics` or
  `blending` sibling subfolder — the convention several landscape texture mods (e.g. Vanaheimr)
  already use to signal "this texture wants alpha blending, not alpha testing" — and includes the
  game's own implicit base masters (`Skyrim.esm`, `Update.esm`, `Dawnguard.esm`, `HearthFires.esm`,
  `Dragonborn.esm`) when reading a Mod Organizer 2 load order, even though MO2's own `plugins.txt`
  never lists them.
- Evaluates each Static/MoveableStatic record individually: a shape's own pre-existing Alternate
  Texture is checked on its own merits, independently of whether the shared mesh's default
  (no-override) texture matches — so records sharing one mesh but displaying different regional
  variants aren't all-or-nothing. Meshes needing divergent per-record treatment get a physical
  duplicate; everything else stays on one shared, in-place patched file.
- Flips `NiAlphaProperty` from alpha-testing to alpha-blending in place — the property itself is
  never removed, only its blend mode changes.
- Derived TextureSets are vanilla-friendly by design: only Diffuse and Normal/Gloss are set,
  leaving parallax/Complex Material/PBR slots for a downstream tool like PGPatcher to decide on
  its own terms.
- Wildcard mesh-path and EditorID-keyword blacklists (`*\glass\*`, `*\ice\*`, `*\trees\*`,
  `*\actors\*`, and EditorIDs containing "ice"/"frozen"/"glass"/"unique" by default) protect
  surfaces where alpha testing is intentional.
- Guards against scanning its own previous output if left enabled in the mod manager, so re-runs
  don't compound on top of a prior run's already-derived textures.
- Case-insensitive archive folder lookups, and Alternate Texture matching by NIF shape index as a
  fallback when the (possibly stale) CK-authored name doesn't match anything.
- Native wxWidgets shell, forked from AutoSeasons' own native shell/build toolchain, wired to
  `AutoBlend.Core` via a DNNE-exported `AutoBlend.NativeExport` assembly — plus a WPF desktop shell
  for platforms where the native toolchain isn't convenient. Both share the same settings file.
- Mod Organizer 2 and Vortex/manual-install support, including launching directly from MO2's
  executable list. `autoblend.ini` (content "Empty", same convention as AutoSeasons' `AS.ini`) is
  shipped next to the exe on every build, so dropping the build output into a `mods/` folder is
  enough for MO2 to recognize it as a mod.
- Localized GUI (EN/FR/ES/DE/IT/PT-BR) and a System/Light/Dark theme selector, both ported from
  AutoSeasons' own `ASLocale`/theme-switching pattern — changing the language rebuilds the launcher
  in place, changing the theme restarts the process (wx's dark-mode support can't reliably toggle
  back within one running process).
- "Load Config..." / "Save Config As..." buttons, mirroring PGPatcher's/AutoSeasons' own pattern:
  one shared AutoBlend install (outside any one modlist) can keep separate settings per use case as
  saved JSON files, instead of needing a dedicated copy of the exe per modlist for isolation.
- Synthesizes a missing statics sibling texture on the fly (strips the alpha channel of whatever
  diffuse is actually winning in the load order to opaque, via a small bundled DirectXTex-based
  library called in-process - not a subprocess, so it stays compatible with MO2's USVFS process
  hooking) whenever nothing already provides one, so texture authors no longer need to hand-author
  these siblings themselves. Verified to reproduce Vanaheimr's own hand-authored statics textures
  almost pixel-for-pixel (identical RGB, only the alpha channel changes) - not an approximation.
  Only ever runs for textures explicitly listed in `AutoGenerateAllowlist` (defaults to the base
  game/DLC/Beyond Skyrim landscape textures already hand-verified) rather than blanket-covering
  every structurally-eligible texture, and runs as its own upfront phase before any mesh scanning
  starts. Editable in the native shell as an inline table, same pattern as the mesh/EditorID
  blacklists. Enabled by default (`AutoGenerateMissingStatics` in settings.json).
- Resolves each mod's own packed BSA/BA2 archives, not just its loose files, when reading a Mod
  Organizer 2 load order - previously only loose files were ever checked, so any mod shipping
  assets packed into its own archive (e.g. Beyond Skyrim's `BSAssets.bsa`) was invisible entirely.
  Honors MO2's own priority order throughout, with loose always beating archived regardless of
  which mod either comes from, matching Skyrim's own engine behavior.
