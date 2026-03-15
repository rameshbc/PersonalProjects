using Calculation.Engine;
using Calculation.Rules;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Calculation.Strategies;

/// <summary>
/// Base implementation shared by all jurisdiction strategies.
/// Provides standard rule dispatch, apportionment calculation, and override handling.
/// Derived classes override specific methods to customize per-state behavior.
/// </summary>
public abstract class BaseJurisdictionStrategy : IJurisdictionModificationStrategy
{
    protected readonly IEnumerable<IModificationRule> _rules;
    protected readonly IApportionmentDataProvider _apportionmentData;
    protected readonly ILogger _logger;

    protected BaseJurisdictionStrategy(
        IEnumerable<IModificationRule> rules,
        IApportionmentDataProvider apportionmentData,
        ILogger logger)
    {
        _rules = rules;
        _apportionmentData = apportionmentData;
        _logger = logger;
    }

    // ── Pre-apportionment ──────────────────────────────────────────────────

    public virtual async Task<ModificationLineResult> ComputePreApportionmentAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default)
    {
        var result = new ModificationLineResult { CategoryId = category.Id, Category = category };

        if (!category.IsAutoCalculable)
        {
            result.IsExcluded = true;
            result.ExclusionReason = "Manual-only category — auto calculation skipped";
            return result;
        }

        var rule = _rules
            .OrderBy(r => r.Priority)
            .FirstOrDefault(r => r.Applies(category, context));

        if (rule is null)
        {
            context.AddWarning(
                $"No calculation rule found for category '{category.Code}'.",
                $"Jurisdiction={context.Jurisdiction.Code}");
            return result;
        }

        var ruleResult = await rule.ComputeAsync(context, category, ct);
        result.GrossAmount = ruleResult.Amount;
        result.CalculationDetail = ruleResult.Detail;

        if (ruleResult.HasWarning)
            context.AddWarning(ruleResult.WarningMessage!, category.Code);

        return result;
    }

    // ── Apportionment ──────────────────────────────────────────────────────

    public virtual async Task<ApportionmentFactor> ComputeApportionmentFactorAsync(
        CalculationContext context,
        CancellationToken ct = default)
    {
        var data = await _apportionmentData.GetAsync(
            context.Entity.Id, context.Jurisdiction.Id, context.TaxPeriod, ct);

        return context.Jurisdiction.ApportionmentMethod switch
        {
            ApportionmentMethod.SingleSales =>
                ApportionmentFactor.SingleSales(data.SalesNumerator, data.SalesDenominator),

            ApportionmentMethod.ThreeFactor =>
                ComputeThreeFactor(data, weight: (1m / 3m, 1m / 3m, 1m / 3m)),

            ApportionmentMethod.DoubleWeightedSales =>
                ComputeThreeFactor(data, weight: (0.5m, 0.25m, 0.25m)),

            _ => ApportionmentFactor.SingleSales(data.SalesNumerator, data.SalesDenominator)
        };
    }

    protected virtual ApportionmentFactor ComputeThreeFactor(
        ApportionmentData data,
        (decimal sales, decimal payroll, decimal property) weight)
    {
        var sf = data.SalesDenominator == 0 ? 0m : data.SalesNumerator / data.SalesDenominator;
        var pf = data.PayrollDenominator is > 0
            ? data.PayrollNumerator!.Value / data.PayrollDenominator!.Value : sf;
        var propf = data.PropertyDenominator is > 0
            ? data.PropertyNumerator!.Value / data.PropertyDenominator!.Value : sf;

        return new ApportionmentFactor
        {
            SalesNumerator = data.SalesNumerator,
            SalesDenominator = data.SalesDenominator,
            PayrollNumerator = data.PayrollNumerator,
            PayrollDenominator = data.PayrollDenominator,
            PropertyNumerator = data.PropertyNumerator,
            PropertyDenominator = data.PropertyDenominator,
            CombinedFactor = sf * weight.sales + pf * weight.payroll + propf * weight.property
        };
    }

    // ── Post-apportionment ─────────────────────────────────────────────────

    public virtual async Task<ModificationLineResult> ComputePostApportionmentAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default) =>
        await ComputePreApportionmentAsync(context, category, ct);
}
