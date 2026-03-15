using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// A single state modification line — one row in the state modification workpaper.
/// Ties a modification type and amount to a specific entity, jurisdiction, and tax period.
/// </summary>
public sealed class StateModification
{
    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid EntityId { get; private set; }
    public Guid JurisdictionId { get; private set; }
    public Guid ModificationCategoryId { get; private set; }
    public TaxPeriod TaxPeriod { get; private set; } = null!;

    public ModificationType ModificationType { get; private set; }
    public ModificationTiming Timing { get; private set; }
    public ModificationStatus Status { get; private set; }

    public ModificationAmount Amount { get; private set; } = null!;

    /// <summary>Apportioned amount — set by ApportionmentStage for pre-apportionment mods.</summary>
    public decimal? ApportionedAmount { get; private set; }

    /// <summary>Final amount included in state taxable income after all stages.</summary>
    public decimal? FinalAmount { get; private set; }

    public string? Notes { get; private set; }
    public string? CalculationDetail { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? LastCalculatedAt { get; private set; }
    public string? CalculatedBy { get; private set; }

    private readonly List<ModificationAuditEntry> _auditTrail = [];
    public IReadOnlyList<ModificationAuditEntry> AuditTrail => _auditTrail.AsReadOnly();

    private StateModification() { }

    public static StateModification CreateAuto(
        Guid clientId,
        Guid entityId,
        Guid jurisdictionId,
        Guid modificationCategoryId,
        TaxPeriod taxPeriod,
        ModificationType modificationType,
        ModificationTiming timing,
        ModificationAmount amount)
    {
        return new StateModification
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            EntityId = entityId,
            JurisdictionId = jurisdictionId,
            ModificationCategoryId = modificationCategoryId,
            TaxPeriod = taxPeriod,
            ModificationType = modificationType,
            Timing = timing,
            Amount = amount,
            Status = ModificationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static StateModification CreateManual(
        Guid clientId,
        Guid entityId,
        Guid jurisdictionId,
        Guid modificationCategoryId,
        TaxPeriod taxPeriod,
        ModificationType modificationType,
        ModificationTiming timing,
        decimal manualAmount,
        string notes)
    {
        var amount = new ModificationAmount(
            Value: 0,
            SourceDescription: "Manual entry",
            IsSystemCalculated: false,
            OverrideValue: manualAmount);

        return new StateModification
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            EntityId = entityId,
            JurisdictionId = jurisdictionId,
            ModificationCategoryId = modificationCategoryId,
            TaxPeriod = taxPeriod,
            ModificationType = modificationType,
            Timing = timing,
            Amount = amount,
            Status = ModificationStatus.ManualOverride,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };
    }

    // ── State transitions ──────────────────────────────────────────────────

    public void ApplyCalculatedAmount(decimal calculatedValue, string detail, string calculatedBy)
    {
        var previousAmount = Amount.EffectiveValue;
        Amount = Amount with { Value = calculatedValue };
        Status = ModificationStatus.AutoCalculated;
        LastCalculatedAt = DateTime.UtcNow;
        CalculatedBy = calculatedBy;
        CalculationDetail = detail;
        RecordAudit("AutoCalculated", previousAmount, calculatedValue, calculatedBy);
    }

    public void ApplyApportionment(decimal apportionedAmount, decimal factor)
    {
        ApportionedAmount = apportionedAmount;
        RecordAudit("ApportionmentApplied",
            Amount.EffectiveValue, apportionedAmount,
            $"Factor={factor:P4}");
    }

    public void SetFinalAmount(decimal finalAmount)
    {
        FinalAmount = finalAmount;
        Status = ModificationStatus.AutoCalculated;
    }

    public void ApplyManualOverride(decimal overrideValue, string reason, string user)
    {
        var previous = Amount.EffectiveValue;
        Amount = Amount.WithOverride(overrideValue);
        Status = ModificationStatus.ManualOverride;
        Notes = reason;
        RecordAudit("ManualOverride", previous, overrideValue, user);
    }

    public void ClearOverride(string user)
    {
        var previous = Amount.EffectiveValue;
        Amount = Amount.ClearOverride();
        Status = ModificationStatus.Pending;
        RecordAudit("OverrideCleared", previous, Amount.Value, user);
    }

    private void RecordAudit(string action, decimal previousValue, decimal newValue, string actor) =>
        _auditTrail.Add(new ModificationAuditEntry(
            Guid.NewGuid(), Id, action, previousValue, newValue, actor, DateTime.UtcNow));
}
