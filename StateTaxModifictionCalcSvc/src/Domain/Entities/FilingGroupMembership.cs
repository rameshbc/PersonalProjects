using Domain.Enums;

namespace Domain.Entities;

public sealed record FilingGroupMembership(Guid FilingGroupId, FilingGroupRole Role);
