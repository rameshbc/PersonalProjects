namespace Domain.Enums;

public enum FilingGroupType
{
    /// <summary>Divisional consolidation group</summary>
    DivCon,

    /// <summary>Top-level reporting group</summary>
    ReportingGroup,

    /// <summary>Intercompany elimination entries</summary>
    Elimination,

    /// <summary>Manual adjustment entries</summary>
    Adjustment
}
