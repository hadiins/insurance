namespace Aqsat.Application.Payments;

public readonly record struct InstallmentBalance(Guid InstallmentId, decimal Balance);

public readonly record struct AllocationResult(
    IReadOnlyList<(Guid InstallmentId, decimal Amount)> Allocations, decimal UnallocatedAmount);

/// <summary>
/// docs/PHASE-1-SPEC.md §3.4: apply a payment across a customer's unsettled installments, oldest
/// due date first, until the payment is exhausted. Underpayment is always a partial allocation to
/// the current installment — never a discount (CLAUDE.md rule 23). Whatever remains after every
/// unsettled installment is covered is left unallocated — that gap (Payment.Amount minus the sum of
/// its PaymentAllocation rows) *is* the customer's credit; no separate ledger column needed to
/// "park" it, and the same gap is exactly what flags a payment for agent review.
/// </summary>
public static class PaymentAllocator
{
    /// <param name="installmentsOrderedByDueDate">The customer's unsettled installments, already
    /// ordered oldest-due-date-first by the caller.</param>
    public static AllocationResult Allocate(decimal paymentAmount, IReadOnlyList<InstallmentBalance> installmentsOrderedByDueDate)
    {
        var remaining = paymentAmount;
        var allocations = new List<(Guid, decimal)>();

        foreach (var installment in installmentsOrderedByDueDate)
        {
            if (remaining <= 0)
            {
                break;
            }

            var take = Math.Min(remaining, installment.Balance);
            if (take <= 0)
            {
                continue;
            }

            allocations.Add((installment.InstallmentId, take));
            remaining -= take;
        }

        return new AllocationResult(allocations, remaining);
    }
}
