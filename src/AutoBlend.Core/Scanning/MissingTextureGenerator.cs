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

    private readonly IGameFileProbe _fileProbe;
    private readonly string _outputLocation;
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _mirroredPbrJsonPaths = new(StringComparer.OrdinalIgnoreCase);
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
    /// just resolved: same material parameters and explicit slot overrides untouched, only the
    /// "texture"/"match_diffuse" field(s) updated to the nested path, written into AutoBlend's own
    /// output under the matching "PBRNifPatcher\..." location (the same convention every mod above
    /// follows, since PG Patcher discovers jsons by scanning that whole subtree, not by an index).
    /// Does nothing if the nested variant already has its own dedicated json (some packs, like
    /// Vanaheimr, ship one explicitly even though their own bare-filename convention wouldn't have
    /// needed it). If there's no json living at the exact path our own convention would compute,
    /// falls back to searching every PBRNifPatcher json anywhere in the load order for an entry
    /// that already covers the parent texture (see TryFindExistingPbrEntry) - this is what actually
    /// catches a pack like "Sloppy Vanilla Landscapes PBR", which ships ALL of its entries bundled
    /// into one arbitrarily-named combined json rather than one file per texture. Only as a last
    /// resort, when nothing anywhere already describes this texture, does it author a fresh default
    /// (see TryAuthorDefaultPbrJson) rather than leaving PG Patcher with nothing.
    /// </summary>
    public void TryMirrorPbrJson(string parentPbrDiffusePath, string nestedPbrDiffusePath)
    {
        var targetJsonPath = ToPbrJsonPath(nestedPbrDiffusePath);
        if (targetJsonPath is null || _mirroredPbrJsonPaths.Contains(targetJsonPath) || _fileProbe.Exists(targetJsonPath))
        {
            return;
        }

        var sourceJsonPath = ToPbrJsonPath(parentPbrDiffusePath);
        if (sourceJsonPath is null || !_fileProbe.Exists(sourceJsonPath))
        {
            var parentIdentity = StripTexturesPbrPrefixAndExtension(parentPbrDiffusePath);
            var existingEntry = parentIdentity is not null ? TryFindExistingPbrEntry(parentIdentity) : null;
            if (existingEntry is not null)
            {
                CloneEntryToTarget(existingEntry, nestedPbrDiffusePath, targetJsonPath);
                return;
            }

            // No existing json anywhere to clone from either - matches AutoSeasons' own situation
            // for every seasonal variant it generates (a texture that structurally can't have a
            // pre-existing PBR config, since AutoSeasons only just invented it). Author a fresh one
            // instead of leaving PG Patcher with nothing to match at all.
            TryAuthorDefaultPbrJson(nestedPbrDiffusePath);
            return;
        }

        try
        {
            JsonNode? json;
            using (var source = _fileProbe.OpenRead(sourceJsonPath))
            {
                json = JsonNode.Parse(source);
            }

            var entries = json as JsonArray ?? json?["entries"] as JsonArray;
            if (entries is null || entries.Count == 0)
            {
                return;
            }

            var nestedMatchValue = StripTexturesPbrPrefixAndExtension(nestedPbrDiffusePath);
            foreach (var entry in entries)
            {
                if (entry is not JsonObject obj)
                {
                    continue;
                }

                if (obj.ContainsKey("texture"))
                {
                    obj["texture"] = nestedMatchValue;
                }

                if (obj.ContainsKey("match_diffuse"))
                {
                    obj["match_diffuse"] = nestedMatchValue;
                }
            }

            var destFullPath = Path.Combine(_outputLocation, targetJsonPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destFullPath)!);
            File.WriteAllText(destFullPath, json!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            _mirroredPbrJsonPaths.Add(targetJsonPath);
            MirroredJsonCount++;
        }
        catch (Exception ex)
        {
            AddDiagnostic($"Could not mirror PBRNifPatcher json for '{nestedPbrDiffusePath}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Searches every json found under "PBRNifPatcher\" anywhere in the load order (via
    /// <see cref="IGameFileProbe.EnumerateFiles"/> - loose files across every active mod, not just
    /// one computed path) for an entry whose own "texture"/"match_diffuse" value already covers
    /// <paramref name="vanillaIdentity"/> (e.g. "landscape\rocks01"). Needed because not every pack
    /// follows the "one json file per texture, named after it" convention <see cref="ToPbrJsonPath"/>
    /// assumes - "Sloppy Vanilla Landscapes PBR" was found shipping ALL of its entries bundled into
    /// a single "Sloppy Vanilla Landscapes.json", so a plain Exists() check against the computed
    /// per-texture path never finds it even though the exact entry we need is right there. Indexed
    /// once (lazily) under both the full identity and its bare filename, matching the two real
    /// conventions PG Patcher's own suffix-match already tolerates (see TryMirrorPbrJson's own doc
    /// comment on the Vanaheimr/TomatoRim distinction). Returns null if nothing anywhere covers it.
    /// </summary>
    private JsonObject? TryFindExistingPbrEntry(string vanillaIdentity)
    {
        EnsurePbrJsonEntryIndexBuilt();

        var normalized = NormalizeMatchValue(vanillaIdentity);
        if (_pbrJsonEntryIndex!.TryGetValue(normalized, out var json))
        {
            return JsonNode.Parse(json) as JsonObject;
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
    /// into a fresh json covering <paramref name="nestedPbrDiffusePath"/> - same tuned material
    /// parameters (roughness, displacement, glint, etc.) as whatever already describes the parent
    /// texture, only the match key itself updated. "rename" is stripped since that field redirects
    /// to the SOURCE entry's own alternate identity, which has no meaning for our own new path.</summary>
    private void CloneEntryToTarget(JsonObject sourceEntry, string nestedPbrDiffusePath, string targetJsonPath)
    {
        try
        {
            var clone = JsonNode.Parse(sourceEntry.ToJsonString()) as JsonObject;
            var nestedMatchValue = StripTexturesPbrPrefixAndExtension(nestedPbrDiffusePath);

            clone!.Remove("rename");
            if (clone.ContainsKey("texture"))
            {
                clone["texture"] = nestedMatchValue;
            }
            else if (clone.ContainsKey("match_diffuse"))
            {
                clone["match_diffuse"] = nestedMatchValue;
            }
            else
            {
                clone["match_diffuse"] = nestedMatchValue;
            }

            var json = new JsonArray(clone);
            var destFullPath = Path.Combine(_outputLocation, targetJsonPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destFullPath)!);
            File.WriteAllText(destFullPath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            _mirroredPbrJsonPaths.Add(targetJsonPath);
            MirroredJsonCount++;
        }
        catch (Exception ex)
        {
            AddDiagnostic($"Could not clone an existing PBRNifPatcher entry for '{nestedPbrDiffusePath}': {ex.GetType().Name}: {ex.Message}");
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
    /// Authors a fresh PBRNifPatcher json for <paramref name="pbrDiffusePath"/> from scratch, using
    /// the same default TruePBR material parameters and "match_diffuse: bare-filename.dds" key
    /// convention AutoSeasons' own equivalent generator already uses for its own seasonal texture
    /// variants (verified directly against a real "AutoSeasons Output" json) - same author, same
    /// underlying PG Patcher target, no reason to invent a second convention. Used as the last-resort
    /// fallback when there's no existing json anywhere to mirror parameters from (see
    /// TryMirrorPbrJson) - a generic, reasonable-default material is still far better than PG
    /// Patcher's own bare "mark as PBR, no parameters" fallback for a texture with zero prior art.
    /// </summary>
    private void TryAuthorDefaultPbrJson(string pbrDiffusePath)
    {
        var targetJsonPath = ToPbrJsonPath(pbrDiffusePath);
        if (targetJsonPath is null || _mirroredPbrJsonPaths.Contains(targetJsonPath) || _fileProbe.Exists(targetJsonPath))
        {
            return;
        }

        try
        {
            var matchDiffuse = Path.GetFileName(pbrDiffusePath);
            var entry = new JsonObject
            {
                ["match_diffuse"] = matchDiffuse,
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
            var json = new JsonArray(entry);

            var destFullPath = Path.Combine(_outputLocation, targetJsonPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destFullPath)!);
            File.WriteAllText(destFullPath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            _mirroredPbrJsonPaths.Add(targetJsonPath);
            MirroredJsonCount++;
        }
        catch (Exception ex)
        {
            AddDiagnostic($"Could not author a default PBRNifPatcher json for '{pbrDiffusePath}': {ex.GetType().Name}: {ex.Message}");
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

    /// <summary>"textures\pbr\landscape\statics\dirt02.dds" -&gt; "PBRNifPatcher\landscape\statics\dirt02.json",
    /// following the same convention every real json in a PBR pack was found to follow.</summary>
    private static string? ToPbrJsonPath(string pbrDiffusePath)
    {
        var stripped = StripTexturesPbrPrefixAndExtension(pbrDiffusePath);
        return stripped is null ? null : Path.Combine("PBRNifPatcher", stripped) + ".json";
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
