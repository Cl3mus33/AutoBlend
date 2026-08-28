using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// Synthesizes a missing statics/blending sibling texture on the fly from whatever diffuse is
/// actually winning in the load order, so a texture author never has to hand-author these siblings
/// themselves - AutoBlend covers the gap automatically.
///
/// The transform is a straight alpha-channel strip: Skyrim's landscape alpha-blending reads its
/// blend weight from the diffuse texture's own alpha channel (not vertex colors), so forcing that
/// channel to fully opaque - while leaving every RGB pixel untouched - is exactly what turns a
/// blend-edge diffuse into a standalone "statics" one. This was verified directly against
/// Vanaheimr's own hand-authored "landscape\statics\rocks01.dds": sampling both against the
/// original blended "landscape\rocks01.dds" showed identical RGB data (nothing but ordinary BC7
/// recompression noise, a handful of intensity levels out of 255) and the same near-255 flattened
/// alpha - i.e. this is the exact transform Vanaheimr's own textures were made with, not a
/// convenient approximation.
///
/// Implemented via a direct P/Invoke call into AutoBlendTexTools.dll (native/AutoBlendTexTools, a
/// small DirectXTex wrapper built alongside this project) rather than shelling out to texconv.exe
/// as a subprocess: MO2's USVFS hooks CreateProcess for every child process of an MO2-launched
/// executable, and texconv.exe reliably crashed with STATUS_ACCESS_VIOLATION under that hook in
/// real-world testing despite working flawlessly standalone outside MO2. Calling the same
/// transform in-process (no new process ever gets created) sidesteps that hook entirely.
/// </summary>
public sealed class MissingTextureGenerator
{
    [DllImport("AutoBlendTexTools.dll", CharSet = CharSet.Unicode)]
    private static extern int ab_strip_alpha_to_opaque(string srcPath, string dstPath, int isPbr);

    // A single combined file (matching how "Sloppy Vanilla Landscapes PBR" itself ships all of its
    // own entries bundled together, rather than one file per texture) under a name distinctive
    // enough that it can never collide with another mod's own PBRNifPatcher json landing in the
    // same shared folder - PG Patcher discovers configs by scanning the whole "PBRNifPatcher\"
    // subtree regardless of how many files are in it (confirmed: file count/naming has no bearing
    // on its own matching or conflict-resolution behavior), so consolidating costs nothing.
    private const string CombinedPbrNifPatcherJsonPath = @"PBRNifPatcher\AutoBlend_blend.json";

    private readonly IGameFileProbe _fileProbe;
    private readonly string _outputLocation;
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonArray _combinedPbrNifPatcherEntries = new();
    private readonly HashSet<string> _writtenPbrNifPatcherMatchKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pbrTextureSetJsonPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _diagnostics = new();
    private bool _reportedMissingDll;

    // Built lazily on first lookup: normalized match value (e.g. "landscape\rocks01", no prefix/
    // extension) -> raw JSON text of the single entry object that covers it, taken from ANY json
    // found under "PBRNifPatcher\" across the whole load order - not just a file living at the
    // exact path our own naming convention would compute. See TryFindExistingPbrEntry.
    private Dictionary<string, string>? _pbrJsonEntryIndex;

    /// <summary>Number of textures successfully synthesized this run.</summary>
    public int GeneratedCount { get; private set; }

    /// <summary>Number of PBRNifPatcher json configs mirrored this run (see <see cref="TryMirrorPbrJson"/>).</summary>
    public int MirroredJsonCount { get; private set; }

    /// <summary>Number of PBRTextureSets json configs mirrored this run (see <see cref="TryMirrorPbrTextureSetJson"/>).</summary>
    public int MirroredTextureSetJsonCount { get; private set; }

    /// <summary>Number of generation attempts that were made but failed (source not found,
    /// transform failed, etc.) - distinct from a sibling already existing, which never reaches
    /// this generator at all.</summary>
    public int FailedCount { get; private set; }

    /// <summary>Human-readable detail for every failed attempt, capped to avoid flooding the run
    /// log on a modlist where generation can't run at all - the first entry always explains why.</summary>
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    public MissingTextureGenerator(IGameFileProbe fileProbe, string outputLocation)
    {
        _fileProbe = fileProbe;
        _outputLocation = outputLocation;
    }

    /// <summary>
    /// Generates an opaque copy of <paramref name="sourceDiffusePath"/> (a path relative to Data,
    /// e.g. "textures\landscape\rocks01.dds") at <paramref name="targetRelativePath"/> (e.g.
    /// "textures\landscape\statics\rocks01.dds") inside the output location's own textures folder.
    /// <paramref name="isPbr"/> selects the output compression: PBR diffuse textures need BC1 (sRGB,
    /// not linear) while vanilla/complex-material ones need BC7 - recompressing a PBR source to BC7
    /// (or dropping its sRGB tag) reported directly as visibly wrong rendering downstream. Cached
    /// per target path within this generator's lifetime (one patch run) so repeat calls don't redo
    /// the transform for the same texture. Returns false if generation isn't possible (source
    /// missing, or the transform itself failed) - callers should treat that exactly like "no
    /// sibling found" rather than an error.
    /// </summary>
    public bool TryGenerate(string sourceDiffusePath, string targetRelativePath, bool isPbr, out string generatedFullPath)
    {
        if (_cache.TryGetValue(targetRelativePath, out var cached))
        {
            generatedFullPath = cached ?? string.Empty;
            return cached is not null;
        }

        var success = TryGenerateCore(sourceDiffusePath, targetRelativePath, isPbr, out generatedFullPath);
        _cache[targetRelativePath] = success ? generatedFullPath : null;
        if (success)
        {
            GeneratedCount++;
        }
        else
        {
            FailedCount++;
        }
        return success;
    }

    /// <summary>
    /// PG Patcher (the "last step" PBR patcher many users run after AutoBlend) discovers its own
    /// per-texture material parameters (parallax, roughness, subsurface, etc.) from
    /// "PBRNifPatcher\...json" configs shipped by texture authors, matched against a shape's
    /// diffuse path by a SUFFIX search over the json's own "texture"/"match_diffuse" field - not
    /// by where the json file itself lives on disk. A bare filename key like "dirt02" (no folder
    /// prefix, as Vanaheimr's own PBR packs ship it) already matches ANY depth of nesting
    /// ("landscape\dirt02", "landscape\statics\dirt02", "landscape\blend\dirt02" alike), so most
    /// real-world packs need nothing extra from AutoBlend at all. But a pack whose json instead
    /// qualifies the path with its containing folder (e.g. TomatoRim's own "landscape\Dirt02", one
    /// segment only - confirmed directly against TomatoRim PBR - Landscapes' own shipped json)
    /// does NOT match a "statics"/"blending"/"blend"-nested variant, since the extra folder segment
    /// breaks the suffix match. For those, PG Patcher falls all the way back to its bare
    /// "mark as PBR, no extra parameters" default - losing every author-tuned material value.
    ///
    /// This mirrors the parent (non-nested) diffuse's own json for the nested variant AutoBlend
    /// just resolved: same material parameters, only the "texture"/"match_diffuse" field updated to
    /// the nested path. Every entry AutoBlend itself ever produces (cloned or freshly authored)
    /// accumulates into ONE combined array in memory and is written out as a single file (see
    /// <see cref="WritePbrNifPatcherJson"/>) - matching how "Sloppy Vanilla Landscapes PBR" itself
    /// ships all of its own entries bundled into one file rather than one per texture; PG Patcher's
    /// own discovery scans the whole "PBRNifPatcher\" subtree regardless of file count, so this
    /// changes nothing about what gets matched, only how many files AutoBlend itself adds.
    ///
    /// Does nothing if the nested variant already has its own entry, keyed to that EXACT full
    /// identity, ANYWHERE in the load order (some packs, like Vanaheimr, ship one explicitly even
    /// though their own bare-filename convention wouldn't have needed it) - checked via
    /// <see cref="TryFindExistingPbrEntry"/> with bare-filename fallback disabled. A bare-filename
    /// match alone is NOT treated as existing coverage here (see that method's own doc comment for
    /// why - reproduced directly: PG Patcher set the PBR shader flag via its own separate heuristic
    /// but never repointed the diffuse itself, because nothing was ever authored against the exact
    /// nested identity it needed). Otherwise searches every PBRNifPatcher json anywhere in the load
    /// order (bare-filename fallback allowed here - this is a DONOR lookup, not a skip decision) for
    /// a single entry that already covers the PARENT texture - this is what catches a pack like
    /// "Sloppy Vanilla Landscapes PBR", which ships ALL of its entries bundled into one
    /// arbitrarily-named combined json rather than one file per texture, and is also what a pack's
    /// own dedicated per-texture json resolves through (that file is itself part of the same index,
    /// so no separate direct-path check is needed). Only as a last resort, when nothing anywhere
    /// already describes this texture, is a fresh default authored (see
    /// <see cref="BuildDefaultEntry"/>).
    ///
    /// <paramref name="pbrNormalPath"/>/<paramref name="pbrHeightPath"/>/<paramref name="pbrRmaosPath"/>
    /// are the REAL PBR sibling files this run already resolved (see LandscapeFolderDetector) - PG
    /// Patcher auto-derives Normal/Height/RMAOS from the SAME base path as the matched diffuse
    /// (confirmed directly against PGPatcher's own source, PatcherMeshShaderTruePBR.cpp's
    /// applyOnePatchSlots: "{matchedPath}_n.dds"/"_p.dds"/"_rmaos.dds", unless an explicit "slotN"
    /// override is present), and AutoBlend's own "blend" folder only ever contains the diffuse (see
    /// EnsureVanillaCompanion in LandscapeFolderDetector) - so without an explicit override, PG
    /// Patcher would look for "_n"/"_p"/"_rmaos" siblings that don't exist next to our blend diffuse,
    /// and (per PGPatcher's own path-validation) drop the WHOLE match, not just those slots. Only
    /// applied when the entry doesn't already define its own override (respecting a donor's own
    /// explicit intent) - see <see cref="ApplyPbrSlotOverridesIfAbsent"/>.
    /// </summary>
    public void TryMirrorPbrJson(
        string parentPbrDiffusePath,
        string nestedPbrDiffusePath,
        string? pbrNormalPath = null,
        string? pbrHeightPath = null,
        string? pbrRmaosPath = null)
    {
        var nestedMatchValue = StripTexturesPbrPrefixAndExtension(nestedPbrDiffusePath);
        if (nestedMatchValue is null || !_writtenPbrNifPatcherMatchKeys.Add(NormalizeMatchValue(nestedMatchValue)))
        {
            // Either not a recognizable PBR path, or already added to our own combined json this
            // run (redundant calls happen naturally - many records share the same source texture).
            return;
        }

        if (TryFindExistingPbrEntry(nestedMatchValue, allowBareNameFallback: false) is not null)
        {
            // Some pack already ships a dedicated entry for this EXACT nested identity - nothing
            // for AutoBlend to add. A bare-filename-only match doesn't count here (see
            // TryFindExistingPbrEntry's own doc comment) - it's a real donor for parameters below,
            // not proof PG Patcher will actually repoint this specific nested path's diffuse.
            return;
        }

        var parentIdentity = StripTexturesPbrPrefixAndExtension(parentPbrDiffusePath);
        var donorEntry = parentIdentity is not null ? TryFindExistingPbrEntry(parentIdentity) : null;

        try
        {
            var entry = donorEntry is not null
                ? BuildClonedEntry(donorEntry, nestedMatchValue)
                : BuildDefaultEntry(nestedMatchValue);
            ApplyPbrSlotOverridesIfAbsent(entry, pbrNormalPath, pbrHeightPath, pbrRmaosPath);

            _combinedPbrNifPatcherEntries.Add(entry);
            MirroredJsonCount++;
        }
        catch (Exception ex)
        {
            AddDiagnostic($"Could not add a PBRNifPatcher entry for '{nestedPbrDiffusePath}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Writes every entry accumulated this run (see <see cref="TryMirrorPbrJson"/>) out as
    /// the single combined <see cref="CombinedPbrNifPatcherJsonPath"/> file. Called once, after every
    /// mesh has been processed - entries keep accumulating in memory throughout the run rather than
    /// each call touching disk.</summary>
    public void WritePbrNifPatcherJson()
    {
        if (_combinedPbrNifPatcherEntries.Count == 0)
        {
            return;
        }

        try
        {
            var destFullPath = Path.Combine(_outputLocation, CombinedPbrNifPatcherJsonPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destFullPath)!);
            File.WriteAllText(destFullPath, _combinedPbrNifPatcherEntries.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AddDiagnostic($"Could not write combined PBRNifPatcher json: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Searches every json found under "PBRNifPatcher\" anywhere in the load order (via
    /// <see cref="IGameFileProbe.EnumerateFiles"/> - loose files across every active mod, not just
    /// one computed path) for an entry whose own "texture"/"match_diffuse" value already covers
    /// <paramref name="vanillaIdentity"/> (e.g. "landscape\rocks01"). Needed because not every pack
    /// ships one json file per texture, named after it - "Sloppy Vanilla Landscapes PBR" was found
    /// shipping ALL of its entries bundled into a single "Sloppy Vanilla Landscapes.json" (the same
    /// convention AutoBlend's own combined output now follows too), so a plain per-texture-path
    /// existence check would never find it even though the exact entry needed is right there.
    /// </summary>
    /// <param name="allowBareNameFallback">
    /// When true (the default - used for DONOR lookup, i.e. borrowing an existing entry's tuned
    /// material parameters), also matches on the bare filename alone (e.g. a Vanaheimr-style entry
    /// keyed just "rocks01"), on the assumption that PG Patcher's own suffix-match would apply that
    /// same entry at any nesting depth. Reproduced directly against a real PG Patcher run that this
    /// assumption does NOT hold for the actual diffuse-texture-set repoint: PG Patcher set the "this
    /// is PBR" shader flag (via its own, separate RMAOS-existence heuristic) but left the shape's
    /// diffuse slot on the vanilla path, because no entry was ever written whose own match key is
    /// the FULL nested identity ("landscape\blend\rocks01", not just "rocks01") - the bare-filename
    /// donor is real and does get used for material PARAMETERS, but doesn't, on its own, drive PG
    /// Patcher's diffuse repoint for a nested identity it was never authored against. Pass false when
    /// deciding whether to SKIP writing our own dedicated entry (see TryMirrorPbrJson) - a full-identity
    /// match there means a pack genuinely already ships this exact nested path; a bare-filename match
    /// does not, and must not be treated as if it does.
    /// </param>
    /// <returns>Null if nothing anywhere covers it.</returns>
    private JsonObject? TryFindExistingPbrEntry(string vanillaIdentity, bool allowBareNameFallback = true)
    {
        EnsurePbrJsonEntryIndexBuilt();

        var normalized = NormalizeMatchValue(vanillaIdentity);
        if (_pbrJsonEntryIndex!.TryGetValue(normalized, out var json))
        {
            return JsonNode.Parse(json) as JsonObject;
        }

        if (!allowBareNameFallback)
        {
            return null;
        }

        var bareName = NormalizeMatchValue(Path.GetFileName(vanillaIdentity));
        if (_pbrJsonEntryIndex.TryGetValue(bareName, out json))
        {
            return JsonNode.Parse(json) as JsonObject;
        }

        return null;
    }

    private void EnsurePbrJsonEntryIndexBuilt()
    {
        if (_pbrJsonEntryIndex is not null)
        {
            return;
        }

        _pbrJsonEntryIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> jsonPaths;
        try
        {
            jsonPaths = _fileProbe.EnumerateFiles("PBRNifPatcher", ".json").ToList();
        }
        catch (Exception ex)
        {
            AddDiagnostic($"Could not enumerate existing PBRNifPatcher json files: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        foreach (var jsonPath in jsonPaths)
        {
            try
            {
                JsonNode? json;
                using (var source = _fileProbe.OpenRead(jsonPath))
                {
                    json = JsonNode.Parse(source);
                }

                var entries = json as JsonArray ?? json?["entries"] as JsonArray;
                if (entries is null)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (entry is not JsonObject obj)
                    {
                        continue;
                    }

                    var matchValue = (obj["texture"] ?? obj["match_diffuse"])?.GetValue<string>();
                    if (string.IsNullOrEmpty(matchValue))
                    {
                        continue;
                    }

                    // First entry found for a given identity wins - matches how a real modlist's
                    // own load-order priority would resolve two mods both describing the same
                    // texture, since EnumerateFiles already yields loose files in that same order.
                    _pbrJsonEntryIndex.TryAdd(NormalizeMatchValue(matchValue), obj.ToJsonString());
                }
            }
            catch
            {
                // Best-effort: one unparsable json (malformed, not actually PG Patcher's schema)
                // shouldn't stop the rest of the load order from being indexed.
            }
        }
    }

    /// <summary>Clones a single existing PBRNifPatcher entry (found via <see cref="TryFindExistingPbrEntry"/>)
    /// with its match key re-pointed at <paramref name="nestedMatchValue"/> - same tuned material
    /// parameters (roughness, displacement, glint, etc.) as whatever already describes the parent
    /// texture, only the match key itself updated. "rename" is stripped since that field redirects
    /// to the SOURCE entry's own alternate identity, which has no meaning for our own new path.</summary>
    private static JsonObject BuildClonedEntry(JsonObject sourceEntry, string nestedMatchValue)
    {
        var clone = (JsonNode.Parse(sourceEntry.ToJsonString()) as JsonObject)!;
        clone.Remove("rename");
        if (clone.ContainsKey("texture"))
        {
            clone["texture"] = nestedMatchValue;
        }
        else
        {
            clone["match_diffuse"] = nestedMatchValue;
        }

        return clone;
    }

    /// <summary>Points PG Patcher's Normal/Height(parallax)/RMAOS slots at the real, already-resolved
    /// PBR sibling files (see TryMirrorPbrJson's own doc comment for why this is required, not
    /// optional, for AutoBlend's own "blend" folder) - "slot2"/"slot4"/"slot6" per PGPatcher's own
    /// TextureSlots numbering (Diffuse=slot1/implicit, Normal=slot2, Parallax=slot4, RMAOS=slot6;
    /// verified both against PGPatcher's own source and a real shipped AutoSeasons Output json using
    /// this exact numbering). Only sets a slot the entry doesn't already define itself (via an
    /// explicit "slotN" key or a "lock_*" flag) - a donor entry cloned from a real pack may already
    /// carry its own correct override, which should win over ours.</summary>
    private static void ApplyPbrSlotOverridesIfAbsent(JsonObject entry, string? pbrNormalPath, string? pbrHeightPath, string? pbrRmaosPath)
    {
        if (pbrNormalPath is not null && !entry.ContainsKey("slot2") && !entry.ContainsKey("lock_normal"))
        {
            entry["slot2"] = pbrNormalPath;
        }

        if (pbrHeightPath is not null && !entry.ContainsKey("slot4") && !entry.ContainsKey("lock_parallax"))
        {
            entry["slot4"] = pbrHeightPath;
        }

        if (pbrRmaosPath is not null && !entry.ContainsKey("slot6") && !entry.ContainsKey("lock_rmaos"))
        {
            entry["slot6"] = pbrRmaosPath;
        }
    }

    /// <summary>Normalizes a PBRNifPatcher match value ("texture"/"match_diffuse", or our own
    /// StripTexturesPbrPrefixAndExtension output) to a stable index key: backslash-separated,
    /// extension-less, case folded via the index's own OrdinalIgnoreCase comparer.</summary>
    private static string NormalizeMatchValue(string value)
    {
        var noExt = Path.HasExtension(value) ? value[..^Path.GetExtension(value).Length] : value;
        return noExt.Replace('/', '\\').Trim('\\');
    }

    /// <summary>
    /// Builds a fresh PBRNifPatcher entry for <paramref name="matchValue"/> from scratch, using the
    /// same default TruePBR material parameters AutoSeasons' own equivalent generator already uses
    /// for its own seasonal texture variants (verified directly against a real "AutoSeasons Output"
    /// json) - same author, same underlying PG Patcher target, no reason to invent a second set of
    /// defaults. The match key itself, unlike AutoSeasons' own bare-filename convention, is always
    /// fully qualified (folder + name, e.g. "landscape\blend\dirt02") rather than a bare "dirt02" -
    /// PG Patcher's own matching is a raw path-suffix match with no component boundary enforced in
    /// code (confirmed against PGPatcher's own source), so a bare filename can hijack an unrelated
    /// texture of the same name anywhere else in the load order (including the parent, non-blend
    /// texture this variant was itself derived from) - a real, demonstrated overlap hazard this
    /// generator must not reintroduce just because AutoSeasons' own situation (a genuinely new,
    /// never-colliding name per season) made it safe for them specifically. Used as the last-resort
    /// fallback when there's no existing entry anywhere to mirror parameters from (see
    /// TryMirrorPbrJson) - a generic, reasonable-default material is still far better than PG
    /// Patcher's own bare "mark as PBR, no parameters" fallback for a texture with zero prior art.
    /// </summary>
    private static JsonObject BuildDefaultEntry(string matchValue) => new()
    {
        ["match_diffuse"] = matchValue,
        ["emissive"] = false,
        ["parallax"] = true,
        ["subsurface_foliage"] = false,
        ["subsurface"] = false,
        ["specular_level"] = 0.04,
        ["subsurface_color"] = new JsonArray(1, 1, 1),
        ["roughness_scale"] = 1,
        ["subsurface_opacity"] = 1,
        ["smooth_angle"] = 75,
        ["displacement_scale"] = 1,
    };

    /// <summary>
    /// Community Shaders' own PBR material config for a TextureSet record - distinct from PG
    /// Patcher's own "PBRNifPatcher\...json" (matched by diffuse texture path against a NIF shape)
    /// - lives at "PBRTextureSets\{TextureSet EditorID}.json" and is matched purely by that EditorID
    /// as its own filename, with no match key inside the file itself. Every real pack found on disk
    /// (AutoSeasons, Sloppy Vanilla Landscapes PBR, Vanaheimr, TomatoRim) ships exactly one file per
    /// EditorID - unlike PBRNifPatcher, there's no combined-multi-entry-file case to search for
    /// here, so a plain existence check against the exact computed source path is enough. Because
    /// the content has no self-referencing field, a match is copied verbatim - no rewriting needed,
    /// unlike TryMirrorPbrJson's own "texture"/"match_diffuse" field.
    /// </summary>
    public void TryMirrorPbrTextureSetJson(string sourceEditorId, string derivedEditorId)
    {
        var targetPath = Path.Combine("PBRTextureSets", derivedEditorId + ".json");
        if (_pbrTextureSetJsonPaths.Contains(targetPath) || _fileProbe.Exists(targetPath))
        {
            return;
        }

        try
        {
            string content;
            var sourcePath = Path.Combine("PBRTextureSets", sourceEditorId + ".json");
            if (_fileProbe.Exists(sourcePath))
            {
                using var source = _fileProbe.OpenRead(sourcePath);
                using var reader = new StreamReader(source);
                content = reader.ReadToEnd();
            }
            else
            {
                // Same minimal default AutoSeasons' own generator ships for a texture with no
                // prior art (verified directly against its own "PBRTextureSets\LandscapeDirt02_
                // AUT.json") - a reasonable generic PBR material rather than Community Shaders
                // silently having nothing at all for this TextureSet.
                content = "{\n  \"displacementScale\": 1.0,\n  \"roughnessScale\": 1.0,\n  \"specularLevel\": 0.04\n}";
            }

            var destFullPath = Path.Combine(_outputLocation, targetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destFullPath)!);
            File.WriteAllText(destFullPath, content);

            _pbrTextureSetJsonPaths.Add(targetPath);
            MirroredTextureSetJsonCount++;
        }
        catch (Exception ex)
        {
            AddDiagnostic($"Could not write PBRTextureSets json for '{derivedEditorId}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>"textures\pbr\landscape\statics\dirt02.dds" -&gt; "landscape\statics\dirt02" (the bare,
    /// extension-less, prefix-less form used both as a PBRNifPatcher json match value and to derive
    /// where such a json itself conventionally lives). Null if not a recognizable PBR diffuse path.</summary>
    private static string? StripTexturesPbrPrefixAndExtension(string pbrDiffusePath)
    {
        const string prefix = @"textures\pbr\";
        if (!pbrDiffusePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = pbrDiffusePath[prefix.Length..];
        var dir = Path.GetDirectoryName(relative) ?? "";
        var nameNoExt = Path.GetFileNameWithoutExtension(relative);
        return string.IsNullOrEmpty(dir) ? nameNoExt : Path.Combine(dir, nameNoExt);
    }

    private void AddDiagnostic(string message)
    {
        // Capped so a modlist where every single texture fails the same way doesn't produce a
        // run log thousands of lines long - the first handful is always enough to diagnose.
        if (_diagnostics.Count < 20)
        {
            _diagnostics.Add(message);
        }
    }

    private bool TryGenerateCore(string sourceDiffusePath, string targetRelativePath, bool isPbr, out string generatedFullPath)
    {
        generatedFullPath = Path.Combine(_outputLocation, targetRelativePath);

        string extractedPath;
        try
        {
            using var source = _fileProbe.OpenRead(sourceDiffusePath);
            extractedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dds");
            using var dest = File.Create(extractedPath);
            source.CopyTo(dest);
        }
        catch (FileNotFoundException)
        {
            AddDiagnostic($"'{sourceDiffusePath}': source texture not found in the load order, skipped.");
            return false;
        }
        catch (Exception ex)
        {
            AddDiagnostic($"'{sourceDiffusePath}': failed to extract source texture ({ex.GetType().Name}: {ex.Message}).");
            return false;
        }

        var outputDir = Path.GetDirectoryName(generatedFullPath)!;
        var createdOutputDir = !Directory.Exists(outputDir);
        var success = false;

        try
        {
            Directory.CreateDirectory(outputDir);

            var resultCode = ab_strip_alpha_to_opaque(extractedPath, generatedFullPath, isPbr ? 1 : 0);
            if (resultCode != 0)
            {
                AddDiagnostic($"'{sourceDiffusePath}': AutoBlendTexTools failed (code {resultCode}).");
                return false;
            }

            success = true;
            return true;
        }
        catch (DllNotFoundException ex)
        {
            if (!_reportedMissingDll)
            {
                _reportedMissingDll = true;
                AddDiagnostic($"Auto-generation disabled: AutoBlendTexTools.dll not found ({ex.Message}).");
            }
            return false;
        }
        catch (Exception ex)
        {
            AddDiagnostic($"'{sourceDiffusePath}': {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            try
            {
                File.Delete(extractedPath);
            }
            catch
            {
                // best-effort cleanup of the temp extraction
            }

            // Directory.CreateDirectory above may have created an empty "statics" folder that a
            // failed generation never populated - remove it so a run that can't generate anything
            // doesn't litter the output textures tree with empty subfolders. Only touches
            // directories this call itself created, and only if still empty, so a folder another
            // texture already populated is never removed.
            if (!success && createdOutputDir)
            {
                try
                {
                    if (Directory.Exists(outputDir) && !Directory.EnumerateFileSystemEntries(outputDir).Any())
                    {
                        Directory.Delete(outputDir);
                    }
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }
    }
}
