using AutoBlend.Core.Configuration;
using AutoBlend.Core.Nif;
using AutoBlend.Core.Plugin;
using AutoBlend.Core.Scanning;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using nifly;

namespace AutoBlend.Core.Pipeline;

/// <summary>
/// End-to-end driver: scan Static and MoveableStatic records for landscape statics/blending
/// texture variants, patch the NIF alpha blend flag, and generate the derived TextureSets +
/// Alternate Texture overrides in a dedicated output plugin.
/// </summary>
public sealed class PatchOrchestrator
{
    private const string PatchModName = "AutoBlend";

    private enum RecordKind
    {
        Static,
        MoveableStatic,
    }

    private enum ShapeTreatmentKind
    {
        /// <summary>Neither this record's own Alternate Texture (if any) nor the mesh's own
        /// embedded default resolves to a statics/blending sibling - leave this shape alone.</summary>
        Untouched,

        /// <summary>The mesh's own embedded (vanilla, no-override) diffuse resolves - uniform
        /// across every record sharing this mesh, since it does not depend on any per-record
        /// override.</summary>
        BaseDerived,

        /// <summary>This specific record already carries an Alternate Texture on this shape, and
        /// THAT texture set's Diffuse resolves - record-specific, independent of what the mesh's
        /// own embedded default is.</summary>
        AltTexDerived,
    }

    /// <summary>One shape with NiAlphaProperty found on a mesh, plus what its own embedded
    /// (no-override) diffuse resolves to, if anything.</summary>
    private sealed record AlphaShape(int ShapeIndex, string EmbeddedDiffuse, LandscapeFolderDetection? EmbeddedDetection, SourceTexturePaths EmbeddedSlots);

    /// <summary>What one record wants done to one shape of a shared mesh.</summary>
    private sealed record ShapeTreatment(ShapeTreatmentKind Kind, int ShapeIndex, LandscapeFolderDetection? Detection, SourceTexturePaths? Source);

    private readonly PatcherSettings _settings;

    public PatchOrchestrator(PatcherSettings settings)
    {
        _settings = settings;
    }

    public PatchRunResult Run(Action<PatchProgress>? progress = null)
    {
        void Report(string message, int current = 0, int total = 0) => progress?.Invoke(new PatchProgress(message, current, total));
        var warnings = new List<string>();

        var gameRelease = ToGameRelease(_settings.GameType);
        var skyrimRelease = ToSkyrimRelease(_settings.GameType);
        // Game Location (matching the AutoSeasons convention) is the game's install root, e.g.
        // "...\Skyrim Special Edition" — Mutagen and our file probes both want the Data folder.
        var dataFolder = Path.Combine(_settings.GameLocation, "Data");

        using var mo2Reader = _settings.ModManager == ModManagerType.ModOrganizer2
            ? new Mo2InstanceReader(_settings.Mo2InstancePath, _settings.Mo2ProfileName, gameRelease)
            : null;

        // Mutagen loads every plugin from one physical Data folder — it has no notion of MO2's
        // per-mod folders. For MO2 we materialize just the active plugins.txt entries (resolved
        // through the same overwrite/priority order as the file probe) into a throwaway folder in
        // plugins.txt's order, so the winning-override resolution below matches what MO2 actually
        // shows in-game. Meshes/textures are NOT materialized — those stay served live through
        // Mo2ModlistFileProbe further down.
        Report("Loading game environment...");
        using var materializedLoadOrder = mo2Reader is not null
            ? Mo2LoadOrderMaterializer.Materialize(mo2Reader, _settings.Mo2ProfileName, dataFolder, warnings)
            : null;

        var envDataFolder = materializedLoadOrder?.DataFolder ?? dataFolder;
        var envBuilder = GameEnvironment.Typical.Builder(gameRelease).WithTargetDataFolder(envDataFolder);
        using var env = materializedLoadOrder is not null
            ? envBuilder.WithLoadOrder(materializedLoadOrder.LoadOrder.ToArray()).Build()
            : envBuilder.Build();

        using IGameFileProbe fileProbe = mo2Reader is not null
            ? new Mo2ModlistFileProbe(mo2Reader, dataFolder, _settings.GameType)
            : new ArchiveAwareFileProbe(dataFolder, _settings.GameType);

        // Refuse to run while a previous run's own output is still active in the merged Data view
        // (e.g. the "AutoBlend Output" mod is still enabled in MO2). Scanning its own previous
        // output as if it were a real mod means every Alternate Texture this run assigns derives
        // from what THIS tool generated last time rather than the real source mod - each run
        // compounds on the last one (naming the derived TextureSet after its own already-derived
        // name, deriving diffuse detection from an already-derived diffuse, etc). Checking for the
        // exact output plugin filename is a light heuristic (matches AutoSeasons' own equivalent
        // guard, done there via a stricter same-physical-file check) but sufficient here since
        // "{PatchModName}.esp" is a name only this tool ever produces.
        var previousOutputEspPath = Path.Combine(_settings.OutputLocation, $"{PatchModName}.esp");
        if (File.Exists(previousOutputEspPath) && fileProbe.Exists($"{PatchModName}.esp"))
        {
            throw new InvalidOperationException(
                $"AutoBlend's own output ({PatchModName}.esp) is still enabled in your mod manager. "
                + "Please disable it (e.g. the \"AutoBlend Output\" mod in MO2) before starting a new "
                + "generation, then re-enable it once this run has finished - otherwise this run would "
                + "scan its own previous output as if it were a real mod, producing incorrect results.");
        }

        MissingTextureGenerator? textureGenerator = null;
        if (_settings.AutoGenerateMissingStatics)
        {
            textureGenerator = new MissingTextureGenerator(fileProbe, _settings.OutputLocation);
        }

        var folderDetector = new LandscapeFolderDetector(
            _settings.LandscapeFolderRules, fileProbe, textureGenerator, _settings.AutoGenerateAllowlist, _settings.GeneratePbrSlots);
        var blacklist = new BlacklistEvaluator(_settings);

        // Runs before any mesh/record scanning, as its own explicit phase: the allowlist is a
        // small, fixed, known-upfront list (unlike everything else here, which is only discovered
        // by scanning), so there's no reason to wait for a mesh to lazily reference one of these
        // textures before generating it. Detect() itself does the actual exists-check/generate -
        // MissingTextureGenerator's own per-target-path cache means the per-mesh scan below hitting
        // the exact same textures later is then just a fast cache lookup, not a second attempt.
        if (textureGenerator is not null)
        {
            var allowlist = _settings.AutoGenerateAllowlist;
            for (var i = 0; i < allowlist.Count; i++)
            {
                Report($"Generating missing statics textures... ({i + 1}/{allowlist.Count})", i + 1, allowlist.Count);
                folderDetector.Detect(allowlist[i]);
            }
        }

        var patchMod = new SkyrimMod(new ModKey(PatchModName, ModType.Plugin), skyrimRelease);
        var txstFactory = new DerivedTextureSetFactory(patchMod, _settings.TextureSetNamingTemplate);
        var derivedTxstCache = new Dictionary<string, TextureSet>(StringComparer.OrdinalIgnoreCase);

        Report("Scanning Static and MoveableStatic records...");
        var meshIndex = new MeshUsageIndex();
        var candidatesByMesh = new Dictionary<string, List<FormKey>>(StringComparer.OrdinalIgnoreCase);
        var recordKinds = new Dictionary<FormKey, RecordKind>();
        var recordsScanned = 0;

        void ScanRecord(FormKey formKey, string? editorId, string? meshPath, RecordKind kind)
        {
            recordsScanned++;
            if (string.IsNullOrEmpty(meshPath))
            {
                return;
            }

            recordKinds[formKey] = kind;

            var isBlacklisted = blacklist.IsMeshBlacklisted(meshPath) || blacklist.IsEditorIdBlacklisted(editorId ?? string.Empty);
            meshIndex.Record(meshPath, formKey, isBlacklisted);

            if (isBlacklisted)
            {
                return;
            }

            if (!candidatesByMesh.TryGetValue(meshPath, out var list))
            {
                list = new List<FormKey>();
                candidatesByMesh[meshPath] = list;
            }
            list.Add(formKey);
        }

        foreach (var stat in env.LoadOrder.PriorityOrder.WinningOverrides<IStaticGetter>())
        {
            ScanRecord(stat.FormKey, stat.EditorID, stat.Model?.File.GivenPath, RecordKind.Static);
        }

        foreach (var mstt in env.LoadOrder.PriorityOrder.WinningOverrides<IMoveableStaticGetter>())
        {
            ScanRecord(mstt.FormKey, mstt.EditorID, mstt.Model?.File.GivenPath, RecordKind.MoveableStatic);
        }

        Report($"{recordsScanned} Static/MoveableStatic record(s) scanned, {candidatesByMesh.Count} distinct mesh(es) not blacklisted.");

        var tempDir = Path.Combine(Path.GetTempPath(), "AutoBlend_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var meshesPatchedInPlace = 0;
        var meshesDuplicated = 0;
        var altTexAssigned = 0;

        try
        {
            var totalMeshes = candidatesByMesh.Count;
            var meshCounter = 0;
            foreach (var (meshPath, candidateFormKeys) in candidatesByMesh)
            {
                meshCounter++;
                // Texture generation happens lazily inside folderDetector.Detect() below, as part
                // of this same per-mesh pass - folding its running count into this same status line
                // (rather than a separate phase) is what makes it visible during the run at all,
                // since the native/WPF shells only ever surface Report()'s live Status text, not
                // the final PatchRunResult, until the run completes.
                var textureGenSuffix = textureGenerator is not null
                    ? $" - {textureGenerator.GeneratedCount} texture(s) generated"
                    : string.Empty;
                Report($"Patching meshes... ({meshCounter}/{totalMeshes}){textureGenSuffix}", meshCounter, totalMeshes);

                var plan = meshIndex.GetPlan(meshPath);
                if (!plan.ShouldPatch)
                {
                    continue;
                }

                var meshRelativeToData = Path.Combine("meshes", meshPath);
                string extractedPath;
                try
                {
                    extractedPath = ExtractToTemp(fileProbe, meshRelativeToData, tempDir);
                }
                catch (FileNotFoundException)
                {
                    warnings.Add($"Mesh not found, skipped: {meshPath}");
                    continue;
                }

                // Every shape with NiAlphaProperty, regardless of whether the mesh's OWN embedded
                // (no-override) diffuse resolves - a shape whose default doesn't match can still be
                // in scope for a specific record via that record's own Alternate Texture (see
                // ClassifyRecord below), so filtering here would silently drop those records before
                // their AltTex is ever even looked at.
                var alphaShapes = new Dictionary<string, AlphaShape>(StringComparer.OrdinalIgnoreCase);
                using (var readNif = new NifFile())
                {
                    if (readNif.Load(extractedPath) != 0)
                    {
                        warnings.Add($"nifly failed to load, skipped: {meshPath}");
                        continue;
                    }

                    var allShapes = readNif.GetShapes();
                    for (var shapeIndex = 0; shapeIndex < allShapes.Count; shapeIndex++)
                    {
                        var shape = allShapes[shapeIndex];
                        if (!shape.HasAlphaProperty())
                        {
                            continue;
                        }

                        var slots = NifShapeTextureResolver.GetAllSlots(readNif, shape);
                        if (string.IsNullOrEmpty(slots.Diffuse))
                        {
                            continue;
                        }

                        var detection = folderDetector.Detect(slots.Diffuse);
                        alphaShapes[shape.name.get()] = new AlphaShape(shapeIndex, slots.Diffuse, detection, slots);
                    }
                }

                if (alphaShapes.Count == 0)
                {
                    continue;
                }

                // Classify every candidate record's per-shape treatment: does IT (via its own
                // Alternate Texture, or failing that the mesh's own embedded default) end up
                // displaying a texture with a statics/blending sibling? This has to be evaluated per
                // record because two records sharing this exact mesh can carry completely different
                // Alternate Textures on the same shape (e.g. one region's variant has a statics
                // match, another region's variant does not) - the mesh's own embedded default is
                // only what a record with NO override on that shape falls back to.
                var perRecordTreatment = new Dictionary<FormKey, Dictionary<string, ShapeTreatment>>();
                foreach (var formKey in candidateFormKeys)
                {
                    perRecordTreatment[formKey] = ClassifyRecord(formKey, recordKinds[formKey], alphaShapes, env, folderDetector, warnings);
                }

                // Base-derived shapes (embedded default resolves) get baked + alpha-converted
                // identically for EVERY record sharing this mesh, regardless of what any given
                // record's own Alternate Texture situation is - an unrelated or non-resolving
                // AltTex on that same shape does not change what the physical file needs, since
                // AltTex only overrides which TXST is displayed, never the alpha-blend mode. So the
                // only thing that can ever require a record to diverge onto its own physical
                // duplicate is a shape where the base does NOT resolve but THIS record's own AltTex
                // does - grouping only needs to key on the SET of such shape names (not which
                // specific texture each resolves to, since two records deriving from different
                // source textures on the same shape still need the identical physical treatment:
                // alpha-mode flipped, no diffuse baked, the ESP-level AltTex supplies the diffuse).
                var groups = new Dictionary<string, List<FormKey>>();
                foreach (var (formKey, treatment) in perRecordTreatment)
                {
                    var altTexShapeNames = treatment
                        .Where(kv => kv.Value.Kind == ShapeTreatmentKind.AltTexDerived)
                        .Select(kv => kv.Key)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var hasAnyBenefit = altTexShapeNames.Count > 0 || treatment.Values.Any(t => t.Kind == ShapeTreatmentKind.BaseDerived);
                    if (!hasAnyBenefit)
                    {
                        continue;
                    }

                    var signature = string.Join(",", altTexShapeNames);
                    if (!groups.TryGetValue(signature, out var list))
                    {
                        list = new List<FormKey>();
                        groups[signature] = list;
                    }
                    list.Add(formKey);
                }

                if (groups.Count == 0)
                {
                    continue;
                }

                var patcher = new NiAlphaBlendPatcher();
                var variantIndex = 0;

                foreach (var (signature, formKeys) in groups)
                {
                    var alphaConversionByShapeName = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (shapeName, alphaShape) in alphaShapes)
                    {
                        if (alphaShape.EmbeddedDetection is not null)
                        {
                            alphaConversionByShapeName[shapeName] = alphaShape.EmbeddedDetection.DerivedDiffusePath;
                        }
                    }

                    var isDefaultGroup = signature.Length == 0;
                    if (!isDefaultGroup)
                    {
                        foreach (var shapeName in signature.Split(','))
                        {
                            // Alpha mode only - the ESP-level Alternate Texture supplies the diffuse.
                            alphaConversionByShapeName[shapeName] = null;
                        }
                    }

                    // The "default" group (no record-specific AltTex-driven shape) can reuse the
                    // shared mesh path, exactly like before - but only if no blacklisted record also
                    // shares this path (plan.PatchInPlace already reflects that). Any group with at
                    // least one AltTexDerived shape always needs its own duplicate, since only
                    // records in that group should get that shape's alpha mode flipped.
                    var canUseSharedPath = isDefaultGroup && plan.PatchInPlace;

                    string outputMeshPath;
                    string? duplicateRelPath = null;
                    if (canUseSharedPath)
                    {
                        outputMeshPath = Path.Combine(_settings.OutputLocation, meshRelativeToData);
                    }
                    else
                    {
                        duplicateRelPath = MeshUsageIndex.BuildDuplicateMeshPath(meshPath, variantIndex++);
                        outputMeshPath = Path.Combine(_settings.OutputLocation, "meshes", duplicateRelPath);
                    }

                    var patchResult = patcher.Patch(extractedPath, outputMeshPath, alphaConversionByShapeName);
                    if (patchResult is not null)
                    {
                        if (duplicateRelPath is not null)
                        {
                            meshesDuplicated++;
                        }
                        else
                        {
                            meshesPatchedInPlace++;
                        }
                    }

                    foreach (var formKey in formKeys)
                    {
                        Model? model = null;
                        if (duplicateRelPath is not null)
                        {
                            model = GetOrCreateOverrideModel(formKey, recordKinds[formKey], patchMod, env, warnings);
                            if (model is null)
                            {
                                continue;
                            }

                            model.File.GivenPath = duplicateRelPath;
                        }

                        var treatment = perRecordTreatment[formKey];
                        var hasAltTex = treatment.Values.Any(t => t.Kind == ShapeTreatmentKind.AltTexDerived);
                        if (!hasAltTex)
                        {
                            // Benefits purely from the physical mesh (shared path or duplicate) -
                            // nothing needs to change at the ESP level for this record.
                            continue;
                        }

                        model ??= GetOrCreateOverrideModel(formKey, recordKinds[formKey], patchMod, env, warnings);
                        if (model is null)
                        {
                            continue;
                        }

                        foreach (var (shapeName, t) in treatment)
                        {
                            if (t.Kind != ShapeTreatmentKind.AltTexDerived)
                            {
                                continue;
                            }

                            var cacheKey = $"{t.Detection!.Rule.FolderName}|{t.Source!.SourceName}";
                            if (!derivedTxstCache.TryGetValue(cacheKey, out var derivedTxst))
                            {
                                derivedTxst = txstFactory.CreateDerived(t.Source!, t.Detection!);
                                derivedTxstCache[cacheKey] = derivedTxst;
                            }

                            AlternateTextureAssigner.Assign(model, shapeName, t.ShapeIndex, derivedTxst);
                            altTexAssigned++;
                        }
                    }
                }
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }

        if (textureGenerator is not null)
        {
            Report($"Auto-generated {textureGenerator.GeneratedCount} missing statics/blending texture(s)"
                + (textureGenerator.FailedCount > 0 ? $" ({textureGenerator.FailedCount} attempt(s) failed - see log)." : "."));
            warnings.AddRange(textureGenerator.Diagnostics);
        }

        string? outputEspPath = null;
        if (derivedTxstCache.Count > 0 || altTexAssigned > 0)
        {
            Report("Writing patch plugin...");
            Directory.CreateDirectory(_settings.OutputLocation);
            outputEspPath = Path.Combine(_settings.OutputLocation, $"{PatchModName}.esp");
            patchMod.BeginWrite.ToPath(outputEspPath).WithNoLoadOrder().Write();
        }

        return new PatchRunResult(
            recordsScanned,
            meshesPatchedInPlace,
            meshesDuplicated,
            derivedTxstCache.Count,
            altTexAssigned,
            warnings,
            outputEspPath);
    }

    /// <summary>
    /// For one record sharing a mesh, decides per alpha-property shape whether it ends up
    /// displaying a statics/blending-covered texture: via that record's own pre-existing Alternate
    /// Texture if it has one on this shape, otherwise via the mesh's own embedded default.
    /// </summary>
    private static Dictionary<string, ShapeTreatment> ClassifyRecord(
        FormKey formKey,
        RecordKind kind,
        Dictionary<string, AlphaShape> alphaShapes,
        IGameEnvironment env,
        LandscapeFolderDetector folderDetector,
        List<string> warnings)
    {
        var result = new Dictionary<string, ShapeTreatment>(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<IAlternateTextureGetter>? existingAltTexs = kind switch
        {
            RecordKind.Static => env.LinkCache.TryResolve<IStaticGetter>(formKey, out var s) ? s.Model?.AlternateTextures : null,
            _ => env.LinkCache.TryResolve<IMoveableStaticGetter>(formKey, out var m) ? m.Model?.AlternateTextures : null,
        };

        foreach (var (shapeName, alphaShape) in alphaShapes)
        {
            // AlternateTexture.Name is a CK-authored label that can go stale if the mesh's shapes
            // were ever renamed/reordered after the override was created - Index (the shape's
            // position among this NIF's own shape list) is what the game engine actually resolves
            // by, so it's the authoritative fallback whenever the name doesn't match anything.
            var existingAltTex = existingAltTexs?.FirstOrDefault(a => a.Name == shapeName)
                ?? existingAltTexs?.FirstOrDefault(a => a.Index == alphaShape.ShapeIndex);

            if (existingAltTex is not null)
            {
                if (env.LinkCache.TryResolve<ITextureSetGetter>(existingAltTex.NewTexture.FormKey, out var existingTxst))
                {
                    var source = SourceTexturePaths.FromTextureSet(existingTxst);
                    var detection = !string.IsNullOrEmpty(source.Diffuse) ? folderDetector.Detect(source.Diffuse) : null;
                    result[shapeName] = detection is not null
                        ? new ShapeTreatment(ShapeTreatmentKind.AltTexDerived, alphaShape.ShapeIndex, detection, source)
                        : new ShapeTreatment(ShapeTreatmentKind.Untouched, alphaShape.ShapeIndex, null, null);
                }
                else
                {
                    // The Alternate Texture points at a FormKey that doesn't resolve in this
                    // environment (missing/masked plugin) - there is nothing correct to derive, so
                    // leave it untouched rather than guessing from the mesh's unrelated default.
                    warnings.Add($"Could not resolve existing TextureSet referenced by shape '{shapeName}' on record {formKey} - left untouched.");
                    result[shapeName] = new ShapeTreatment(ShapeTreatmentKind.Untouched, alphaShape.ShapeIndex, null, null);
                }
            }
            else if (alphaShape.EmbeddedDetection is not null)
            {
                result[shapeName] = new ShapeTreatment(ShapeTreatmentKind.BaseDerived, alphaShape.ShapeIndex, alphaShape.EmbeddedDetection, alphaShape.EmbeddedSlots);
            }
            else
            {
                result[shapeName] = new ShapeTreatment(ShapeTreatmentKind.Untouched, alphaShape.ShapeIndex, null, null);
            }
        }

        return result;
    }

    private static Model? GetOrCreateOverrideModel(FormKey recordFormKey, RecordKind kind, SkyrimMod patchMod, IGameEnvironment env, List<string> warnings)
    {
        if (kind == RecordKind.Static)
        {
            if (!env.LinkCache.TryResolve<IStaticGetter>(recordFormKey, out var winning))
            {
                warnings.Add($"Could not resolve Static record {recordFormKey}, skipped.");
                return null;
            }

            return patchMod.Statics.GetOrAddAsOverride(winning).Model;
        }

        if (!env.LinkCache.TryResolve<IMoveableStaticGetter>(recordFormKey, out var winningMstt))
        {
            warnings.Add($"Could not resolve MoveableStatic record {recordFormKey}, skipped.");
            return null;
        }

        return patchMod.MoveableStatics.GetOrAddAsOverride(winningMstt).Model;
    }

    private static string ExtractToTemp(IGameFileProbe fileProbe, string relativeDataPath, string tempDir)
    {
        using var source = fileProbe.OpenRead(relativeDataPath);
        var destPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + Path.GetExtension(relativeDataPath));
        using var dest = File.Create(destPath);
        source.CopyTo(dest);
        return destPath;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort cleanup of the temp extraction folder
        }
    }

    private static GameRelease ToGameRelease(GameType gameType) => gameType switch
    {
        GameType.SkyrimSE => GameRelease.SkyrimSE,
        GameType.SkyrimLE => GameRelease.SkyrimLE,
        _ => throw new ArgumentOutOfRangeException(nameof(gameType), gameType, null),
    };

    private static SkyrimRelease ToSkyrimRelease(GameType gameType) => gameType switch
    {
        GameType.SkyrimSE => SkyrimRelease.SkyrimSE,
        GameType.SkyrimLE => SkyrimRelease.SkyrimLE,
        _ => throw new ArgumentOutOfRangeException(nameof(gameType), gameType, null),
    };
}
