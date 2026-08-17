namespace Aqsat.Domain.Common;

/// <summary>
/// Marks a property whose value must never appear in an audit ChangesJson diff — only the fact
/// that it changed is recorded, never the before/after value (CLAUDE.md rule 30). Apply to any
/// field encrypted at rest, e.g. Customer.NationalId.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditSensitiveAttribute : Attribute;
