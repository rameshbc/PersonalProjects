namespace Domain.Enums;

public enum ApportionmentMethod
{
    /// <summary>100% sales factor (market-based sourcing — most modern states)</summary>
    SingleSales,

    /// <summary>Double-weighted sales + payroll + property</summary>
    DoubleWeightedSales,

    /// <summary>Equal-weighted three-factor (payroll, property, sales)</summary>
    ThreeFactor,

    /// <summary>Massachusetts-style modified three-factor</summary>
    ModifiedThreeFactor,

    /// <summary>Special industry formula (e.g., financial institutions)</summary>
    SpecialIndustry,

    /// <summary>Separate company (no apportionment)</summary>
    SeparateCompany
}
