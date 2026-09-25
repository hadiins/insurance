using System.Text.Json.Serialization;

namespace Aqsat.Domain.Enums;

/// <summary>
/// The identity document a customer was registered with (owner decision 2026-09-21 — support for
/// issuing policies to foreign nationals). ForeignResident holds an Iranian national ID issued to
/// a foreign resident — a 10-digit number in the 996 series, stored and queried exactly like an
/// Iranian ID. ForeignPassportOnly has no national ID at all: identified by passport, and every
/// external credit inquiry (api.ir) is deliberately never run for them.
/// </summary>
/// <remarks>The frontend sends/reads this as a JSON string ("Iranian"/"ForeignResident"/
/// "ForeignPassportOnly"), matching the PaymentMethod convention — [ApiController]'s default
/// System.Text.Json config only accepts enums by their numeric value unless told otherwise, so
/// this converter is required, not cosmetic.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CustomerKind : byte
{
    /// <summary>Iranian citizen with a 10-digit national ID (mod-11 checksum validated).</summary>
    Iranian = 1,

    /// <summary>Foreign resident holding a 996-series Iranian national ID.</summary>
    ForeignResident = 2,

    /// <summary>Foreign national identified solely by passport — no external credit inquiries.</summary>
    ForeignPassportOnly = 3,
}
