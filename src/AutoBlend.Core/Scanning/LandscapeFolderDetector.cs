using AutoBlend.Core.Configuration;

namespace AutoBlend.Core.Scanning;

public sealed record LandscapeFolderDetection(LandscapeFolderRule Rule, string DerivedDiffusePath);

/// <summary>
/// For a vanilla diffuse texture path such as "textures\landscape\dirt02.dds" or
/// "textures\dlc01\landscape\dirt02.dds", checks whether a sibling variant exists at
/// ".../landscape/{FolderName}/dirt02.dds" for any configured <see cref="LandscapeFolderRule"/>
/// (base game, DLC, or mod-added "*\landscape\" paths alike — matching is purely structural,
/// not tied to a fixed list of known DLC folders).
/// </summary>
public sealed class LandscapeFolderDetector
{
    private const string LandscapeSegment = "landscape";
    private const string TexturesSegment = "textures";

    // Auto-generation is only ever attempted for the "statics" folder - alpha-stripping a diffuse
    // to opaque (see MissingTextureGenerator) is what makes a "statics" sibling, so applying that
    // same transform to synthesize a "blending" sibling would produce a mislabeled file (opaque,
    // when a blending variant needs the real alpha gradient). A "blending" sibling can still be
    // matched if one already exists on disk - it just never gets synthesized.
    private const string GeneratableFolderName = "statics";

    private readonly IReadOnlyList<LandscapeFolderRule> _rules;
    private readonly IGameFileProbe _fileProbe;
    private readonly MissingTextureGenerator? _textureGenerator;
    private readonly IReadOnlyList<string> _autoGenerateAllowlist;

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
    /// generation is opt-in per texture rather than blanket-covering anything eligible.
    /// </param>
    public LandscapeFolderDetector(
        IReadOnlyList<LandscapeFolderRule> rules,
        IGameFileProbe fileProbe,
        MissingTextureGenerator? textureGenerator = null,
        IReadOnlyList<string>? autoGenerateAllowlist = null)
    {
        _rules = rules;
        _fileProbe = fileProbe;
        _textureGenerator = textureGenerator;
        _autoGenerateAllowlist = autoGenerateAllowlist ?? Array.Empty<string>();
    }

    /// <summary>
    /// Returns the first matching rule (in configured order) whose sub-folder exists next to
    /// <paramref name="diffusePath"/>, or null if the path has no "landscape" segment or none of
    /// the configured folders exist there.
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
        var segments = vanillaDiffusePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || !segments[0].Equals(TexturesSegment, StringComparison.OrdinalIgnoreCase))
        {
            vanillaDiffusePath = TexturesSegment + "\\" + vanillaDiffusePath;
        }

        segments = vanillaDiffusePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var landscapeIndex = Array.FindIndex(segments, s => s.Equals(LandscapeSegment, StringComparison.OrdinalIgnoreCase));
        if (landscapeIndex < 0 || landscapeIndex == segments.Length - 1)
        {
            return null;
        }

        // The path can already sit inside one of the configured rule folders - e.g. another mod's
        // own Alternate Texture already points straight at the statics/blending variant. Inserting
        // the rule folder a second time would look for a "statics/statics/..." path that can never
        // exist, so recognize this case and treat the path as already-resolved instead.
        if (landscapeIndex + 1 < segments.Length)
        {
            var immediateRule = _rules.FirstOrDefault(r => segments[landscapeIndex + 1].Equals(r.FolderName, StringComparison.OrdinalIgnoreCase));
            if (immediateRule is not null)
            {
                return new LandscapeFolderDetection(immediateRule, vanillaDiffusePath);
            }
        }

        foreach (var rule in _rules)
        {
            var candidateSegments = segments.ToList();
            candidateSegments.Insert(landscapeIndex + 1, rule.FolderName);
            var candidatePath = string.Join('\\', candidateSegments);

            if (_fileProbe.Exists(candidatePath))
            {
                return new LandscapeFolderDetection(rule, candidatePath);
            }

            // Nothing already provides this sibling - synthesize one from whatever diffuse is
            // actually winning in the load order, so a mod author never has to hand-author it (see
            // MissingTextureGenerator's own doc comment for why this is safe: verified to reproduce
            // Vanaheimr's own hand-authored statics textures almost pixel-for-pixel). Only for
            // "statics" (see GeneratableFolderName above) and only for textures explicitly listed in
            // AutoGenerateAllowlist - every landscape texture with an alpha-blended shape is
            // structurally "eligible" here, but generating for all of them indiscriminately produced
            // far more textures than intended (including ones with no real source, or ones a statics
            // variant doesn't actually make sense for), so this is opt-in per texture, not blanket.
            var canGenerate = rule.FolderName.Equals(GeneratableFolderName, StringComparison.OrdinalIgnoreCase)
                && WildcardMatcher.MatchesAny(vanillaDiffusePath, _autoGenerateAllowlist);
            if (canGenerate && _textureGenerator is not null && _textureGenerator.TryGenerate(vanillaDiffusePath, candidatePath, out _))
            {
                return new LandscapeFolderDetection(rule, candidatePath);
            }
        }

        return null;
    }
}
