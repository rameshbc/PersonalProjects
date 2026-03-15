using Domain.Entities;
using Domain.ValueObjects;

namespace Domain.Interfaces;

public interface IStateModificationRepository
{
    Task<IReadOnlyList<StateModification>> GetByEntityJurisdictionAsync(
        Guid entityId,
        Guid jurisdictionId,
        TaxPeriod period,
        CancellationToken ct = default);

    Task UpsertAsync(StateModification modification, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<StateModification> modifications, CancellationToken ct = default);
}
