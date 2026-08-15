using AutoBlend.Core.Configuration;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// PbrNormalPath/PbrHeightPath/PbrRmaosPath are only ever set when generatePbrSlots was on for
/// this detector AND the texture actually has a PBR sibling (see
/// LandscapeFolderDetector.ResolvePbrSlots) - null otherwise, meaning "nothing to add", exactly
/// like every other unused TextureSet slot in this codebase.
/// </summary>
public sealed record LandscapeFolderDetection(
    LandscapeFolderRule Rule,
    string DerivedDiffusePath,
    string? PbrNormalPath = null,
    string? PbrHeightPath = null,
    string? PbrRmaosPath = null);

/// <summary>
/// For a vanilla diffuse texture path such as "textures\landscape\dirt02.dds",
/// "textures\dlc01\landscape\dirt02.dds", or any other category entirely (e.g.
/// "textures\dungeons\mines\rock01.dds"), checks whether a sibling variant exists for any
/// configured <see cref="LandscapeFolderRule"/> — matching is purely structural, not tied to a
/// fixed list of known categories or DLC folders. For a "*\landscape\..." path, the sibling folder
/// is inserted right after "landscape" (e.g. ".../landscape/{FolderName}/westweald/rock.dds",
/// mirroring how landscape texture packs like Vanaheimr/Beyond Skyrim actually ship these); for
/// every other path, it's inserted as the immediate parent of the file itself (e.g.
/// ".../mines/{FolderName}/rock01.dds"), matching how non-landscape retextures (e.g. a cave/mine
/// pack shipping its own "statics" subfolder) place theirs.
/// </summary>
public sealed class LandscapeFolderDetector
{
    private const string LandscapeSegment = "landscape";
    private const string TexturesSegment = "textures";
    private const string PbrSegment = "pbr";

    // Auto-generation is only ever attempted for the "blend" folder - alpha-stripping a diffuse to
    // opaque (see MissingTextureGenerator) is what makes an opaque sibling, so applying that same
    // transform to synthesize a "statics" or "blending" sibling would collide with those two names'
    // own established real-world meaning (hand-authored by mod texture packs, sometimes with a real
    // alpha gradient for "blending" specifically) and potentially overwrite/shadow one. "blend" is
    // AutoBlend's own dedicated output folder, never third-party-authored, so it's safe to own
    // outright. Existing "statics"/"blending"/"blend" siblings are still matched if already present
    // on disk (from any mod) - only synthesis is restricted to "blend".
    private const string GeneratableFolderName = "blend";

    private readonly IReadOnlyList<LandscapeFolderRule> _rules;
    private readonly IGameFileProbe _fileProbe;
    private readonly MissingTextureGenerator? _textureGenerator;
    private readonly IReadOnlyList<string> _autoGenerateAllowlist;
    private readonly bool _generatePbrSlots;

    // Detect() is a pure function of diffusePath given this detector's own immutable rule/allowlist
    // config plus fileProbe/textureGenerator state that only ever monotonically "improves" within a
    // single run (a generated statics texture starts existing and stays that way) - so the very
    // first Detect() call for a given diffusePath already produces the final answer for the rest of
    // the run, and repeating it is redundant. On a real modlist the same handful of landscape
    // textures (e.g. "textures\landscape\dirt02.dds") are the embedded default for thousands of
    // distinct meshes, and a cache miss here previously meant redoing every rule's fileProbe.Exists
    // check (itself a scan across every enabled MO2 mod folder when the file isn't found) plus the
    // AutoGenerateAllowlist wildcard match from scratch each time. Keyed on the raw input string
    // (case-insensitive, matching every other path-keyed cache in this codebase) rather than a
    // further-normalized form - safe either way, just a slightly lower hit rate for callers who mix
    // path conventions for the same texture, never a wrong result.
    private readonly Dictionary<string, LandscapeFolderDetection?> _detectCache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="textureGenerator">
    /// When set, a texture with no existing statics sibling - AND matching
    /// <paramref name="autoGenerateAllowlist"/> - gets one synthesized on the fly (see
    /// <see cref="MissingTextureGenerator"/>) instead of being left undetected - so mod authors no
    /// longer need to hand-author these siblings themselves. Null disables this (e.g. the user
    /// turned the setting off) and falls back to the previous "not found" behavior.
    /// </param>
    /// <param name="autoGenerateAllowlist">
    /// Wildcard patterns gating which source diffuse paths <paramref name="textureGenerator"/> is
    /// allowed to run for - every landscape texture with an alpha-blended shape is structurally
    /// "eligible", but not every one is something a statics variant actually makes sense for, so
    /// generation is opt-in per texture rather than blanket-covering anything eligible. Matched
    /// against the texture's own vanilla identity, never its PBR-swapped path (see
    /// <paramref name="generatePbrSlots"/>) - so existing vanilla-path allowlist entries keep
    /// working unchanged regardless of whether a PBR sibling ends up being used underneath.
    /// </param>
    /// <param name="generatePbrSlots">
    /// When true, every texture this detects first checks whether a PBR sibling exists at the same
    /// relative path with "pbr\" inserted after "textures\" (e.g. "textures\landscape\dirt02.dds"
    /// -> "textures\pbr\landscape\dirt02.dds") - Skyrim PBR texture packs ship at this separate,
    /// parallel location rather than overriding the vanilla path in place, so simply resolving
    /// "whatever wins" never finds them. When a PBR sibling exists, it becomes the effective
    /// diffuse for every file operation below (statics existence/generation), and its own
    /// Normal/Height/RMAOS siblings (Skyrim's own "_n"/"_p"/"_rmaos" suffix convention) are
    /// resolved and surfaced on the returned <see cref="LandscapeFolderDetection"/>.
    /// </param>
    public LandscapeFolderDetector(
        IReadOnlyList<LandscapeFolderRule> rules,
        IGameFileProbe fileProbe,
        MissingTextureGenerator? textureGenerator = null,
        IReadOnlyList<string>? autoGenerateAllowlist = null,
        bool generatePbrSlots = false)
    {
        _rules = rules;
        _fileProbe = fileProbe;
        _textureGenerator = textureGenerator;
        _autoGenerateAllowlist = autoGenerateAllowlist ?? Array.Empty<string>();
        _generatePbrSlots = generatePbrSlots;
    }

    /// <summary>
    /// Returns the first matching rule (in configured order) whose sub-folder exists next to
    /// <paramref name="diffusePath"/>, or null if none of the configured folders exist there (or
    /// can be synthesized - see <see cref="GeneratableFolderName"/>).
    /// </summary>
    /// <param name="diffusePath">
    /// A texture path relative to the Data folder, with or without the leading "textures\" root -
    /// both conventions are in real use here: a NIF's own embedded shader texture path includes it
    /// (e.g. "textures\landscape\statics\rock01.dds"), while a TXST record's GivenPath is relative
    /// to "textures\" and never carries it (e.g. "landscape\rock01.dds") - the same string a
    /// derived TextureSet is built from when a shape already carries an Alternate Texture override
    /// from another mod. Normalizing here (rather than requiring every caller to remember this) is
    /// what a missing-prefix bug looked like before this fix: Detect() always returned null for a
    /// TXST-sourced path, so the caller's own fallback silently substituted the wrong texture.
    /// </param>
    public LandscapeFolderDetection? Detect(string diffusePath)
    {
        if (_detectCache.TryGetValue(diffusePath, out var cached))
        {
            return cached;
        }

        var result = DetectCore(diffusePath);
        _detectCache[diffusePath] = result;
        return result;
    }

    private LandscapeFolderDetection? DetectCore(string diffusePath)
    {
        var vanillaDiffusePath = diffusePath.TrimStart('\\', '/');
        var vanillaSegments = vanillaDiffusePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (vanillaSegments.Length == 0 || !vanillaSegments[0].Equals(TexturesSegment, StringComparison.OrdinalIgnoreCase))
        {
            vanillaDiffusePath = TexturesSegment + "\\" + vanillaDiffusePath;
        }

        // If a PBR sibling exists, every file operation below (statics exists/generate) operates
        // on IT instead - vanillaDiffusePath itself is kept unchanged and still used for allowlist
        // matching below, so a texture's identity for allowlist purposes never depends on whether a
        // PBR pack happens to be installed.
        var effectiveDiffusePath = vanillaDiffusePath;
        string? pbrNormalPath = null;
        string? pbrHeightPath = null;
        string? pbrRmaosPath = null;
        if (_generatePbrSlots)
        {
            var pbrCandidate = ToPbrPath(vanillaDiffusePath);
            if (pbrCandidate is not null && _fileProbe.Exists(pbrCandidate))
            {
                effectiveDiffusePath = pbrCandidate;
                (pbrNormalPath, pbrHeightPath, pbrRmaosPath) = ResolvePbrSlots(pbrCandidate);
            }
        }

        var segments = effectiveDiffusePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            // No containing folder to insert a sibling sub-folder into (e.g. a bare filename with
            // no path at all) - nothing sensible to check.
            return null;
        }

        // Landscape texture packs (Vanaheimr, Beyond Skyrim's westweald, etc.) put statics/blending
        // directly under "landscape" itself, with any deeper original sub-path nested inside it -
        // so for a "*\landscape\..." path, anchor there (verified against real packs). Every other
        // category (architecture, dungeons, clutter - no single established convention across mods)
        // falls back to the simpler, more universal shape: the sibling folder sits as the immediate
        // parent of the file it's a variant of - e.g. a mine/cave retexture shipping its own
        // "textures\dungeons\mines\statics\rock01.dds" next to "textures\dungeons\mines\rock01.dds".
        var landscapeIndex = Array.FindIndex(segments, s => s.Equals(LandscapeSegment, StringComparison.OrdinalIgnoreCase));
        var insertIndex = landscapeIndex >= 0 && landscapeIndex < segments.Length - 1
            ? landscapeIndex + 1
            : segments.Length - 1;

        // The path can already sit inside one of the configured rule folders - e.g. another mod's
        // own Alternate Texture already points straight at the statics/blending/blend variant.
        // Inserting the rule folder a second time would look for a "statics/statics/..." path that
        // can never exist, so recognize this case and treat the path as already-resolved instead.
        // Only meaningful for the landscape-anchored insertion point - in the fallback case,
        // segments[insertIndex] is the file's own name, which can never equal a rule folder name.
        if (insertIndex < segments.Length - 1)
        {
            var immediateRule = _rules.FirstOrDefault(r => segments[insertIndex].Equals(r.FolderName, StringComparison.OrdinalIgnoreCase));
            if (immediateRule is not null)
            {
                return new LandscapeFolderDetection(immediateRule, effectiveDiffusePath, pbrNormalPath, pbrHeightPath, pbrRmaosPath);
            }
        }

        foreach (var rule in _rules)
        {
            var candidateSegments = segments.ToList();
            candidateSegments.Insert(insertIndex, rule.FolderName);
            var candidatePath = string.Join('\\', candidateSegments);

            if (_fileProbe.Exists(candidatePath))
            {
                return new LandscapeFolderDetection(rule, candidatePath, pbrNormalPath, pbrHeightPath, pbrRmaosPath);
            }

            // Nothing already provides this sibling - synthesize one from whatever diffuse is
            // actually winning in the load order (or its PBR sibling, see effectiveDiffusePath
            // above), so a mod author never has to hand-author it (see MissingTextureGenerator's
            // own doc comment for why this is safe: verified to reproduce Vanaheimr's own
            // hand-authored statics textures almost pixel-for-pixel). Only for "blend" (see
            // GeneratableFolderName above) and only for textures explicitly listed in
            // AutoGenerateAllowlist - every alpha-tested shape with a diffuse texture is
            // structurally "eligible" here, but generating for all of them indiscriminately produced
            // far more textures than intended (including ones with no real source, or ones an opaque
            // variant doesn't actually make sense for), so this is opt-in per texture, not blanket.
            var canGenerate = rule.FolderName.Equals(GeneratableFolderName, StringComparison.OrdinalIgnoreCase)
                && WildcardMatcher.MatchesAny(vanillaDiffusePath, _autoGenerateAllowlist);
            if (canGenerate && _textureGenerator is not null && _textureGenerator.TryGenerate(effectiveDiffusePath, candidatePath, out _))
            {
                return new LandscapeFolderDetection(rule, candidatePath, pbrNormalPath, pbrHeightPath, pbrRmaosPath);
            }
        }

        return null;
    }

    /// <summary>
    /// Inserts "pbr" as the segment right after "textures" - e.g.
    /// "textures\landscape\dirt02.dds" -> "textures\pbr\landscape\dirt02.dds" - matching where PBR
    /// texture packs (e.g. Vanaheimr's PBR variants) actually ship their own content: a separate,
    /// parallel location, not an in-place override of the vanilla path. Returns null if the path is
    /// already pbr-prefixed (nothing to swap) or too short to have a second segment.
    /// </summary>
    private static string? ToPbrPath(string texturesPrefixedPath)
    {
        var segments = texturesPrefixedPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || segments[1].Equals(PbrSegment, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var pbrSegments = new List<string>(segments.Length + 1) { segments[0], PbrSegment };
        pbrSegments.AddRange(segments.Skip(1));
        return string.Join('\\', pbrSegments);
    }

    /// <summary>
    /// Skyrim's PBR naming convention: Normal/Height/RMAOS live next to the diffuse, sharing its
    /// exact basename with a fixed suffix ("_n"/"_p"/"_rmaos") before the extension - verified
    /// directly against a real PBR texture pack's own files (e.g. "dirt02.dds" + "dirt02_n.dds" +
    /// "dirt02_p.dds" + "dirt02_rmaos.dds", all in the same folder). A "statics" sibling only ever
    /// carries its own diffuse - it shares this same parent's Normal/Height/RMAOS - so this always
    /// resolves against the PARENT (non-statics) pbr diffuse path, never the derived statics one.
    /// Each slot is only included if that specific file actually exists; not every PBR texture
    /// ships all three maps.
    /// </summary>
    private (string? Normal, string? Height, string? Rmaos) ResolvePbrSlots(string pbrDiffusePath)
    {
        var dir = Path.GetDirectoryName(pbrDiffusePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(pbrDiffusePath);
        var ext = Path.GetExtension(pbrDiffusePath);

        string? Resolve(string suffix)
        {
            var candidate = Path.Combine(dir, stem + suffix + ext);
            return _fileProbe.Exists(candidate) ? candidate : null;
        }

        return (Resolve("_n"), Resolve("_p"), Resolve("_rmaos"));
    }
}
