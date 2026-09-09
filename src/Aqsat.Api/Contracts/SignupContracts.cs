namespace Aqsat.Api.Contracts;

public sealed record SignupStatusDto(bool IsOpen);

public sealed record SignupRequestOtpRequest(string Mobile);

public sealed record AgencySignupRequest(
    string AgencyName, string ManagerFullName, string ManagerMobile,
    string OtpCode, string Password, string? Province, string? City, string? InsurerName);

/// <summary>No token is returned — the org is created pending approval, so login stays closed
/// until the platform owner activates it.</summary>
public sealed record AgencySignupResultDto(string Message);

/// <summary>Owner-facing read of the singleton signup switch (PlatformSignupSettings).</summary>
public sealed record PlatformSignupSettingsDto(bool AllowAgencySignup, DateTimeOffset? UpdatedAtUtc);

public sealed record UpdatePlatformSignupSettingsRequest(bool AllowAgencySignup);
