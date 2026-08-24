using Aqsat.Application.Common;

namespace Aqsat.UnitTests.Customers;

/// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §2 — "09" + 9 digits, +98/0098 prefixes normalized.</summary>
public class MobileNumberValidatorTests
{
    [Fact]
    public void Standard_local_format_is_valid()
    {
        Assert.True(MobileNumberValidator.IsValid("09123456789"));
    }

    [Fact]
    public void International_plus98_prefix_normalizes_and_validates()
    {
        Assert.True(MobileNumberValidator.IsValid("+989123456789"));
        Assert.Equal("09123456789", MobileNumberValidator.Normalize("+989123456789"));
    }

    [Fact]
    public void International_00_98_prefix_normalizes_and_validates()
    {
        Assert.True(MobileNumberValidator.IsValid("00989123456789"));
        Assert.Equal("09123456789", MobileNumberValidator.Normalize("00989123456789"));
    }

    [Fact]
    public void Persian_digits_are_normalized()
    {
        Assert.True(MobileNumberValidator.IsValid("۰۹۱۲۳۴۵۶۷۸۹"));
    }

    [Fact]
    public void Wrong_length_is_rejected()
    {
        Assert.False(MobileNumberValidator.IsValid("0912345678"));
        Assert.False(MobileNumberValidator.IsValid("091234567890"));
    }

    [Fact]
    public void Landline_shape_is_rejected()
    {
        Assert.False(MobileNumberValidator.IsValid("02112345678"));
    }
}
