using Aqsat.Application.Numbering;
using Aqsat.Domain;

namespace Aqsat.UnitTests.Numbering;

/// <summary>docs/TASK-24-POLICY-NUMBER.md §10 test list.</summary>
public class PolicyNumberParserTests
{
    private static readonly PolicyNumberFormat ParsianFormat = new()
    {
        InsurerName = "پارسیان", Pattern = "{line}/{agency}/{year}/{serial}", Separator = "/",
        LineCodeLength = 4, AgencyCodeLength = 6, YearDigits = 3, SerialLength = 6, IsStrict = false, IsActive = true,
    };

    [Fact]
    public void Standard_number_parses_all_four_parts()
    {
        var parts = PolicyNumberParser.Parse("1110/576210/405/000001", ParsianFormat);

        Assert.True(parts.IsParsed);
        Assert.Equal("1110", parts.LineCode);
        Assert.Equal("576210", parts.AgencyCode);
        Assert.Equal(1405, parts.Year);
        Assert.Equal("000001", parts.Serial);
    }

    [Fact]
    public void Persian_digits_are_normalized()
    {
        var parts = PolicyNumberParser.Parse("۱۱۱۰/۵۷۶۲۱۰/۴۰۵/۰۰۰۰۰۱", ParsianFormat);

        Assert.True(parts.IsParsed);
        Assert.Equal("1110", parts.LineCode);
        Assert.Equal("000001", parts.Serial);
    }

    [Fact]
    public void Alternate_separators_are_accepted()
    {
        var parts = PolicyNumberParser.Parse("1110-576210-405-000001", ParsianFormat);

        Assert.True(parts.IsParsed);
        Assert.Equal(1405, parts.Year);
    }

    [Fact]
    public void Spaces_and_invisible_characters_are_stripped()
    {
        var parts = PolicyNumberParser.Parse(" 1110/576210/405/000001 ", ParsianFormat);

        Assert.True(parts.IsParsed);
        Assert.Equal("000001", parts.Serial);
    }

    [Fact]
    public void Serial_keeps_leading_zeros_as_a_string()
    {
        var parts = PolicyNumberParser.Parse("1110/576210/405/000001", ParsianFormat);

        Assert.Equal("000001", parts.Serial);
        Assert.NotEqual("1", parts.Serial);
    }

    [Fact]
    public void Three_part_manual_entry_escape_hatch_fails_to_parse_but_does_not_throw()
    {
        var parts = PolicyNumberParser.Parse("1110/576210/405", ParsianFormat);

        Assert.False(parts.IsParsed);
        Assert.Equal("1110/576210/405", parts.Raw);
        Assert.NotNull(parts.Note);
    }

    [Fact]
    public void Two_digit_year_is_accepted_with_a_note()
    {
        var parts = PolicyNumberParser.Parse("1110/576210/05/000001", ParsianFormat);

        Assert.True(parts.IsParsed);
        Assert.Equal(1405, parts.Year);
        Assert.NotNull(parts.Note);
    }
}
