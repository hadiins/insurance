using Aqsat.Application.Payments;

namespace Aqsat.UnitTests.Payments;

public class PaymentAllocatorTests
{
    [Fact]
    public void Negative_payment_is_rejected_instead_of_creating_negative_credit()
    {
        var installments = new[] { new InstallmentBalance(Guid.NewGuid(), 100m) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => PaymentAllocator.Allocate(-0.01m, installments));

        Assert.Equal("paymentAmount", exception.ParamName);
    }

    [Fact]
    public void Zero_payment_allocates_nothing()
    {
        var installments = new[] { new InstallmentBalance(Guid.NewGuid(), 100m) };

        var result = PaymentAllocator.Allocate(0m, installments);

        Assert.Empty(result.Allocations);
        Assert.Equal(0m, result.UnallocatedAmount);
    }

    [Fact]
    public void Fractional_payment_skips_nonpositive_balances_and_preserves_exact_total()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var installments = new[]
        {
            new InstallmentBalance(Guid.NewGuid(), 0m),
            new InstallmentBalance(Guid.NewGuid(), -10m),
            new InstallmentBalance(first, 0.10m),
            new InstallmentBalance(second, 0.30m),
        };

        var result = PaymentAllocator.Allocate(0.25m, installments);

        Assert.Equal(2, result.Allocations.Count);
        Assert.Equal((first, 0.10m), result.Allocations[0]);
        Assert.Equal((second, 0.15m), result.Allocations[1]);
        Assert.Equal(0m, result.UnallocatedAmount);
        Assert.Equal(0.25m, result.Allocations.Sum(x => x.Amount) + result.UnallocatedAmount);
    }

    [Fact]
    public void Overpayment_settles_the_first_installment_and_flows_the_remainder_to_the_next()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var installments = new[] { new InstallmentBalance(first, 1_000_000m), new InstallmentBalance(second, 1_000_000m) };

        var result = PaymentAllocator.Allocate(1_500_000m, installments);

        Assert.Equal(2, result.Allocations.Count);
        Assert.Equal((first, 1_000_000m), result.Allocations[0]);
        Assert.Equal((second, 500_000m), result.Allocations[1]);
        Assert.Equal(0m, result.UnallocatedAmount);
    }

    [Fact]
    public void Underpayment_partially_allocates_only_the_first_installment()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var installments = new[] { new InstallmentBalance(first, 1_000_000m), new InstallmentBalance(second, 1_000_000m) };

        var result = PaymentAllocator.Allocate(600_000m, installments);

        Assert.Single(result.Allocations);
        Assert.Equal((first, 600_000m), result.Allocations[0]);
        Assert.Equal(0m, result.UnallocatedAmount);
    }

    [Fact]
    public void Remainder_past_every_unsettled_installment_is_left_unallocated_as_credit()
    {
        var only = Guid.NewGuid();
        var installments = new[] { new InstallmentBalance(only, 1_000_000m) };

        var result = PaymentAllocator.Allocate(1_300_000m, installments);

        Assert.Single(result.Allocations);
        Assert.Equal((only, 1_000_000m), result.Allocations[0]);
        Assert.Equal(300_000m, result.UnallocatedAmount);
    }

    [Fact]
    public void No_unsettled_installments_leaves_the_entire_payment_unallocated()
    {
        var result = PaymentAllocator.Allocate(500_000m, []);

        Assert.Empty(result.Allocations);
        Assert.Equal(500_000m, result.UnallocatedAmount);
    }
}
