using Aqsat.Domain.Common;

namespace Aqsat.Domain;

/// <summary>Agency-definable — expense categories are not a fixed system list (CLAUDE.md-style
/// "نیاز به دسته‌بندی قابل تعریف توسط خود نمایندگی دارد").</summary>
public class ExpenseCategory : AgencyOwnedEntity
{
    public string Name { get; set; } = default!;
    public bool IsActive { get; set; } = true;
}
