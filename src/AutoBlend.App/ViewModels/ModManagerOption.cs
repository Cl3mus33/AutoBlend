using AutoBlend.Core.Configuration;

namespace AutoBlend.App.ViewModels;

/// <summary>Display-friendly label for a ModManagerType, since "None" alone doesn't tell a user
/// this is also the right choice for Vortex (its deployment already merges mods into Data).</summary>
public sealed record ModManagerOption(ModManagerType Value, string Label);
