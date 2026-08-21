namespace Aqsat.Application.ApiIr;

/// <summary>
/// docs/PHASE-1-SPEC.md §6 — api.ir's response shape. CLAUDE.md rule 18: check `Success`, never
/// the presence of `Data` alone — their own docs show `success:false` alongside populated `data`.
/// </summary>
public sealed record ApiIrEnvelope<T>(T? Data, bool Success, int Code, string? Message);
