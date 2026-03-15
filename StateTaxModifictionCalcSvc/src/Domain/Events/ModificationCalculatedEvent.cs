namespace Domain.Events;

public sealed record ModificationCalculatedEvent(
    Guid JobId,
    Guid ModificationId,
    Guid EntityId,
    Guid JurisdictionId,
    decimal PreApportionmentAmount,
    decimal ApportionmentFactor,
    decimal FinalAmount) : DomainEvent;
