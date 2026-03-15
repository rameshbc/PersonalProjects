using Domain.Enums;

namespace Domain.Events;

public sealed record CalculationJobCompletedEvent(
    Guid JobId,
    Guid ClientId,
    CalculationStatus FinalStatus,
    int Processed,
    int Failed,
    TimeSpan Duration) : DomainEvent;
