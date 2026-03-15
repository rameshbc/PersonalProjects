namespace Domain.Entities;

public sealed record CalculationJobError(
    Guid Id,
    Guid JobId,
    Guid? ModificationId,
    string Message,
    string? Detail,
    DateTime OccurredAt);
