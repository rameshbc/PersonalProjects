namespace Domain.Events;

public sealed record CalculationJobStartedEvent(
    Guid JobId,
    Guid ClientId,
    int TotalModifications,
    string WorkerInstanceId) : DomainEvent;
