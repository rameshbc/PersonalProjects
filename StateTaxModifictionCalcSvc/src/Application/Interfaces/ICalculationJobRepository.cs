using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.Interfaces;

public interface ICalculationJobRepository
{
    Task<CalculationJob?> GetByIdAsync(Guid jobId, CancellationToken ct = default);
    Task SaveAsync(CalculationJob job, CancellationToken ct = default);
    Task<IReadOnlyList<CalculationJob>> GetByStatusAsync(CalculationStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<CalculationJob>> GetByClientAsync(Guid clientId, TaxPeriod period, CancellationToken ct = default);
}
