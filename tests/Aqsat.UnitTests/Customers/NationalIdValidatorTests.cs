using Aqsat.Application.Common;

namespace Aqsat.UnitTests.Customers;

/// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §8 test list.</summary>
public class NationalIdValidatorTests
{
    // Checksum-valid fixture — verified by running the algorithm directly, not assumed. The doc's
    // own "۰۰۷۲•••۴۵۳" masking example (raw "0072345453") does NOT satisfy the checksum, so it is
    // used below only as an invalid-check-digit case, not as a valid one.
    private const string ValidId = "0072345454";

    [Fact]
    public void Valid_national_id_passes()
    {
        Assert.True(NationalIdValidator.IsValid(ValidId));
    }

    [Fact]
    public void Persian_digits_are_normalized_before_validation()
    {
        Assert.True(NationalIdValidator.IsValid("۰۰۷۲۳۴۵۴۵۴"));
    }

    [Fact]
    public void Leading_zero_is_preserved_not_truncated()
    {
        Assert.True(NationalIdValidator.IsValid(ValidId));
        Assert.False(NationalIdValidator.IsValid(ValidId.TrimStart('0')));
    }

    [Fact]
    public void All_identical_digits_are_rejected()
    {
        Assert.False(NationalIdValidator.IsValid("1111111111"));
    }

    [Fact]
    public void Wrong_length_is_rejected()
    {
        Assert.False(NationalIdValidator.IsValid("123456789"));
    }

    [Fact]
    public void Invalid_check_digit_is_rejected()
    {
        Assert.False(NationalIdValidator.IsValid("0072345453"));
    }

    [Fact]
    public void Null_or_empty_is_rejected()
    {
        Assert.False(NationalIdValidator.IsValid(null));
        Assert.False(NationalIdValidator.IsValid(""));
        Assert.False(NationalIdValidator.IsValid("   "));
    }
}
