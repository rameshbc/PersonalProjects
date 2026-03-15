namespace Domain.ValueObjects;

/// <summary>
/// Immutable value representing a modification amount with its source.
/// Positive = addition to state taxable income; negative = subtraction.
/// </summary>
public sealed record ModificationAmount(
    decimal Value,
    string SourceDescription,
    bool IsSystemCalculated,
    decimal? OverrideValue = null)
{
    /// <summary>Effective amount used in computation — override wins when set.</summary>
    public decimal EffectiveValue => OverrideValue ?? Value;

    public bool HasOverride => OverrideValue.HasValue;

    public ModificationAmount WithOverride(decimal overrideValue) =>
        this with { OverrideValue = overrideValue };

    public ModificationAmount ClearOverride() =>
        this with { OverrideValue = null };
}
