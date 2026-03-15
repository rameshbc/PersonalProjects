using Domain.Entities;
using Domain.Enums;

namespace Domain.Interfaces;

public interface IModificationCategoryRepository
{
    Task<IReadOnlyList<ModificationCategory>> GetApplicableAsync(
        Guid jurisdictionId,
        EntityType entityType,
        CancellationToken ct = default);

    Task<ModificationCategory?> GetByCodeAsync(string code, CancellationToken ct = default);
}
