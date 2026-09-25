namespace Aqsat.Api.Contracts;

/// <summary>A logged collection contact. Channel/Outcome arrive as strings so the caller gets a
/// Persian validation message instead of a model-binding failure, the same convention the cheque
/// and collateral status endpoints use.
///
/// There is no note field, deliberately: CLAUDE.md rule 8 forbids free text about a person, and a
/// collection call is a conversation with one.</summary>
public sealed record CreateCollectionContactRequest(
    Guid PolicyId,
    Guid? InstallmentId,
    string Channel,
    string Outcome,
    DateTimeOffset OccurredAt,
    DateOnly? PromisedOn,
    decimal? PromisedAmount);

public sealed record CollectionContactDto(
    Guid Id,
    Guid PolicyId,
    Guid? InstallmentId,
    string Channel,
    string Outcome,
    DateTimeOffset OccurredAt,
    DateOnly? PromisedOn,
    decimal? PromisedAmount,
    string RecordedBy);
