using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>
/// قالب پیامک‌ها — an agency's own override of one of the fixed reminder templates
/// (Aqsat.Infrastructure.Jobs.InstallmentReminderTemplate / RenewalReminderTemplate). Absence of a
/// row for a Key simply means "use the hard-coded default text" — the send path never breaks
/// because a template hasn't been customized yet.
/// </summary>
public class SmsTemplate : AgencyOwnedEntity
{
    /// <summary>Matches one of the Key constants on the Render classes — never a free-text key.</summary>
    public string Key { get; set; } = default!;

    public string Body { get; set; } = default!;
}
