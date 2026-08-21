namespace Aqsat.Infrastructure.ApiIr;

/// <summary>
/// CLAUDE.md "Ask before deciding": "Anything that would call a paid api.ir endpoint outside
/// Sandbox" — AllowPaidEndpoints defaults to false precisely so that flipping it on is a deliberate,
/// visible configuration change, never an accident of forgetting to set it.
/// </summary>
public sealed class ApiIrOptions
{
    public const string SectionName = "ApiIr";

    public string BaseUrl { get; set; } = "https://s.api.ir";
    public string ApiKey { get; set; } = string.Empty;
    public bool AllowPaidEndpoints { get; set; }
}
