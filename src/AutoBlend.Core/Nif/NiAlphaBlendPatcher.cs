using nifly;

namespace AutoBlend.Core.Nif;

public sealed record ShapePatchResult(string ShapeName, ushort OriginalFlags, ushort NewFlags, string? DiffusePathSet);

public sealed record NifPatchResult(string SourcePath, IReadOnlyList<ShapePatchResult> PatchedShapes)
{
    public bool WasModified => PatchedShapes.Count > 0;
}

/// <summary>
/// Patches exactly the shapes the caller identified as in-scope (matching a landscape folder
/// detection) — never shapes merely discovered by re-scanning for NiAlphaProperty, since a mesh
/// can carry other alpha-tested shapes (e.g. an unrelated decal) that were never meant to blend.
/// Flips alpha testing to alpha blending for each targeted shape, and - for shapes whose own
/// embedded default resolves to a "blend" sibling - rewrites the raw diffuse slot to that
/// vanilla-looking identity too (PatchOrchestrator passes the SAME value for a given shape
/// regardless of which record group is being processed, so this never creates a need for more
/// than one physical copy of the mesh: any record with its own Alternate Texture overrides this
/// raw slot completely at runtime anyway, so baking a shared value here is always safe). A shape
/// reachable only via some record's own existing Alternate Texture, with no resolving embedded
/// default of its own, gets its alpha mode flipped only - there's no vanilla identity to bake for
/// it - and PatchOrchestrator derives/assigns its own TextureSet at the ESP level instead.
/// </summary>
public sealed class NiAlphaBlendPatcher
{
    /// <summary>
    /// Patches every shape named in <paramref name="derivedDiffuseByShapeName"/> (alpha test→blend,
    /// plus a diffuse slot rewrite when the value is non-null) on the already-loaded
    /// <paramref name="nifFile"/>, and writes the result to <paramref name="destPath"/> (which may
    /// equal sourcePath for an in-place patch). Takes an already-loaded NifFile rather than loading
    /// sourcePath itself - the caller has always already loaded and scanned this exact file once to
    /// even build derivedDiffuseByShapeName in the first place, so a second load-from-disk here was
    /// a fully redundant nifly parse of every mesh needing a patch. sourcePath is kept only for
    /// NifPatchResult's own reporting field. Returns null if none of the named shapes were found
    /// with a NiAlphaProperty — callers should treat that as "nothing to do" rather than an error.
    /// </summary>
    public NifPatchResult? Patch(NifFile nifFile, string sourcePath, string destPath, IReadOnlyDictionary<string, string?> derivedDiffuseByShapeName)
    {
        var patched = new List<ShapePatchResult>();

        foreach (var shape in nifFile.GetShapes())
        {
            var shapeName = shape.name.get();
            if (!derivedDiffuseByShapeName.TryGetValue(shapeName, out var derivedDiffusePath))
            {
                continue;
            }

            if (derivedDiffusePath is not null)
            {
                nifFile.SetTextureSlot(shape, derivedDiffusePath, 0);
            }

            ushort originalFlags = 0;
            ushort newFlags = 0;
            var flagsChanged = false;

            if (shape.HasAlphaProperty())
            {
                var alphaProperty = nifFile.GetAlphaProperty(shape);
                if (alphaProperty is not null)
                {
                    originalFlags = alphaProperty.flags;
                    if (NiAlphaFlags.IsAlphaTestEnabled(originalFlags))
                    {
                        newFlags = NiAlphaFlags.ToAlphaBlending(originalFlags);
                        alphaProperty.flags = newFlags;
                        flagsChanged = true;
                    }
                    else
                    {
                        newFlags = originalFlags;
                    }
                }
            }

            // Every shape this loop touches ends up alpha-blended (either just now, or already so
            // in the source mesh) with none of the genuinely-translucent surfaces this would harm
            // (glass, ice) ever reaching here - those are excluded via the mesh blacklist earlier
            // in the pipeline. Measured directly against a real modlist: ZBuffer_Write was missing
            // on the majority of already-alpha-blend source shapes too, not just the ones AutoBlend
            // itself flips from alpha-test - so this isn't scoped to flagsChanged. No_Fade joins it
            // for the same reason (stop the shape from dithering out at distance, on top of writing
            // depth correctly). NifFile.GetShader() returns its result typed as the NiShader base
            // class regardless of the block's real type, so downcasting through it silently never
            // matches BSShaderProperty - fetching the block directly by its own ref index is what
            // actually returns the correctly-typed BSLightingShaderProperty instance.
            if (shape.HasShaderProperty())
            {
                var shaderBlockIndex = shape.ShaderPropertyRef().index;
                if (nifFile.GetHeader().GetBlockById(shaderBlockIndex) is BSShaderProperty shaderProperty)
                {
                    var flags2 = shaderProperty.shaderFlags2;
                    if (!BSShaderFlags2.IsZBufferWriteSet(flags2))
                    {
                        flags2 = BSShaderFlags2.WithZBufferWrite(flags2);
                    }

                    if (!BSShaderFlags2.IsNoFadeSet(flags2))
                    {
                        flags2 = BSShaderFlags2.WithNoFade(flags2);
                    }

                    shaderProperty.shaderFlags2 = flags2;
                }
            }

            patched.Add(new ShapePatchResult(shapeName, originalFlags, newFlags, derivedDiffusePath));
            _ = flagsChanged; // diffuse rewrite alone is enough to record the shape as patched
        }

        if (patched.Count == 0)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Explicit, conservative save options — we only want the diffuse slot + alpha flags
        // changed, not nifly's own optimize/sortBlocks passes reshaping the rest of the file.
        var saveOptions = new NifSaveOptions
        {
            optimize = false,
            sortBlocks = false,
        };

        var saveResult = nifFile.Save(destPath, saveOptions);
        if (saveResult != 0)
        {
            throw new InvalidOperationException($"nifly failed to save '{destPath}' (error {saveResult}).");
        }

        return new NifPatchResult(sourcePath, patched);
    }
}
