using Calculation.Engine;
using Calculation.Rules;
using Domain.Entities;
using Domain.Enums;

namespace Calculation.Rules.Config;

/// <summary>
/// An IModificationRule driven entirely by a RuleDefinition loaded from JSON/YAML.
/// Handles LinearRate, NetOfTwoLines, NetOfTwoLinesWithFloor, PercentageOfLine, LesserOf formulas.
/// CodeBased formula type is resolved externally — this class will never be constructed for it.
/// </summary>
public sealed class ConfigurableModificationRule : IModificationRule
{
    private readonly RuleDefinition _def;

    public ConfigurableModificationRule(RuleDefinition definition)
    {
        _def = definition;
    }

    public string RuleId => _def.RuleId;

    public bool Applies(ModificationCategory category, CalculationContext context)
    {
        if (!string.Equals(category.Code, _def.CategoryCode, StringComparison.OrdinalIgnoreCase))
            return false;

        if (context.TaxPeriod.Year < _def.EffectiveFrom) return false;
        if (_def.EffectiveTo.HasValue && context.TaxPeriod.Year > _def.EffectiveTo.Value) return false;

        var jurisdictionCode = context.Jurisdiction.Code.ToString();

        if (_def.ExcludedJurisdictions.Contains(jurisdictionCode)) return false;

        if (!_def.AppliesToJurisdictions.Contains("ALL")
            && !_def.AppliesToJurisdictions.Contains(jurisdictionCode))
            return false;

        return true;
    }

    public Task<RuleResult> ComputeAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default)
    {
        var jurisdictionCode = context.Jurisdiction.Code.ToString();
        var effectiveRate = ResolveRate(jurisdictionCode, context.TaxPeriod.Year);

        var result = _def.FormulaType switch
        {
            RuleFormulaType.LinearRate => ComputeLinearRate(context, effectiveRate),
            RuleFormulaType.NetOfTwoLines => ComputeNetOfTwo(context, effectiveRate),
            RuleFormulaType.NetOfTwoLinesWithFloor => ComputeNetOfTwoWithFloor(context, effectiveRate),
            RuleFormulaType.PercentageOfLine => ComputePercentage(context, effectiveRate),
            RuleFormulaType.LesserOf => ComputeLesserOf(context),
            _ => throw new InvalidOperationException(
                $"Formula type {_def.FormulaType} not handled by ConfigurableModificationRule.")
        };

        return Task.FromResult(result);
    }

    // ── Rate resolution ────────────────────────────────────────────────────

    /// <summary>
    /// Returns the effective rate for a given jurisdiction and tax year.
    ///
    /// Resolution order:
    ///   1. Jurisdiction has a JurisdictionRateOverrides entry AND a range whose
    ///      EffectiveFrom ≤ taxYear ≤ EffectiveTo (most-recent-applicable range wins).
    ///   2. Jurisdiction has an entry but no range covers this tax year → use top-level Rate.
    ///   3. No entry for this jurisdiction → use top-level Rate.
    ///
    /// This ensures that updating a rate for 2025 has zero effect on 2024 and earlier
    /// calculations — each year resolves its own rate snapshot independently.
    /// </summary>
    private decimal ResolveRate(string jurisdictionCode, int taxYear)
    {
        if (!_def.JurisdictionRateOverrides.TryGetValue(jurisdictionCode, out var ranges))
            return _def.Rate;

        // Most-recently-effective range that still covers taxYear wins.
        var match = ranges
            .Where(r => taxYear >= r.EffectiveFrom
                        && (!r.EffectiveTo.HasValue || taxYear <= r.EffectiveTo.Value))
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefault();

        // No range covers this year → fall back to default rate.
        // (e.g., a jurisdiction with overrides starting 2023 falls back to Rate for 2018-2022)
        return match?.Rate ?? _def.Rate;
    }

    // ── Formula implementations ────────────────────────────────────────────

    private RuleResult ComputeLinearRate(CalculationContext context, decimal rate)
    {
        var sum = _def.InputLines.Sum(l => context.GetFederalLine(l.LineCode) * l.Sign);
        if (_def.MinimumInputAmount.HasValue) sum = Math.Max(sum, _def.MinimumInputAmount.Value);
        var result = sum * rate;
        if (_def.MaximumAmount.HasValue) result = Math.Min(result, _def.MaximumAmount.Value);

        return RuleResult.Of(result,
            $"[{RuleId}] LinearRate: Sum={sum:C} × {rate:P1} = {result:C}");
    }

    private RuleResult ComputeNetOfTwo(CalculationContext context, decimal rate)
    {
        if (_def.InputLines.Count < 2)
            return RuleResult.Zero($"[{RuleId}] NetOfTwoLines requires at least 2 input lines.");

        var a = context.GetFederalLine(_def.InputLines[0].LineCode);
        var b = context.GetFederalLine(_def.InputLines[1].LineCode);
        var net = (a - b) * rate;

        return RuleResult.Of(net,
            $"[{RuleId}] Net={a:C} - {b:C} = {net:C} (rate={rate:P1})");
    }

    private RuleResult ComputeNetOfTwoWithFloor(CalculationContext context, decimal rate)
    {
        if (_def.InputLines.Count < 2)
            return RuleResult.Zero($"[{RuleId}] NetOfTwoLinesWithFloor requires at least 2 input lines.");

        var a = context.GetFederalLine(_def.InputLines[0].LineCode);
        var b = context.GetFederalLine(_def.InputLines[1].LineCode);
        var net = Math.Max(0, a - b) * rate;

        return RuleResult.Of(net,
            $"[{RuleId}] MAX(0, {a:C} - {b:C}) × {rate:P1} = {net:C}");
    }

    private RuleResult ComputePercentage(CalculationContext context, decimal rate)
    {
        if (_def.InputLines.Count < 1)
            return RuleResult.Zero($"[{RuleId}] PercentageOfLine requires at least 1 input line.");

        var value = context.GetFederalLine(_def.InputLines[0].LineCode);
        var result = value * rate;

        return RuleResult.Of(result,
            $"[{RuleId}] {value:C} × {rate:P1} = {result:C}");
    }

    private RuleResult ComputeLesserOf(CalculationContext context)
    {
        if (_def.InputLines.Count < 2)
            return RuleResult.Zero($"[{RuleId}] LesserOf requires at least 2 input lines.");

        var a = context.GetFederalLine(_def.InputLines[0].LineCode);
        var b = context.GetFederalLine(_def.InputLines[1].LineCode);
        var result = Math.Min(a, b);

        return RuleResult.Of(result,
            $"[{RuleId}] MIN({a:C}, {b:C}) = {result:C}");
    }
}
