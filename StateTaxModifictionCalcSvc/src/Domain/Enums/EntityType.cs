namespace Domain.Enums;

/// <summary>
/// Federal/state tax form type that determines which modifications apply.
/// </summary>
public enum EntityType
{
    /// <summary>C-Corporation — domestic filer (Form 1120)</summary>
    Form1120,

    /// <summary>Controlled Foreign Corporation (Form 5471)</summary>
    Form5471,

    /// <summary>Disregarded Entity — single-member LLC treated as ignored for tax</summary>
    DisregardedEntity,

    /// <summary>Foreign disregarded entity / branch (Form 8858)</summary>
    Form8858,

    /// <summary>S-Corporation (Form 1120-S)</summary>
    Form1120S,

    /// <summary>Partnership (Form 1065)</summary>
    Form1065,

    /// <summary>Real Estate Investment Trust (Form 1120-REIT)</summary>
    REIT,

    /// <summary>Insurance company (Form 1120-L / 1120-PC)</summary>
    InsuranceCompany
}
