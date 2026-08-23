namespace Aqsat.Api.Contracts;

public sealed record SmsTemplateDto(string Key, string Label, string Placeholders, string Body, bool IsCustomized);

public sealed record SaveSmsTemplateRequest(string Body);
