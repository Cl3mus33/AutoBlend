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

    private readonly IReadOnlyList<LandscapeFolderRule> _rules;
    private readonly IGameFileProbe _fileProbe;
    private readonly MissingTextureGenerator? _textureGenerator;

    /// <param name="textureGenerator">
    /// When set, a texture with no existing statics/blending sibling gets one synthesized on the
    /// fly (see <see cref="MissingTextureGenerator"/>) instead of being left undetected - so mod
    /// authors no longer need to hand-author these siblings themselves. Null disables this (e.g.
    /// texconv isn't available, or the user turned the setting off) and falls back to the previous
    /// "not found" behavior.
    /// </param>
    public LandscapeFolderDetector(IReadOnlyList<LandscapeFolderRule> rules, IGameFileProbe fileProbe, MissingTextureGenerator? textureGenerator = null)
    {
        _rules = rules;
        _fileProbe = fileProbe;
        _textureGenerator = textureGenerator;
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
            // Vanaheimr's own hand-authored statics textures almost pixel-for-pixel).
            if (_textureGenerator is not null && _textureGenerator.TryGenerate(vanillaDiffusePath, candidatePath, out _))
            {
                return new LandscapeFolderDetection(rule, candidatePath);
            }
        }

        return null;
    }
}
