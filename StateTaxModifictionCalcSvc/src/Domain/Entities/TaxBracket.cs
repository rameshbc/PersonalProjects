namespace Domain.Entities;

public sealed record TaxBracket(decimal IncomeFloor, decimal? IncomeCeiling, decimal Rate);
