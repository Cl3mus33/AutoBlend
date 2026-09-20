using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Exceptions;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// One empty or corrupted plugin in the load order (e.g. a 0-byte .esp left behind by a failed
/// download - reported directly on Nexus) makes Mutagen's whole environment build throw
/// ("Could not read enough data to parse a Mod Header"), aborting the run before it starts. Mutagen
/// tags that failure with the offending plugin (RecordException.ModKey), so callers can drop just
/// that plugin from the load order and retry instead of failing entirely.
/// </summary>
internal static class UnreadablePluginFinder
{
    public static bool TryFind(Exception exception, out ModKey modKey, out string reason)
    {
        if (exception is RecordException { ModKey: { } found })
        {
            modKey = found;
            reason = Innermost(exception).Message;
            return true;
        }

        IEnumerable<Exception> children = exception is AggregateException aggregate
            ? aggregate.InnerExceptions
            : exception.InnerException is { } inner ? new[] { inner } : Array.Empty<Exception>();
        foreach (var child in children)
        {
            if (TryFind(child, out modKey, out reason))
            {
                return true;
            }
        }

        modKey = default;
        reason = string.Empty;
        return false;
    }

    /// <summary>Cheap sanity check for a plugin file: at least a full record header (24 bytes) that
    /// starts with the "TES4" signature. Catches empty/truncated files without parsing anything.</summary>
    public static bool HasReadableHeader(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[24];
            return stream.Read(header) == 24 && header[0] == (byte)'T' && header[1] == (byte)'E' && header[2] == (byte)'S' && header[3] == (byte)'4';
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static Exception Innermost(Exception exception)
    {
        while (exception.InnerException is { } inner)
        {
            exception = inner;
        }

        return exception;
    }
}
