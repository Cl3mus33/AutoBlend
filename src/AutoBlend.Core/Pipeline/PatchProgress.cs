namespace AutoBlend.Core.Pipeline;

/// <summary>A progress update from a patch run. Total &lt;= 0 means indeterminate (no known count yet).</summary>
public sealed record PatchProgress(string Message, int Current, int Total)
{
    public bool IsIndeterminate => Total <= 0;
}
