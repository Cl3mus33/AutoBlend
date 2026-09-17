using Mutagen.Bethesda.Archives;

namespace AutoBlend.Core.Scanning;

/// <summary>
/// Works around a real bug in Mutagen.Bethesda 0.54.4 (the pinned version this project uses):
/// reading a COMPRESSED file entry out of a Legendary Edition-format archive (BSA v103 - the "FO3"
/// header type Mutagen's own reader uses for it) throws
/// <c>ArchiveException: "InflaterInputStream Length is not supported"</c> from deep inside
/// Mutagen's own BSA reader, via both <see cref="IArchiveFile.AsStream"/> and the concrete file
/// record's own GetBytes() - SharpZipLib's InflaterInputStream doesn't support querying .Length,
/// and Mutagen's own code does that internally before ever handing back a stream. Confirmed this
/// does NOT affect Special Edition's own archives (a different, newer BSA version with its own
/// reader implementation) - reproduced directly against real vanilla LE/SE Data folders.
///
/// There is no public API for any of this - Offset/Size/Compressed only exist on Mutagen's own
/// internal file-record type (Mutagen.Bethesda.Archives.Bsa.BsaFileRecord), reachable only via
/// reflection. This exists purely to work around Mutagen's own bug until a future version fixes
/// it; every failure mode here (missing property, unexpected shape, bad zlib header) falls back to
/// returning false rather than throwing, so a Mutagen update that changes this internal shape just
/// makes this path stop firing - it never corrupts anything or masks a real error.
/// </summary>
internal static class ManualArchiveExtractor
{
    public static bool TryExtract(string archivePath, IArchiveFile file, out byte[]? bytes)
    {
        bytes = null;
        try
        {
            var fileType = file.GetType();
            var offsetProp = fileType.GetProperty("Offset");
            var sizeProp = fileType.GetProperty("Size");
            var compressedProp = fileType.GetProperty("Compressed");
            if (offsetProp is null || sizeProp is null || compressedProp is null
                || compressedProp.GetValue(file) is not bool compressed || !compressed)
            {
                // The bug this works around is compressed-entry-only - nothing to recover from
                // otherwise.
                return false;
            }

            var offset = Convert.ToInt64(offsetProp.GetValue(file));
            var size = Convert.ToInt64(sizeProp.GetValue(file));

            using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read);

            // Some archives embed each file's own relative path (a 1-byte length prefix followed by
            // that many ASCII bytes) directly before its data; others don't, and which one applies
            // is itself only exposed via reflection on the archive reader (not the file record) -
            // simpler and just as reliable to try both layouts and keep whichever one's zlib header
            // actually validates (deflate method nibble + the standard FCHECK divisible-by-31 rule).
            if (TryReadCompressedAt(stream, offset, offset, size, out bytes))
            {
                return true;
            }

            stream.Position = offset;
            var nameLength = stream.ReadByte();
            if (nameLength is > 0 and < 260
                && TryReadCompressedAt(stream, offset + 1 + nameLength, offset, size, out bytes))
            {
                return true;
            }

            bytes = null;
            return false;
        }
        catch
        {
            bytes = null;
            return false;
        }
    }

    private static bool TryReadCompressedAt(FileStream stream, long dataStart, long recordStart, long recordSize, out byte[]? bytes)
    {
        bytes = null;
        stream.Position = dataStart;

        Span<byte> sizeBuf = stackalloc byte[4];
        if (stream.Read(sizeBuf) != 4)
        {
            return false;
        }

        var uncompressedSize = BitConverter.ToUInt32(sizeBuf);
        if (uncompressedSize == 0 || uncompressedSize > 500_000_000)
        {
            return false;
        }

        Span<byte> zlibHeader = stackalloc byte[2];
        if (stream.Read(zlibHeader) != 2
            || (zlibHeader[0] & 0x0F) != 8 // CMF low nibble must be 8 (deflate)
            || ((zlibHeader[0] << 8) + zlibHeader[1]) % 31 != 0) // zlib header FCHECK
        {
            return false;
        }

        var compressedLength = recordSize - (dataStart - recordStart) - 4;
        if (compressedLength is <= 0 or > int.MaxValue)
        {
            return false;
        }

        stream.Position = dataStart + 4;
        var compressedBytes = new byte[compressedLength];
        if (stream.Read(compressedBytes) != compressedBytes.Length)
        {
            return false;
        }

        try
        {
            using var compressedStream = new MemoryStream(compressedBytes);
            using var zlib = new System.IO.Compression.ZLibStream(compressedStream, System.IO.Compression.CompressionMode.Decompress);
            using var output = new MemoryStream((int)uncompressedSize);
            zlib.CopyTo(output);
            if (output.Length != uncompressedSize)
            {
                return false;
            }

            bytes = output.ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
