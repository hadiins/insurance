namespace Aqsat.Api.Contracts;

public sealed record BootstrapOwnerRequest(string Secret, string FullName, string Mobile, string Password);
