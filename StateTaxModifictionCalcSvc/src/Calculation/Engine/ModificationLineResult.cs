using Domain.Entities;

namespace Calculation.Engine;

/// <summary>Result for one modification line within a calculation context.</summary>
public sealed class ModificationLineResult
{
    public Guid CategoryId { get; init; }
    public ModificationCategory? Category { get; init; }
    public decimal GrossAmount { get; set; }
    public decimal ApportionedAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public string? CalculationDetail { get; set; }
    public bool IsExcluded { get; set; }
    public string? ExclusionReason { get; set; }
}
