using System.Collections.Concurrent;

namespace Aqsat.Infrastructure.Monitoring;

/// <summary>
/// In-memory per-minute request telemetry the metrics middleware feeds and MetricsSamplerJob drains.
/// Bounded by design: at most 10 live minute buckets and 200 distinct route templates per bucket,
/// latency in a fixed 9-bucket histogram (percentiles approximate, memory constant) — an aggregator
/// that grows with traffic would itself be the outage. Singleton; restart loses only the in-flight
/// minute, which the persisted MetricSample rows never notice.
/// </summary>
public sealed class RequestMetricsAggregator
{
    /// <summary>Upper edges of the latency histogram (ms); the last bucket is "everything above".</summary>
    private static readonly int[] LatencyEdges = [50, 100, 200, 400, 800, 1500, 3000, 5000, int.MaxValue];

    private readonly object _lock = new();
    private readonly SortedDictionary<DateTimeOffset, MinuteBucket> _minutes = new();

    public void Record(DateTimeOffset nowUtc, string? path, int statusCode, double durationMs)
    {
        var minute = TruncateToMinute(nowUtc);
        var ms = (int)Math.Min(durationMs, int.MaxValue);

        lock (_lock)
        {
            // A missed sampler run must not leak buckets forever — anything older than 10 minutes
            // is dropped, persisted history lives in MetricSample instead.
            while (_minutes.Count > 0 && (minute - _minutes.First().Key).TotalMinutes > 10)
            {
                _minutes.Remove(_minutes.First().Key);
            }

            if (!_minutes.TryGetValue(minute, out var bucket))
            {
                bucket = new MinuteBucket();
                _minutes[minute] = bucket;
            }

            bucket.Requests++;
            if (statusCode >= 500)
            {
                bucket.Errors++;
            }
            else if (statusCode >= 400)
            {
                bucket.ClientErrors++;
            }

            var histogramIndex = ((ReadOnlySpan<int>)LatencyEdges).IndexOfEdge(ms);
            bucket.LatencyHistogram[histogramIndex]++;

            if (path is { Length: > 0 } && bucket.Paths.Count < 200)
            {
                var stats = bucket.Paths.TryGetValue(path, out var existing) ? existing : bucket.Paths[path] = new PathAgg();
                stats.Count++;
                if (statusCode >= 500)
                {
                    stats.Errors++;
                }

                stats.TotalMs += ms;
                if (ms > 1000)
                {
                    stats.SlowCount++;
                }
            }
        }
    }

    /// <summary>Removes and returns every COMPLETED minute bucket (strictly before the current
    /// minute) for the sampler to persist. Path stats come with each bucket.</summary>
    public List<DrainedMinute> DrainCompleted(DateTimeOffset nowUtc)
    {
        var currentMinute = TruncateToMinute(nowUtc);
        var drained = new List<DrainedMinute>();

        lock (_lock)
        {
            var keys = _minutes.Keys.ToList();
            foreach (var key in keys)
            {
                if (key >= currentMinute)
                {
                    continue;
                }

                var bucket = _minutes[key];
                drained.Add(new DrainedMinute(
                    key,
                    bucket.Requests,
                    bucket.Errors,
                    bucket.ClientErrors,
                    [.. bucket.LatencyHistogram],
                    bucket.Paths.Select(p => new PathStat(p.Key, p.Value.Count, p.Value.Errors, p.Value.TotalMs, p.Value.SlowCount)).ToList()));
                _minutes.Remove(key);
            }
        }

        return drained;
    }

    /// <summary>The current (partial) minute — what the live "server" panel shows before anything persisted.</summary>
    public (int Requests, int Errors) CurrentMinute(DateTimeOffset nowUtc)
    {
        var minute = TruncateToMinute(nowUtc);
        lock (_lock)
        {
            return _minutes.TryGetValue(minute, out var bucket) ? (bucket.Requests, bucket.Errors) : (0, 0);
        }
    }

    public static DateTimeOffset TruncateToMinute(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Offset);

    /// <summary>Approximate percentile from the fixed histogram: the upper edge of the bucket that
    /// contains the requested percentile of samples (midpoint for the open top bucket).</summary>
    public static int PercentileFromHistogram(int[] histogram, int percentile)
    {
        var total = histogram.Sum();
        if (total == 0)
        {
            return 0;
        }

        var target = Math.Ceiling(total * percentile / 100.0);
        var cumulative = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            cumulative += histogram[i];
            if (cumulative >= target)
            {
                return i == histogram.Length - 1 ? LatencyEdges[^2] : LatencyEdges[i];
            }
        }

        return LatencyEdges[^2];
    }

    private sealed class MinuteBucket
    {
        public int Requests;
        public int Errors;
        public int ClientErrors;
        public readonly int[] LatencyHistogram = new int[LatencyEdges.Length];
        public readonly Dictionary<string, PathAgg> Paths = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PathAgg
    {
        public int Count;
        public int Errors;
        public int TotalMs;
        public int SlowCount;
    }
}

public sealed record DrainedMinute(
    DateTimeOffset MinuteUtc, int Requests, int Errors, int ClientErrors, int[] LatencyHistogram,
    IReadOnlyList<PathStat> Paths);

public sealed record PathStat(string Path, int Count, int Errors, int TotalMs, int SlowCount);

internal static class HistogramEdgeExtensions
{
    /// <summary>Index of the first edge at or above the value.</summary>
    public static int IndexOfEdge(this ReadOnlySpan<int> edges, int value)
    {
        for (var i = 0; i < edges.Length; i++)
        {
            if (value <= edges[i])
            {
                return i;
            }
        }

        return edges.Length - 1;
    }
}

/// <summary>
/// Cumulative EF command durations by command-text prefix — the "slow queries" panel's data. Lives
/// only in memory (restart clears it, which the UI states); persisted query telemetry would need a
/// table whose volume this dashboard doesn't justify yet.
/// </summary>
public sealed class EfCommandDurationStats
{
    private readonly ConcurrentDictionary<string, QueryStat> _stats = new();

    public void Record(string commandText, TimeSpan duration)
    {
        var key = commandText.Length > 200 ? commandText[..200] : commandText;
        _stats.AddOrUpdate(key, _ => new QueryStat(key, 1, duration.TotalMilliseconds, duration.TotalMilliseconds),
            (_, s) => s.Record(duration));
    }

    public IReadOnlyList<QueryStat> TopSlow(int take) =>
        _stats.Values.OrderByDescending(s => s.MaxMs).Take(take).ToList();

    public sealed record QueryStat(string Text, long Count, double TotalMs, double MaxMs)
    {
        public double AvgMs => Count == 0 ? 0 : TotalMs / Count;

        internal QueryStat Record(TimeSpan duration) => new(Text, Count + 1, TotalMs + duration.TotalMilliseconds, Math.Max(MaxMs, duration.TotalMilliseconds));
    }
}
