using System.Text.Json.Serialization;

namespace Aqsat.Domain.Enums;

/// <summary>Provider-agnostic by design (CLAUDE.md: no hard-coded vendor) — the panel picks one
/// and the IPaymentGateway implementation behind it does the vendor-specific work. Mock is the
/// safe default: fully simulated (no real money moves), so a fresh install completes the whole
/// customer-portal flow before any credential exists; switching to a real PSP is a deliberate,
/// visible panel change — same philosophy as ApiIrSettings.AllowPaidEndpoints defaulting to
/// sandbox.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentProvider : byte
{
    /// <summary>Simulated gateway for dev/test — Create always succeeds, Verify confirms
    /// immediately. Never select this in production.</summary>
    Mock = 1,

    /// <summary>زرینپال — the first real PSP wired behind IPaymentGateway. Amounts are sent as
    /// IRT (تومان) explicitly, matching this system's own internal unit (rule 19).</summary>
    ZarinPal = 2,

    /// <summary>گویا پی — wired behind the same IPaymentGateway abstraction. Its Amount field is
    /// toman-only (no currency parameter), so the shared in-toman contract maps directly.</summary>
    GooyaPay = 3,
}