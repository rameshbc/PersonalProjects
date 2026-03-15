using Domain.Entities;
using Domain.Enums;

namespace Domain.Interfaces;

public interface IJurisdictionRepository
{
    Task<Jurisdiction?> GetByIdAsync(Guid jurisdictionId, CancellationToken ct = default);
    Task<Jurisdiction?> GetByCodeAsync(JurisdictionCode code, CancellationToken ct = default);
    Task<IReadOnlyList<Jurisdiction>> GetAllAsync(CancellationToken ct = default);
}
