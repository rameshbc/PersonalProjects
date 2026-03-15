namespace Domain.Entities;

public sealed record ModificationAuditEntry(
    Guid Id,
    Guid ModificationId,
    string Action,
    decimal PreviousValue,
    decimal NewValue,
    string Actor,
    DateTime Timestamp);
