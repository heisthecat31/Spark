// Ported from the Replay Analyser (EchoAnalyser.Core/Parsing/ReplayLoader.cs). Logic is unchanged; only the
// namespace, explicit usings and nullable context differ, so fixes can be diffed against
// the original.
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Text;
using EchoVRAPI;
using Newtonsoft.Json;

namespace Spark.ReplayAnalyser.Parsing;

/// <summary>
/// Entry point for reading any replay this tool understands. Everything is normalised to a
/// <c>List&lt;Frame&gt;</c> so the analysis layer never has to care where the data came from.
/// </summary>
public static class ReplayLoader
{
    public static readonly string[] SupportedExtensions = { ".echodemo", ".echoreplay", ".butter", ".tape" };

    public static ReplayFormat DetectFormat(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".echodemo" => ReplayFormat.EchoDemo,
            ".echoreplay" => ReplayFormat.EchoReplay,
            ".butter" => ReplayFormat.Butter,
            ".tape" => ReplayFormat.Tape,
            _ => ReplayFormat.Unknown,
        };
    }

    public static ReplayReadResult Load(string path, CancellationToken ct = default)
    {
        var format = DetectFormat(path);
        try
        {
            return format switch
            {
                ReplayFormat.Butter => ButterV3Reader.Read(File.ReadAllBytes(path), path),
                ReplayFormat.EchoDemo => EchoDemoReader.Read(path),
                ReplayFormat.EchoReplay => ReadEchoReplay(path, ct),
                ReplayFormat.Tape => ReadTape(path),
                _ => Failed(path, format, "Unrecognised file extension."),
            };
        }
        catch (Exception ex)
        {
            return Failed(path, format, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static ReplayReadResult Failed(string path, ReplayFormat fmt, string message)
    {
        var r = new ReplayReadResult { SourcePath = path, Format = fmt };
        r.Diagnostics.Add(message);
        return r;
    }

    /// <summary>
    /// .echoreplay is one API response per line: <c>timestamp \t json [\t bonedata]</c>.
    /// Modern files wrap that in a zip; older ones are the raw text. A malformed line is
    /// skipped rather than aborting the read.
    /// </summary>
    private static ReplayReadResult ReadEchoReplay(string path, CancellationToken ct)
    {
        var result = new ReplayReadResult { SourcePath = path, Format = ReplayFormat.EchoReplay };

        using Stream stream = OpenPossiblyZipped(path, out string? innerName);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        int lineNo = 0, malformed = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            ct.ThrowIfCancellationRequested();
            lineNo++;
            if (line.Length == 0) continue;

            int tab = line.IndexOf('\t');
            if (tab <= 0) { malformed++; continue; }

            string timePart = line[..tab];
            string rest = line[(tab + 1)..];

            // An optional third field carries bone data; the JSON is the second field.
            int tab2 = rest.IndexOf('\t');
            string json = tab2 > 0 ? rest[..tab2] : rest;
            if (json.Length < 2 || json[0] != '{') { malformed++; continue; }

            Frame? frame;
            try { frame = JsonConvert.DeserializeObject<Frame>(json); }
            catch { malformed++; continue; }
            if (frame == null) { malformed++; continue; }

            frame.recorded_time = ParseTimestamp(timePart);
            result.Frames.Add(frame);
        }

        if (malformed > 0)
            result.Diagnostics.Add($"{malformed:N0} of {lineNo:N0} line(s) were malformed and skipped.");

        var first = result.Frames.FirstOrDefault();
        result.ClientName = first?.client_name;
        result.SessionId = first?.sessionid;
        return result;
    }

    private static Stream OpenPossiblyZipped(string path, out string? innerName)
    {
        innerName = null;
        var head = new byte[2];
        using (var probe = File.OpenRead(path))
        {
            if (probe.Read(head, 0, 2) < 2) return new MemoryStream(Array.Empty<byte>());
        }

        // "PK" — a zip container holding a single entry with the replay text.
        if (head[0] == 0x50 && head[1] == 0x4B)
        {
            var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries.FirstOrDefault(e => e.Length > 0) ?? zip.Entries.FirstOrDefault();
            if (entry == null) { zip.Dispose(); return new MemoryStream(Array.Empty<byte>()); }
            innerName = entry.FullName;
            return new ZipEntryStream(zip, entry.Open());
        }

        return File.OpenRead(path);
    }

    private static DateTime ParseTimestamp(string s)
    {
        // Recorded as "yyyy/MM/dd HH:mm:ss.fff", with some older files using '-' separators.
        if (DateTime.TryParseExact(s, new[]
            {
                "yyyy/MM/dd HH:mm:ss.fff",
                "yyyy/MM/dd HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss.fff",
                "yyyy-MM-ddTHH:mm:ss.fffZ",
            }, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt))
            return dt;
        return DateTime.TryParse(s, out var any) ? any : DateTime.MinValue;
    }

    /// <summary>
    /// .tape is a zstd-framed protobuf stream. The bundled native library only exposes
    /// writing, so rather than half-decode it we report clearly and point at the converter.
    /// </summary>
    private static ReplayReadResult ReadTape(string path)
    {
        var r = new ReplayReadResult { SourcePath = path, Format = ReplayFormat.Tape };
        r.Diagnostics.Add(
            "The .tape container (zstd-framed protobuf) has no managed reader yet. " +
            "Convert it first with: tapedeck show --format json \"" + Path.GetFileName(path) + "\"");
        return r;
    }

    /// <summary>Keeps the owning <see cref="ZipArchive"/> alive for the lifetime of the entry stream.</summary>
    private sealed class ZipEntryStream : Stream
    {
        private readonly ZipArchive _archive;
        private readonly Stream _inner;

        public ZipEntryStream(ZipArchive archive, Stream inner) { _archive = archive; _inner = inner; }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _inner.Dispose(); _archive.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
