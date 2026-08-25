# Changelog

All notable changes to this project are documented here. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.0.15] - 2026-08-24

### Fixed
- Fixed the generated plugin's own master list being written in plain alphabetical order
  (Dawnguard.esm, Dragonborn.esm, HearthFires.esm, Skyrim.esm, Update.esm, _ResourcePack.esl)
  instead of load order - reported directly via xEdit's own background loader warning: "Modules
  with extended FormID range should always have the Game Master as their first master", tripped by
  Skyrim.esm sitting fourth instead of first on this ESL-flagged plugin. Confirmed and fixed using
  Mutagen's own documented mechanism for this (WithMastersListOrdering, sorted by the real load
  order) - Skyrim.esm now always writes first, matching every real load order.

## [1.0.14] - 2026-08-24

### Changed
- **Generation runs noticeably faster on large modlists.** Mesh processing (extraction, nifly
  parse/patch/save, per-record classification) now runs across every available CPU core instead of
  one mesh at a time - the dominant per-run cost on a modlist with thousands of candidate meshes.
  Only the actual ESP-mutation step (creating TextureSet records, assigning Alternate Textures)
  stays serialized, since that touches shared plugin state that can't safely be written from
  multiple threads at once - everything else (which is most of the work) is fully parallel.
  Verified against a real ~200-mod modlist: two full runs of the same modlist produced byte-for-byte
  identical output counts, confirming the parallel version isn't racy.
- Every mesh needing a patch was being parsed by nifly **twice** - once to scan its shapes during
  detection, then a second time from scratch inside the actual patching step, discarding the first
  parse entirely. The already-loaded file is now reused for both steps, roughly halving the nifly
  parsing cost of the whole run.
- MO2 file resolution (used for every mesh/texture lookup across a run, and every landscape
  texture's up-to-3-candidate-path probe) previously re-walked every enabled mod folder on every
  query with no way to short-circuit "not found anywhere" - the single most common outcome, since
  most landscape textures have no statics/blending/blend sibling at all. Both positive and negative
  results are now cached per path, turning every repeat query into an instant lookup.

### Fixed
- A derived TextureSet's Height/RMAOS slots always carry forward whatever the winning source
  TextureSet already had there (vanilla complex-material data included), regardless of the
  "Generate PBR Slots" setting - correct, deliberate behavior from an earlier fix, but the
  setting's own description still claimed the old ("only Diffuse/Normal ever set") behavior.
  Updated to describe what the setting actually controls: whether NEW PBR files get generated for
  textures that don't already have Height/RMAOS data, not whether existing data gets dropped.

### Removed
- A handful of methods with zero call sites anywhere in the codebase (confirmed by exhaustive
  search, not left in "just in case"): `NifShapeTextureResolver.GetDiffusePath` (superseded by
  `GetAllSlots`), `WildcardMatcher.IsMatch` (only `MatchesAny` is ever used), and three unused
  `NiAlphaFlags` bit-accessors (`GetSourceBlend`/`GetDestBlend`/`IsNoSorterSet`).

## [1.0.13] - 2026-08-24

### Fixed
- **Fixed a hard crash ("Failed to compare two elements in the array. → The method or operation is
  not implemented.") during "Loading game environment..." for load orders where two archives
  (.bsa/.ba2) collapse to the same name once a mod's own " - Suffix" convention is stripped off -
  reported directly, reliably reproduced down to a single Creation Club content pack. This is a
  genuine bug in Mutagen's own archive-priority comparer (confirmed directly against its own
  source): the branch reached when two archives tie is a bare `throw new NotImplementedException()`
  that its own authors never filled in. It was being hit from AutoBlend's own code - the real
  Data folder archive scan `ArchiveAwareFileProbe` runs as a fallback for anything not provided by a
  mod - not from anything AutoBlend itself does with the colliding names, and could affect any load
  order regardless of mod manager or profile setup. Since this can't be fixed on Mutagen's end from
  here, that specific call is now wrapped: if it throws, AutoBlend falls back to a plain, unsorted
  archive listing (archive priority ordering isn't actually needed for what this scan is used for)
  instead of taking down the whole run.
- Fixed the MO2 Profile dropdown never repopulating when the MO2 Instance Path is typed or pasted
  directly rather than chosen via the "Browse..." button - the likely first-time setup path, before
  a settings.json exists to pre-fill the field. Left the dropdown empty with no way to pick anything
  but whatever "Default" silently falls back to, which is wrong for any instance whose real active
  profile isn't literally named "Default". Now also refreshes when the field loses focus.
- Error dialogs now show the full exception chain (every `InnerException`, not just the outermost
  wrapper's own message) plus the innermost exception's own stack trace - a generic .NET wrapper
  message like "Failed to compare two elements in the array." was the only thing ever surfacing to
  a user's error dialog, with no way to tell what actually went wrong underneath without attaching a
  debugger. This is what made the fix above possible to track down from a bug report alone.

## [1.0.12] - 2026-08-24

### Fixed
- Mesh Blacklist now also includes `*\effects\*`, `*\magic\*`, and `*\weapons\*` by default (with
  the same once-only backfill used for the 1.0.11 rules) - these folders never carry a landscape
  diffuse, confirmed directly from a real load order's own run log where a large share of harmless
  "mesh not found" warnings came from exactly these three folders. Also brought the native shell's
  own `*\actors\*` default in line with the C# side, which already had it.

## [1.0.11] - 2026-08-24

### Fixed
- Fixed PBR generation silently creating zero TextureSets for shapes whose source is an existing
  Alternate Texture that already resolves to a PBR-edited TextureSet (e.g. "Sloppy Vanilla
  Landscapes PBR", which edits vanilla TextureSet records in place rather than shipping new ones).
  The allowlist check that decides whether a "blend" variant may be generated compared the
  already-PBR-prefixed incoming path directly against an allowlist authored in vanilla paths, so it
  never matched and generation silently produced nothing for this whole class of shapes - even
  though the exact same texture generated correctly when reached through a mesh's own vanilla-path
  embedded default instead. The allowlist check now strips a leading PBR path segment before
  comparing, so it always sees the texture's true vanilla identity regardless of which path
  resolved it.
- Fixed "Generate PBR Slots" never actually generating the plain, vanilla-looking "blend" texture
  file (e.g. `textures\landscape\blend\rocks01.dds`) whenever a PBR sibling was involved - only its
  PBR-prefixed counterpart (`textures\pbr\landscape\blend\rocks01.dds`) ever got synthesized. Every
  such mesh's own diffuse texture slot is intentionally baked with the vanilla-looking path (so PG
  Patcher can discover and convert it - see below), which meant the mesh referenced a texture file
  that existed nowhere in the load order at all: a hard missing-texture ("purple") result,
  independent of whether PG Patcher ever runs. AutoBlend now always completes both the vanilla and
  the PBR sibling together.
- Fixed the last-resort, from-scratch PBRNifPatcher json AutoBlend authors for a texture with no
  existing config anywhere using a bare filename (e.g. `"dirt02.dds"`) as its match key. PG Patcher's
  own matching is a raw path-suffix match with no folder-boundary enforcement, so a bare filename can
  silently hijack an unrelated texture of the same name anywhere else in the load order - including
  the very parent (non-blend) texture this variant was derived from. The match key is now always
  fully qualified with its folder path (e.g. `"landscape\blend\dirt02"`).
- Fixed cloning an existing PBRNifPatcher entry from a combined, multi-texture json (e.g. Sloppy's
  own single "Sloppy Vanilla Landscapes.json") rewriting every entry in that file to the same match
  value, instead of cloning only the one entry that actually matches. Simplified to a single
  code path (find the one matching entry anywhere in the load order, clone it) used for every case.
- The `settings.json` a copy of AutoBlend writes now lives in its own `AutoBlend\` subfolder next to
  the exe (e.g. `AutoBlend\settings.json`) instead of directly as a bare `settings.json` - under
  MO2's own USVFS this previously landed at `overwrite\settings.json`, a name generic enough to risk
  colliding with another tool's own config landing in that same shared overwrite root. Existing
  settings are migrated automatically, once, the first time this version runs.
- Fixed a newly-typed row in the Mesh Blacklist / EditorID Keywords / Auto-Generate Allowlist lists
  being silently dropped if the user clicked OK/Generate (or changed language/theme) immediately
  after typing, without pressing Enter or clicking elsewhere first to commit the in-place edit -
  reported directly: a newly added EditorID keyword never made it into `settings.json` despite
  clicking Generate right after typing it, with no error shown. Any pending edit is now committed
  before its list is read.
- **Fixed every derived TextureSet's Diffuse/Normal/Height/RMAOS fields carrying a redundant
  leading `textures\` segment** (e.g. `textures\PBR\Landscape\blend\Rocks01.dds` instead of the
  correct `PBR\Landscape\blend\Rocks01.dds`) - confirmed directly against a real vanilla TXST
  record's own field, which never carries that segment. A TextureSet's own asset link is always
  relative to `Data\textures\` already; the doubled path silently resolved to a nonexistent
  `Data\textures\textures\...` file in game - a hard missing-texture ("purple") result on every
  single derived TextureSet this whole session's investigation had been chasing. This was the
  actual root cause underneath most of the "purple texture" reports.
- Fixed a derived TextureSet (and its own PBRTextureSets json) being named after the NIF SHAPE's
  own name rather than the actual texture it uses, for shapes with no existing Alternate Texture
  override. A shape's own name can be completely unrelated to its texture (a decorative rock
  sub-object nifly auto-named e.g. `RockPileM01:8` inside some unrelated static mesh, rendering
  with a totally different "Rocks01" texture) - producing confusing names like
  `BlendRockPileM01` with no connection to any real "RockPileM01" texture, and in the worst case
  (a colon in the auto-generated name) silently writing the json into a hidden NTFS stream instead
  of a real file. TextureSets are now always named after the actual resolved texture.
- **Eliminated the "\_blend2", "\_blend3", ... mesh duplication AutoBlend could produce when many
  different references shared one physical mesh file with different Alternate Texture needs.**
  Every shape - whether its diffuse came from an existing override or the mesh's own embedded
  default - now derives/reuses a TextureSet and gets it assigned via a new Alternate Texture, the
  same way an existing override always worked; the mesh itself only ever needs its alpha-blend
  mode flipped once, uniformly, so it's patched in place in the vast majority of cases (mesh
  duplication now only happens for the one remaining legitimate reason: a mesh shared between an
  in-scope record and a blacklisted one).
- Mesh Blacklist and EditorID Keywords now include `*\dungeons\*` and `wet`/`road`/`cave`/`mine`
  by default (dungeon/cave meshes and weather-variant/road/mine records reuse ordinary landscape
  textures the same way roads already did in 1.0.10, with the same false-positive risk). Existing
  installs get these backfilled automatically, once, the same way the 1.0.10 roads rule was.
### Added
- AutoBlend now generates its own `PBRNifPatcher\...json` configs for every PBR "blend" texture it
  creates, so PG Patcher picks up author-tuned material parameters (roughness, parallax,
  displacement, glint, etc.) instead of falling back to its own bare "mark as PBR, no parameters"
  default - including explicit `slot2`/`slot4`/`slot6` overrides pointing Normal/Height/RMAOS at the
  real PBR sibling files, since PG Patcher otherwise auto-derives those from the SAME location as the
  matched diffuse (confirmed directly against PG Patcher's own source), which AutoBlend's own "blend"
  folder never populates. Two sources are tried in order: (1) every PBRNifPatcher json anywhere in
  the load order is searched for a single entry that already covers the source texture - this is
  what actually matters for packs like "Sloppy Vanilla Landscapes PBR", which bundle every one of
  their entries into a single arbitrarily-named combined json rather than one file per texture, so a
  plain "does a file exist at the expected path" check could never find them; (2) only if nothing
  anywhere already describes the texture is a fresh json authored from scratch, using the same
  default parameters AutoSeasons' own equivalent generator already uses. When multiple mods each
  describe the same texture in their own separate json, the mod highest in the MO2 load order wins -
  matching PG Patcher's own documented conflict-resolution order (mod order first, then alphabetical
  filename, then entry position).
- AutoBlend also generates a `PBRTextureSets\{TextureSet EditorID}.json` for every new PBR
  TextureSet it creates in its own ESP - Community Shaders' own PBR material config for that record,
  matched purely by EditorID rather than texture path. Cloned verbatim from the source TextureSet's
  own same-named json if one exists anywhere in the load order, else a minimal default.

## [1.0.10] - 2026-08-23

### Fixed
- Fixed derived TextureSets being cached (and reused) by NIF shape name instead of by the actual
  texture being derived. Generic shape names like "RockSkirt" are reused across many unrelated
  meshes with completely different diffuse textures (e.g. `MountainTrim01.nif`'s own "RockSkirt"
  legitimately differs per placed reference - some use SnowRocks01, others use Rocks01, Tundra
  Rocks01, etc., matching each reference's own vanilla-authored Alternate Texture). Because the
  cache was keyed only on the rule folder plus the shape's name, whichever mesh/record happened to
  be processed first "won" the cache entry, and every other reference sharing that shape name
  silently inherited its wrong derived TextureSet instead of getting its own - producing visibly
  mismatched ("purple"/wrong-looking) landscape textures in game, independent of PBR or any specific
  texture pack. Reproduced directly against a minimal vanilla + ERM - Enhanced Rocks and Mountains
  test: every "RockSkirt" shape across MountainTrim01/02/03 and their Wet variants collapsed onto a
  single shared (and mostly wrong) TextureSet before this fix. The cache is now keyed on the actual
  resolved output texture path instead, so unrelated shapes sharing a name no longer collide, and
  each reference's own alternate texture is derived independently and correctly.
- Generated plugins are now flagged ESL (light plugin) automatically whenever the run's own new
  records fit the ESL limit (~2048) - which every real run does, since AutoBlend only ever adds a
  handful of derived TextureSets. Keeps AutoBlend Output off the 254-regular-plugin hard limit
  without the user needing to flag it by hand. Falls back to a normal ESP (with a warning) on the
  rare run large enough to exceed the limit, rather than writing a corrupt plugin.
- Removed the one-time fallback to a shared `%APPDATA%\AutoBlend\settings.json` when a copy's own
  local `settings.json` doesn't exist yet. That fallback was meant to carry an existing user's
  values forward across the update that introduced per-exe-dir settings (v1.0.7) - but left in
  place indefinitely, it meant every brand new AutoBlend install on a brand new modlist silently
  inherited whatever modlist's settings were saved there most recently (wrong Game Location,
  Output Location, MO2 Instance Path, etc.) instead of starting clean, reported directly as
  confusing and unwanted. A copy with no settings.json of its own now always starts blank.

## [1.0.9] - 2026-08-21

### Fixed
- Added `*\roads\*` to the default Mesh Blacklist. Road-texture-replacer mods (Simplest Roads,
  Simply Dirt Roads, reported directly) reuse an ordinary landscape texture's diffuse (e.g.
  Dirt02.dds, FallForestDirt01.dds) directly on their own road meshes instead of shipping a
  dedicated one. AutoBlend had no way to tell that reuse apart from a real landscape mesh sharing
  the same texture, so it patched road meshes as if they were landscape ones - producing malformed
  derived texture paths and wrong (e.g. snow) texture assignments on roads. An existing
  `settings.json` saved before this fix gets the new rule appended automatically on next load, same
  as every previous default-list addition - nothing to reconfigure by hand.

## [1.0.8] - 2026-08-21

### Added
- AutoBlend now mirrors a PBRNifPatcher json config for derived/generated PBR "statics"/
  "blending"/"blend" texture variants, when the texture pack itself didn't already ship one that
  matches: PG Patcher (which must always run last) discovers its per-texture material parameters
  (parallax, roughness, subsurface, glint, multilayer/coat data, explicit slot overrides, etc.)
  from these jsons, matched against a shape's diffuse path by a suffix search over the json's own
  "texture"/"match_diffuse" field - not by the json's own file location. Packs that key their json
  with just the bare filename (e.g. Vanaheimr's own convention: `"texture": "dirt02"`) already
  match any nesting depth and need nothing extra. Packs that qualify the path with a folder (e.g.
  TomatoRim PBR's `"texture": "landscape\\Dirt02"`) do NOT match a "statics"/"blend"-nested variant
  - PG Patcher then falls all the way back to a bare "mark as PBR, no parameters" default for that
  texture, losing every author-tuned value. AutoBlend now clones the parent texture's own json
  (every material parameter and explicit slot override kept exactly as authored) and redirects
  just the "texture"/"match_diffuse" field to the resolved variant, so PG Patcher applies its full,
  normal patch instead. Verified directly against a real modlist: correctly skipped Vanaheimr's own
  bare-filename-keyed jsons (nothing to do) and mirrored 8 real cases from other packs, preserving
  glint/multilayer/coat parameters and explicit slot overrides unchanged.

## [1.0.7] - 2026-08-21

### Added
- `settings.json` now lives beside the running exe instead of a single shared
  `%APPDATA%\AutoBlend\settings.json` - each modlist's own dedicated AutoBlend copy (the real,
  established deployment convention: a separate "AutoBlend" mod folder per MO2 instance) now keeps
  its own settings automatically, with nothing to save/load by hand when switching between
  modlists. First run for a given copy falls back to the old shared location once (so an existing
  user's values carry forward instead of appearing blank) without ever writing back to it.

### Fixed
- Auto-generated texture compression always recompressed to BC7 regardless of what the texture was
  for - correct for vanilla/complex-material diffuses, but wrong for PBR ones, which need BC1 (sRGB,
  not linear). Reported directly as part of investigating purple/wrong rendering on PBR texture
  packs. `MissingTextureGenerator`/`AutoBlendTexTools` now select the target format based on
  whether the source was resolved via PBR detection.
- Derived TextureSet EditorIDs could double the rule folder's type label - e.g.
  "StaticsStaticsRocks01" - whenever the shape's diffuse came from an existing Alternate Texture
  whose own TextureSet was already named after the rule folder (common with landscape packs that
  ship their own pre-blended, already-named TXST records, e.g. Vanaheimr PBR's own
  "StaticsRocks01"). Cosmetic only, but confirmed directly against a real generation's own output.
- A shape that some OTHER mod already fully alpha-blended itself (own custom mesh, own already-
  blend-mode alpha property, embedded diffuse already the resolved statics/blending/blend variant)
  still got duplicated and renamed by AutoBlend every time, even though nothing would actually
  change - reported as a possible cause of a downstream complex-material/PBR patcher no longer
  recognizing (and therefore not re-processing) AutoBlend's own renamed output. Shapes with nothing
  left to do are now left fully untouched instead.

## [1.0.6] - 2026-08-21

### Fixed
- Found the actual root cause behind the recurring "[ERROR] Absolute path did not have Data
  folder within it." reports, which 1.0.3 and 1.0.5's broader catches still didn't reach: a
  user's full error log showed the crash landing right after "Scanning Static and MoveableStatic
  records..." starts - well before either of those fixes' coverage. `Model.File` is itself a
  Mutagen AssetLink, and reading `.GivenPath` on ANY Static/MoveableStatic record in the entire
  load order - not just ones AutoBlend derives anything for - throws this exact exception if that
  record's own mesh path is a malformed absolute path (the same class of authoring mistake as the
  texture paths fixed in 1.0.4/1.0.5, just on the mesh reference this time). Confirmed directly:
  assigning a `Model` a malformed absolute `File` path and reading `GivenPath` back throws the
  identical exception. The initial record-scanning loop now catches this per-record and skips just
  that one record, instead of aborting before a single mesh is even processed. Regression-verified
  against a real, large modlist: identical counts to before, zero new warnings.

## [1.0.5] - 2026-08-21

### Fixed
- A user on 1.0.4 still hit "Absolute path did not have Data folder within it." aborting the
  whole run, despite 1.0.3's fix for that exact error - the earlier fix only caught it around one
  specific call (deriving a new TextureSet), but Mutagen can throw the same validation error from
  other places a malformed third-party path first gets touched, not just that one. The per-mesh
  processing loop in `PatchOrchestrator` now has a broader catch-and-continue around each mesh's
  entire record-processing pass: any unexpected failure (this one, a corrupt mesh nifly can't
  recover from, or similar) is logged as a warning and only skips that one mesh, instead of
  aborting every other mesh's already-completed work. Verified: full regression run against a
  real, large modlist produces zero new warnings from this broader catch (identical counts to
  before), confirming it only activates on genuine unexpected failures.

## [1.0.4] - 2026-08-20

### Fixed
- Derived "statics"/"blending"/"blend" TextureSets always dropped Height and
  EnvironmentMaskOrSubsurfaceTint (the complex-material parallax/environment-mask slots), even
  outside PBR mode, since those two slots only ever came from PBR detection with no fallback to
  the source TextureSet's own values - unlike NormalOrGloss, which already had this fallback.
  Reported as purple/broken textures after running AutoBlend + a complex-material patcher.
  Verified directly against real data: even vanilla Skyrim's own "Landscape\Dirt02.dds" TXST
  record already populates both slots (complex material shipped in the base game itself), and a
  user's own real generation output showed exactly this - Diffuse/NormalOrGloss correct, Height
  and EnvironmentMaskOrSubsurfaceTint both empty. Now falls back to the source TextureSet's own
  Height/EnvironmentMaskOrSubsurfaceTint whenever no PBR sibling was found, matching the pattern
  already used for NormalOrGloss.
- "Generate PBR slots" never found a PBR variant that ships already nested inside a
  "statics"/"blending"/"blend" folder with no plain non-nested PBR override at all (e.g. Vanaheimr
  PBR) - only the single non-nested candidate was ever checked, so PBR detection silently failed
  and fell back to whatever non-PBR sibling happened to exist instead, even with the setting
  checked. Reported directly. Reproduced exactly with a synthetic probe; now checks every
  configured rule folder's own PBR-nested location too before giving up on PBR for a texture.

## [1.0.3] - 2026-08-20

### Fixed
- A malformed absolute texture path baked into a third-party plugin's own TXST record (some mod
  authors' CK setups store an absolute path missing "Data" entirely instead of a proper
  Data-relative one) aborted the ENTIRE run with a raw "Absolute path did not have Data folder
  within it." error and nothing generated at all, instead of just that one derived TextureSet
  failing. Reported directly (a user hit this on their own real modlist). Root-caused and
  reproduced exactly via a synthetic TXST with a malformed `NormalOrGloss` path, confirming
  Mutagen's own `AssetLink` construction is what throws. `PatchOrchestrator` now catches this
  per-TextureSet, logs it as a warning ("Could not create derived TextureSet for '...' - left
  untouched."), and continues the run for every other record - matching the existing
  "Mesh not found, skipped"/"Could not resolve existing TextureSet... - left untouched" resilience
  pattern already used elsewhere. Verified via a full MO2-mode regression run against a real,
  large modlist: identical counts to before the fix (27006 records scanned, 7 TextureSets created,
  165 Alternate Textures assigned) - the fix only changes behavior on the malformed-data path.

## [1.0.2] - 2026-08-20

### Added
- MO2 Profile picker in the General tab: a dropdown listing the real profile folders under the
  chosen MO2 Instance Path, populated on browse/instance-path change and defaulting to the
  instance's own currently-active profile (from ModOrganizer.ini) when detected, else the first
  entry. Reported directly: the shell previously always assumed the profile was named "Default"
  with no way to override it, so anyone whose instance only had other profiles (a real report had
  three, none named "Default") got nothing but a raw "No modlist.txt found for profile 'Default'"
  exception at the start of a patch run.

### Fixed
- Build reliability only, no functional change for end users: removed `--no-restore` from the
  native shell's own DNNE build step, which could silently skip native-export codegen entirely
  from a cold build folder (see the identical fix already shipped for AutoBlend's sibling tool,
  SnowFixer).

## [1.0.1] - 2026-08-20

### Fixed
- "Generate PBR slots": Normal/Height/RMAOS were silently left unset (falling back to a
  mismatched pre-PBR normal, or nothing at all) whenever the diffuse being resolved was already
  PBR-prefixed - which happens whenever an existing (non-AutoBlend-authored) Alternate Texture
  already points at a PBR-authored TextureSet, e.g. a PBR texture pack overriding a vanilla
  landscape TXST record in place. `ToPbrPath` returned `null` in that case ("nothing to swap"),
  which skipped sibling-slot resolution entirely even though the real `_n`/`_p`/`_rmaos` files
  existed right next to the diffuse on disk. Verified against a real modlist: regenerating after
  the fix correctly populated all 4 slots (Diffuse/Normal/Height/RMAOS) consistently across every
  derived TextureSet, where previously Height/RMAOS were always empty and Normal pointed at an
  unrelated, stale texture.

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
- Removed `textures\landscape\reachmoss01.dds` from the default allowlist.

### Performance
- Full-pipeline runtime on a large real modlist (24,348 records, 14,265 meshes) dropped from
  ~530s to ~113s (4.6x), with identical output. Two fixes: cached wildcard-pattern regexes and
  per-texture sibling-detection results instead of recomputing them on every record/mesh scanned
  (the dominant cost - measured at ~335s of pure waste on the same modlist), and GPU-accelerated
  (DirectCompute) texture compression for the auto-generation feature above, ~8x faster than CPU
  for the large landscape textures a real load order can resolve to, with an automatic CPU
  fallback when no D3D11 device is available.
