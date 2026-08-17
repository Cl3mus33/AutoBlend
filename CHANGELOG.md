# Changelog

All notable changes to this project are documented here. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.0.0] - 2026-08-14

First release.

### Fixed
- **Critical**: MO2 mod priority was inverted - `modlist.txt` is written in the opposite order
  from MO2's own mod list panel (file top = panel bottom, not the same order), so this tool was
  resolving the *lowest*-priority mod's file as the winner instead of the highest's, for every
  loose file and archive lookup. Verified directly against a real instance (modlist.txt
  cross-checked against a live MO2 panel screenshot across four mods) and confirmed fixed against
  two disputed textures that now resolve to the mod the user expected. **Every previous run's
  output should be considered suspect and regenerated.**

### Added
- Scans every `*/landscape/` texture path (vanilla, DLC, and mod-added) for a `statics` or
  `blending` sibling subfolder — the convention several landscape texture mods (e.g. Vanaheimr)
  already use to signal "this texture wants alpha blending, not alpha testing" — and includes the
  game's own implicit base masters (`Skyrim.esm`, `Update.esm`, `Dawnguard.esm`, `HearthFires.esm`,
  `Dragonborn.esm`, `_ResourcePack.esl`) when reading a Mod Organizer 2 load order, even though
  MO2's own `plugins.txt` never lists them.
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
- Optional "Generate PBR slots" checkbox: when a PBR texture pack ships its own variant of a
  texture (Skyrim PBR packs ship at a separate, parallel `textures\pbr\...` location rather than
  overriding the vanilla path in place), a derived "statics" TextureSet resolves and uses that
  pack's own Diffuse/Normal/Height/RMAOS - Skyrim's 4-slot PBR convention, found via its own
  "_n"/"_p"/"_rmaos" naming convention - instead of the vanilla texture. Off by default
  (`GeneratePbrSlots` in settings.json), keeping the original vanilla-friendly 2-slot behavior;
  PGPatcher still does its own separate mesh-level PBR conversion pass either way.
- Native launcher UI reorganized into "General" and "Options" tabs (app-wide Language/Theme moved
  into Options), and the Auto-Generate Allowlist table moved into its own "Edit Allowlist..."
  dialog - both matching AutoSeasons' own launcher layout and keeping the main tab focused on
  per-run settings.
- Sibling-folder detection is no longer restricted to `*/landscape/` texture paths - any diffuse
  texture used by an alpha-tested Static/MoveableStatic shape is now checked for a `statics`,
  `blending`, or `blend` sibling next to it, regardless of category (verified against a mine/cave
  retexture's own `textures\dungeons\mines\statics\` folder). AutoBlend's own auto-generation now
  writes into a dedicated `blend` folder instead of `statics`, so its synthesized output never
  collides with either third-party-authored convention; both are still matched normally when
  already present. Existing `settings.json` files are backfilled with the new rule automatically.
- Auto-generated textures are now always recompressed to BC7, regardless of the winning source's
  own format (previously mirrored it, which could produce BC1/BC3 output) - keeps every generated
  texture on one consistent, modern format and avoids at least one third-party DDS viewer
  misreading BC3's alpha channel.
- Sets the ZBuffer_Write and No_Fade shader flags on every shape AutoBlend touches - many source
  meshes ship with these disabled, which is fine for their original alpha state but causes
  visible render artifacts (and unwanted distance dithering) once real alpha blending is enabled.
  Verified against a real modlist: 741/766 alpha-blend shapes in AutoBlend's own output now carry
  ZBuffer_Write; the remaining 25 are legitimate fire/glow/smoke/fake-water effect shapes outside
  AutoBlend's own detected-shape scope, which correctly stay untouched.
- Mod authors can now ship their own `AutoGenerateAllowlist` entries alongside their mod, as a
  JSON array of the same wildcard path strings in a file named `*_autoblend.json` at the Data
  root - mirroring Base Object Swapper's own `*_SWAP.ini` convention (one uniquely-named file per
  mod, not a single shared filename), so multiple mods' manifests accumulate instead of only the
  highest-priority one winning. Merged with the user's own locally-configured allowlist at run
  time; verified against a real MO2 instance with two separate mods each carrying their own file.
- `*_autoblend.json` is meant for mods the user doesn't directly control the settings of - the
  base game/DLC/Beyond Skyrim entries stay as `AutoGenerateAllowlist`'s own hardcoded default
  instead (reviewable/editable straight from the "Edit Allowlist..." table, saved through the app
  itself like any other entry, rather than requiring a hand-edited JSON file in the install
  folder).

### Performance
- Full-pipeline runtime on a large real modlist (24,348 records, 14,265 meshes) dropped from
  ~530s to ~113s (4.6x), with identical output. Two fixes: cached wildcard-pattern regexes and
  per-texture sibling-detection results instead of recomputing them on every record/mesh scanned
  (the dominant cost - measured at ~335s of pure waste on the same modlist), and GPU-accelerated
  (DirectCompute) texture compression for the auto-generation feature above, ~8x faster than CPU
  for the large landscape textures a real load order can resolve to, with an automatic CPU
  fallback when no D3D11 device is available.
