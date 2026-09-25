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

    // ---- Owner decision 2026-09-21: the three-way identity classifier ----

    [Fact]
    public void A_996_series_id_classifies_as_foreign_resident()
    {
        // Structural only: no official checksum for the 996 series, so the shape decides.
        Assert.Equal(IdentityDocKind.ForeignResidentNationalId, NationalIdValidator.Classify("9961234567"));
        Assert.True(NationalIdValidator.IsForeignResident("۹۹۶۱۲۳۴۵۶۷")); // Persian digits normalize
        Assert.True(NationalIdValidator.IsValid("9961234567")); // and pass the generic gate
    }

    [Fact]
    public void A_regular_iranian_id_does_not_classify_as_foreign_resident()
    {
        Assert.Equal(IdentityDocKind.IranianNationalId, NationalIdValidator.Classify(ValidId));
        Assert.False(NationalIdValidator.IsForeignResident(ValidId));
    }

    [Fact]
    public void An_iranian_id_with_the_wrong_check_digit_is_still_rejected()
    {
        Assert.Equal(IdentityDocKind.Unknown, NationalIdValidator.Classify("0072345453"));
    }

    [Fact]
    public void A_996_id_with_a_wrong_shape_is_rejected()
    {
        Assert.Equal(IdentityDocKind.Unknown, NationalIdValidator.Classify("99612345678")); // 11 digits
        Assert.Equal(IdentityDocKind.Unknown, NationalIdValidator.Classify("996123456")); // 9 digits
        Assert.Equal(IdentityDocKind.Unknown, NationalIdValidator.Classify("99612345a")); // letter
    }

    [Fact]
    public void A_passport_classifies_as_passport()
    {
        Assert.Equal(IdentityDocKind.Passport, NationalIdValidator.Classify("A1234567"));
        Assert.Equal(IdentityDocKind.Passport, NationalIdValidator.Classify("p5432109x")); // lowercased
        Assert.True(NationalIdValidator.IsPassport("A1234567"));
        Assert.True(NationalIdValidator.IsValid("A1234567"));
    }

    [Fact]
    public void A_malformed_passport_is_rejected()
    {
        Assert.Equal(IdentityDocKind.Unknown, NationalIdValidator.Classify("A123")); // too short
        Assert.Equal(IdentityDocKind.Unknown, NationalIdValidator.Classify("A1234567890123456")); // too long
        Assert.Equal(IdentityDocKind.Unknown, NationalIdValidator.Classify("1234567890")); // pure digits → 10-digit path
        Assert.Equal(IdentityDocKind.Unknown, NationalIdValidator.Classify("۱۲۳۴۵۶۷۸۹")); // 9 Persian digits
    }

    [Fact]
    public void An_iranian_id_is_not_a_passport_and_vice_versa()
    {
        Assert.False(NationalIdValidator.IsPassport(ValidId));
        Assert.False(NationalIdValidator.IsIranian("A1234567"));
    }
}
