using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// A legal entity within a client's worldwide structure.
/// Maps to a specific federal filing form and participates in one or more filing groups.
/// </summary>
public sealed class TaxEntity
{
    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public string EntityName { get; private set; } = string.Empty;
    public string EIN { get; private set; } = string.Empty;
    public EntityType EntityType { get; private set; }

    /// <summary>Country of incorporation (ISO 3166-1 alpha-2).</summary>
    public string CountryCode { get; private set; } = "US";

    public bool IsDomestic => CountryCode == "US";

    /// <summary>Parent entity for ownership chain / elimination logic.</summary>
    public Guid? ParentEntityId { get; private set; }

    private readonly List<FilingGroupMembership> _filingGroupMemberships = [];
    public IReadOnlyList<FilingGroupMembership> FilingGroupMemberships =>
        _filingGroupMemberships.AsReadOnly();

    private TaxEntity() { }

    public static TaxEntity Create(
        Guid clientId,
        string entityName,
        string ein,
        EntityType entityType,
        string countryCode = "US",
        Guid? parentEntityId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        return new TaxEntity
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            EntityName = entityName,
            EIN = ein,
            EntityType = entityType,
            CountryCode = countryCode.ToUpperInvariant(),
            ParentEntityId = parentEntityId
        };
    }

    public void JoinFilingGroup(Guid filingGroupId, FilingGroupRole role = FilingGroupRole.Member) =>
        _filingGroupMemberships.Add(new FilingGroupMembership(filingGroupId, role));
}
