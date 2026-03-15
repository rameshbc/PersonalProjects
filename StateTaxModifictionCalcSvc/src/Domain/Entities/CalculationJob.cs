using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Tracks a background calculation job from queue → completion.
/// One job covers a specific scope: client + entity + jurisdiction + tax period.
/// </summary>
public sealed class CalculationJob
{
    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }

    /// <summary>Null when the job covers all entities for the client/period.</summary>
    public Guid? EntityId { get; private set; }

    /// <summary>Null when the job covers all jurisdictions.</summary>
    public Guid? JurisdictionId { get; private set; }

    public TaxPeriod TaxPeriod { get; private set; } = null!;
    public CalculationStatus Status { get; private set; }
    public CalculationTrigger Trigger { get; private set; }

    public DateTime QueuedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public string? RequestedBy { get; private set; }
    public string? WorkerInstanceId { get; private set; }

    public int TotalModifications { get; private set; }
    public int ProcessedModifications { get; private set; }
    public int FailedModifications { get; private set; }

    private readonly List<CalculationJobError> _errors = [];
    public IReadOnlyList<CalculationJobError> Errors => _errors.AsReadOnly();

    public bool HasErrors => _errors.Count > 0;

    public double? ProgressPercent =>
        TotalModifications == 0 ? null
        : (double)ProcessedModifications / TotalModifications * 100;

    private CalculationJob() { }

    public static CalculationJob Queue(
        Guid clientId,
        TaxPeriod taxPeriod,
        CalculationTrigger trigger,
        string requestedBy,
        Guid? entityId = null,
        Guid? jurisdictionId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            EntityId = entityId,
            JurisdictionId = jurisdictionId,
            TaxPeriod = taxPeriod,
            Status = CalculationStatus.Queued,
            Trigger = trigger,
            QueuedAt = DateTime.UtcNow,
            RequestedBy = requestedBy
        };

    public void Start(string workerInstanceId, int totalModifications)
    {
        Status = CalculationStatus.InProgress;
        StartedAt = DateTime.UtcNow;
        WorkerInstanceId = workerInstanceId;
        TotalModifications = totalModifications;
    }

    public void RecordProgress(int count = 1) =>
        ProcessedModifications = Math.Min(ProcessedModifications + count, TotalModifications);

    public void RecordError(Guid? modificationId, string message, string? detail = null)
    {
        FailedModifications++;
        _errors.Add(new CalculationJobError(Guid.NewGuid(), Id, modificationId, message, detail, DateTime.UtcNow));
    }

    public void Complete()
    {
        Status = FailedModifications > 0
            ? CalculationStatus.CompletedWithWarnings
            : CalculationStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void Fail(string reason)
    {
        Status = CalculationStatus.Failed;
        CompletedAt = DateTime.UtcNow;
        _errors.Add(new CalculationJobError(Guid.NewGuid(), Id, null, reason, null, DateTime.UtcNow));
    }

    public void Cancel()
    {
        Status = CalculationStatus.Cancelled;
        CompletedAt = DateTime.UtcNow;
    }
}
