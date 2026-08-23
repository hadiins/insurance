using Aqsat.Application.Numbering;

namespace Aqsat.UnitTests.Numbering;

/// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §5.4/§8 — every Fanavaran plate text variant.</summary>
public class PlateParserTests
{
    [Fact]
    public void Spelled_out_format_parses()
    {
        var parts = PlateParser.Parse("55 الف 555 ایران 55");

        Assert.True(parts.IsParsed);
        Assert.Equal("55الف555-55", parts.Normalized);
    }

    [Fact]
    public void Compact_dashed_format_parses()
    {
        var parts = PlateParser.Parse("55الف555-55");

        Assert.True(parts.IsParsed);
        Assert.Equal("55الف555-55", parts.Normalized);
    }

    [Fact]
    public void Reversed_order_with_iran_prefix_parses()
    {
        var parts = PlateParser.Parse("ایران 55 - 555 الف 55");

        Assert.True(parts.IsParsed);
        Assert.Equal("55الف555-55", parts.Normalized);
    }

    [Fact]
    public void Latin_transliteration_parses()
    {
        var parts = PlateParser.Parse("55ALEF555IR55");

        Assert.True(parts.IsParsed);
        Assert.Equal("55الف555-55", parts.Normalized);
    }

    [Fact]
    public void Unrecognizable_text_fails_without_throwing_and_keeps_the_raw_value()
    {
        var parts = PlateParser.Parse("چیز نامفهوم");

        Assert.False(parts.IsParsed);
        Assert.Equal("چیز نامفهوم", parts.Raw);
        Assert.NotNull(parts.Note);
    }

    [Fact]
    public void Persian_digits_are_normalized_before_parsing()
    {
        var parts = PlateParser.Parse("۵۵ الف ۵۵۵ ایران ۵۵");

        Assert.True(parts.IsParsed);
        Assert.Equal("55الف555-55", parts.Normalized);
    }
}
