using System.Text;
using System.Text.RegularExpressions;

namespace Aqsat.Infrastructure.Monitoring;

public sealed record LogLine(DateTimeOffset TimestampUtc, string Level, string Message);

/// <summary>
/// Reads Serilog's rolling daily files (logs/aqsat-api-YYYYMMDD.log) BACKWARDS — newest entries
/// first, stopping as soon as the requested page is full, so a 30-day-old log never gets scanned
/// to answer "show me the last 100 errors". Shared-open with FileShare.ReadWrite because Serilog
/// holds the file open for writing. Lines that fail the parse (stack-trace continuations, a line
/// mid-write) are skipped, not fatal. Total scan is capped so a pathological file can't pin a
/// request.
/// </summary>
public sealed partial class SerilogTailReader
{
    private const int ChunkSize = 8192;
    private const long MaxScanBytes = 64L * 1024 * 1024;

    /// <summary>Serilog's default [{Level:u3}] token → full name and an orderable rank.</summary>
    private static readonly Dictionary<string, (string Name, int Rank)> Levels = new()
    {
        ["VRB"] = ("Verbose", 0),
        ["DBG"] = ("Debug", 1),
        ["INF"] = ("Information", 2),
        ["WRN"] = ("Warning", 3),
        ["ERR"] = ("Error", 4),
        ["FTL"] = ("Fatal", 5),
    };

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(\w{3})\] (.*)$")]
    private static partial Regex LineFormat();

    /// <param name="directory">The logs folder (relative to the app's working directory).</param>
    /// <param name="limit">Page size; the reader stops after limit + 1 matches (the extra entry
    /// only proves HasMore and is dropped).</param>
    /// <param name="before">Cursor: only entries strictly older than this are returned.</param>
    /// <param name="notOlderThan">Entries older than this are ignored (the time-range filter).</param>
    /// <param name="search">Case-insensitive substring filter on the message.</param>
    /// <param name="minimumLevel">Full level name ("Error" means Error and Fatal).</param>
    public (IReadOnlyList<LogLine> Lines, bool HasMore) Tail(
        string directory, int limit, DateTimeOffset? before, DateTimeOffset notOlderThan,
        string? search, string? minimumLevel)
    {
        var files = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "aqsat-api-*.log")
                .OrderDescending(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        var minimumRank = 0;
        if (minimumLevel is { Length: > 0 })
        {
            foreach (var (name, rank) in Levels.Values)
            {
                if (name.Equals(minimumLevel, StringComparison.OrdinalIgnoreCase))
                {
                    minimumRank = rank;
                    break;
                }
            }
        }

        search = string.IsNullOrEmpty(search) ? null : search;

        var results = new List<LogLine>();
        var hasMore = false;
        var scanned = 0L;
        // Fragment of the cut line at the chunk boundary whose prefix lives in the NEXT (older)
        // chunk — carried across iterations so a line is never read half.
        var carrySuffix = string.Empty;

        foreach (var file in files)
        {
            if (results.Count > limit)
            {
                hasMore = true;
                break;
            }

            if (notOlderThan.UtcDateTime.Date > FileNameDate(file).AddDays(1))
            {
                // Every entry in this file is older than the range — and so are all later files.
                break;
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var position = stream.Length;

            while (position > 0 && results.Count <= limit && scanned < MaxScanBytes)
            {
                var chunkLength = (int)Math.Min(ChunkSize, position);
                position -= chunkLength;
                scanned += chunkLength;

                var buffer = new byte[chunkLength];
                stream.Seek(position, SeekOrigin.Begin);
                stream.ReadExactly(buffer);
                var lines = Encoding.UTF8.GetString(buffer).Split('\n');

                // lines[^1] is the prefix of the line carried from the newer chunk — together they
                // form the newest complete line available at this boundary. On the very first
                // chunk it is simply the file's last line (possibly still being written; if so it
                // fails the parse and is skipped). Both carry paths can end in the \r of a \r\n
                // pair, which the regex's (.*) would otherwise swallow into the message.
                Consider(results, limit, (lines[^1] + carrySuffix).TrimEnd('\r'),
                    before, notOlderThan, search, minimumRank);

                for (var i = lines.Length - 2; i >= 1; i--)
                {
                    Consider(results, limit, lines[i].TrimEnd('\r'), before, notOlderThan, search, minimumRank);
                }

                // lines[0] lacks its prefix — it arrives with the next (older) chunk. At the true
                // file start there is nothing older: it is already complete.
                carrySuffix = lines[0];
                if (position == 0)
                {
                    Consider(results, limit, carrySuffix.TrimEnd('\r'), before, notOlderThan, search, minimumRank);
                    carrySuffix = string.Empty;
                }
            }

            if (scanned >= MaxScanBytes)
            {
                break;
            }
        }

        if (results.Count > limit)
        {
            results.RemoveAt(results.Count - 1);
            hasMore = true;
        }

        return (results, hasMore);
    }

    private static void Consider(
        List<LogLine> results, int limit, string line, DateTimeOffset? before,
        DateTimeOffset notOlderThan, string? search, int minimumRank)
    {
        if (results.Count > limit)
        {
            return;
        }

        var match = LineFormat().Match(line);
        if (!match.Success)
        {
            return;
        }

        if (!Levels.TryGetValue(match.Groups[2].Value, out var level) || level.Rank < minimumRank)
        {
            return;
        }

        if (!DateTimeOffset.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var timestamp))
        {
            return;
        }

        if (timestamp >= before || timestamp < notOlderThan)
        {
            return;
        }

        var message = match.Groups[3].Value;
        if (search is not null && !message.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        results.Add(new LogLine(timestamp, level.Name, message));
    }

    /// <summary>aqsat-api-20260908.log → 2026-09-08 (local midnight). Unparseable name → DateTime.MaxValue,
    /// so the file is never skipped on a naming surprise.</summary>
    private static DateTime FileNameDate(string file)
    {
        var stem = Path.GetFileNameWithoutExtension(file)["aqsat-api-".Length..];
        return DateTime.TryParseExact(stem, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date) ? date : DateTime.MaxValue;
    }
}
