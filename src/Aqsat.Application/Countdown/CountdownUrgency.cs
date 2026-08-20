namespace Aqsat.Application.Countdown;

/// <summary>docs/PHASE-1-SPEC.md §3.3, verbatim ordering (most urgent first).</summary>
public enum CountdownUrgency
{
    Overdue,
    Critical,
    Warning,
    Upcoming,
    Future,
}
