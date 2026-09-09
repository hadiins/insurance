using Aqsat.Infrastructure.Monitoring;

namespace Aqsat.UnitTests.Monitoring;

/// <summary>
/// The log tailer runs against real Serilog-format files written to a temp directory — backwards
/// order, level/search filters, cursor paging, and the skip-not-crash behaviour on unparsable
/// lines (stack-trace continuations, a line mid-write).
/// </summary>
public class SerilogTailReaderTests : IDisposable
{
    private readonly string _directory;

    public SerilogTailReaderTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"aqsat-log-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup on Windows can race with handle release; the OS cleans temp anyway.
        }
    }

    private string WriteTodayLog(params string[] lines)
    {
        // Invariant culture on purpose: this host's default culture is fa-IR (Persian calendar),
        // which would otherwise emit "aqsat-api-14050617.log" — a filename the reader's invariant
        // Gregorian date parse then rejects, exactly the mismatch this guards against.
        var path = Path.Combine(_directory,
            $"aqsat-api-{DateTime.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)}.log");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string Line(TimeSpan offset, string level, string message) =>
        DateTimeOffset.UtcNow.Add(offset).ToString("yyyy-MM-dd HH:mm:ss.fff zzz", System.Globalization.CultureInfo.InvariantCulture)
            + $" [{level}] {message}";

    private (IReadOnlyList<LogLine> Lines, bool HasMore) Tail(
        int limit, DateTimeOffset? before = null, string? search = null, string? minimumLevel = null) =>
        new SerilogTailReader().Tail(
            _directory, limit, before, DateTimeOffset.UtcNow.AddDays(-7), search, minimumLevel);

    [Fact]
    public void Entries_come_back_newest_first_and_malformed_lines_are_skipped()
    {
        WriteTodayLog(
            Line(TimeSpan.FromMinutes(0), "INF", "اولین پیام"),
            "   at SomeNamespace.SomeMethod() in SomeFile.cs:line 42",
            Line(TimeSpan.FromMinutes(1), "ERR", "خطای اول"),
            Line(TimeSpan.FromMinutes(2), "WRN", "هشدار میانی"),
            Line(TimeSpan.FromMinutes(3), "ERR", "خطای دوم"));

        var (lines, hasMore) = Tail(10);

        Assert.False(hasMore);
        Assert.Equal(4, lines.Count);
        // Newest first, and the stack-trace continuation never appears.
        Assert.Equal("خطای دوم", lines[0].Message);
        Assert.Equal("هشدار میانی", lines[1].Message);
        Assert.Equal("خطای اول", lines[2].Message);
        Assert.Equal("اولین پیام", lines[3].Message);
        Assert.Equal("Error", lines[0].Level);
        Assert.Equal("Warning", lines[1].Level);
        Assert.Equal("Information", lines[3].Level);
    }

    [Fact]
    public void Minimum_level_filters_to_that_level_and_above()
    {
        WriteTodayLog(
            Line(TimeSpan.FromMinutes(0), "DBG", "اشکال‌زدایی"),
            Line(TimeSpan.FromMinutes(1), "INF", "اطلاع"),
            Line(TimeSpan.FromMinutes(2), "WRN", "هشدار"),
            Line(TimeSpan.FromMinutes(3), "ERR", "خطا"),
            Line(TimeSpan.FromMinutes(4), "FTL", "بحرانی"));

        var (lines, _) = Tail(10, minimumLevel: "Error");

        Assert.Equal(new[] { "Fatal", "Error" }, lines.Select(l => l.Level).ToArray());
    }

    [Fact]
    public void Search_is_a_case_insensitive_substring_on_the_message()
    {
        WriteTodayLog(
            Line(TimeSpan.FromMinutes(0), "ERR", "Connection refused"),
            Line(TimeSpan.FromMinutes(1), "INF", "request handled"),
            Line(TimeSpan.FromMinutes(2), "ERR", "REQUEST failed"));

        var (lines, _) = Tail(10, search: "request");

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.NotEqual("Connection refused", l.Message));
    }

    [Fact]
    public void The_cursor_returns_only_entries_strictly_older_than_it()
    {
        WriteTodayLog(
            Line(TimeSpan.FromMinutes(0), "INF", "پیش"),
            Line(TimeSpan.FromMinutes(1), "INF", "وسط"),
            Line(TimeSpan.FromMinutes(2), "INF", "بعد"));

        var (first, hasMore) = Tail(1);
        Assert.True(hasMore);
        var newest = Assert.Single(first);

        var (rest, _) = Tail(10, before: newest.TimestampUtc);
        Assert.Equal(2, rest.Count);
        Assert.All(rest, l => Assert.True(l.TimestampUtc < newest.TimestampUtc));
        Assert.Equal("وسط", rest[0].Message);
    }

    [Fact]
    public void Older_files_are_read_after_the_current_one_is_exhausted()
    {
        WriteTodayLog(Line(TimeSpan.FromMinutes(0), "INF", "امروز"));
        File.WriteAllText(
            Path.Combine(_directory,
                $"aqsat-api-{DateTime.UtcNow.AddDays(-1).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)}.log"),
            Line(TimeSpan.FromMinutes(-1440), "INF", "دیروز") + "\n");

        var (lines, _) = Tail(10);

        Assert.Equal(2, lines.Count);
        Assert.Equal("امروز", lines[0].Message);
        Assert.Equal("دیروز", lines[1].Message);
    }

    [Fact]
    public void An_empty_or_missing_directory_is_an_empty_page_not_an_error()
    {
        var (lines, hasMore) = new SerilogTailReader().Tail(
            Path.Combine(_directory, "missing"), 10, null, DateTimeOffset.UtcNow.AddDays(-1), null, null);

        Assert.False(hasMore);
        Assert.Empty(lines);
    }
}
