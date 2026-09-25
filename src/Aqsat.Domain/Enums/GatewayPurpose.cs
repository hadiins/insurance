namespace Aqsat.Domain.Enums;

/// <summary>
/// Which of the customer-portal money flows a gateway transaction belongs to. The PSP's callback
/// is anonymous and carries only the gateway's own reference, so the row it resolves to must say
/// what to finalise once verification succeeds — advancing the verification chain's fee stage,
/// recording a down payment, or settling one installment.
/// </summary>
public enum GatewayPurpose : byte
{
    /// <summary>The inquiry fee (کارمزد استعلام) on a portal invitation — collected through the
    /// OWNER's gateway (PlatformPaymentSettings) into the owner's account.</summary>
    InquiryFee = 1,

    /// <summary>The customer's down payment on a policy-verification invitation — collected through
    /// the AGENCY's own gateway (OrgSettings).</summary>
    DownPayment = 2,

    /// <summary>One installment paid through the agency's long-lived installment-payment link
    /// (/pay/{token}) — also the agency's own gateway.</summary>
    Installment = 3,
}
