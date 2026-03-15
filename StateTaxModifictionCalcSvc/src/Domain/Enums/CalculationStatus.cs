namespace Domain.Enums;

public enum CalculationStatus
{
    Queued,
    InProgress,
    Completed,
    CompletedWithWarnings,
    Failed,
    Cancelled,
    PendingReview
}
