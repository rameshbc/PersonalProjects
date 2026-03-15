using Domain.Entities;

namespace Domain.Interfaces;

public interface IEntityRepository
{
    Task<TaxEntity?> GetByIdAsync(Guid entityId, CancellationToken ct = default);
    Task<IReadOnlyList<TaxEntity>> GetByClientAsync(Guid clientId, CancellationToken ct = default);
}
