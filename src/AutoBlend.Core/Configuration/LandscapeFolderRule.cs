namespace AutoBlend.Core.Configuration;

/// <summary>
/// One detected sub-folder name under any "*/landscape/" texture path (e.g. "statics", "blending"),
/// paired with the label substituted into {Type} when generating a derived TextureSet name.
/// </summary>
public sealed class LandscapeFolderRule
{
    public string FolderName { get; set; } = string.Empty;
    public string TypeLabel { get; set; } = string.Empty;

    public LandscapeFolderRule() { }

    public LandscapeFolderRule(string folderName, string typeLabel)
    {
        FolderName = folderName;
        TypeLabel = typeLabel;
    }

    public static IReadOnlyList<LandscapeFolderRule> Defaults { get; } = new[]
    {
        new LandscapeFolderRule("statics", "Statics"),
        new LandscapeFolderRule("blending", "Blending"),
        new LandscapeFolderRule("blend", "Blend"),
    };
}
