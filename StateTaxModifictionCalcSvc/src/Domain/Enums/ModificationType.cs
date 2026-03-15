namespace Domain.Enums;

/// <summary>
/// Classification of a state modification line — drives sign, apportionment timing,
/// and which federal base amount feeds the calculation.
/// </summary>
public enum ModificationType
{
    // ── Additions (increase state taxable income) ──────────────────────────
    Addition,
    InterestExpenseAddback,
    BonusDepreciationAddback,
    GiltiInclusion,
    SubpartFInclusion,
    Section965Inclusion,

    // ── Subtractions (decrease state taxable income) ───────────────────────
    Subtraction,
    NetOperatingLossDeduction,
    DividendReceivedDeduction,
    GiltiDeduction,
    FdiiDeduction,
    InterestExpenseDeduction,
    BonusDepreciationRecovery,
    WatersEdgeAdjustment,

    // ── Manual / override ──────────────────────────────────────────────────
    ManualAdjustment
}
