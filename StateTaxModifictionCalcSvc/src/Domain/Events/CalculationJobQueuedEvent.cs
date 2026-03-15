using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.Events;

public sealed record CalculationJobQueuedEvent(
    Guid JobId,
    Guid ClientId,
    Guid? EntityId,
    Guid? JurisdictionId,
    TaxPeriod TaxPeriod,
    CalculationTrigger Trigger,
    string RequestedBy) : DomainEvent;
