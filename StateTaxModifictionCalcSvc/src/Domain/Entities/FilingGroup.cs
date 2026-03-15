using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// Groups entities for consolidated/combined filing purposes.
/// e.g., DivCon, ReportingGroup, ELIM, Adjustment.
/// </summary>
public sealed class FilingGroup
{
    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public FilingGroupType GroupType { get; private set; }
    public FilingMethod FilingMethod { get; private set; }
    public Guid JurisdictionId { get; private set; }
    public bool IsActive { get; private set; } = true;

    private FilingGroup() { }

    public static FilingGroup Create(
        Guid clientId,
        string name,
        FilingGroupType groupType,
        FilingMethod filingMethod,
        Guid jurisdictionId) =>
        new()
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            Name = name,
            GroupType = groupType,
            FilingMethod = filingMethod,
            JurisdictionId = jurisdictionId
        };
}
