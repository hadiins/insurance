using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// A saved column mapping for the generic import pipeline (docs/PHASE-1-SPEC.md §4.3) — "upload →
/// preview → user maps columns → save mapping per agency → import." Not part of Task 3's original
/// data model; added for Task 6 (see docs/TASKS.md decision log / commit message).
///
/// ImportType is a plain string key (e.g. "PolicyReport") rather than an enum — Phase 1 only ever
/// has one generic import type, and Task 7's Fanavaran adapter supplies its own hardcoded mapping
/// in code rather than a saved row here, so there is no known second value yet to justify an enum.
/// </summary>
public class ImportColumnMapping : AgencyOwnedEntity
{
    public string ImportType { get; set; } = default!;

    /// <summary>Serialized Dictionary&lt;string TargetFieldKey, string SourceColumnHeader&gt;.</summary>
    public string MappingJson { get; set; } = default!;
}
