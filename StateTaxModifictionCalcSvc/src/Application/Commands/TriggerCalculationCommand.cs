using Domain.Enums;
using Domain.ValueObjects;
using MediatR;

namespace Application.Commands;

/// <summary>
/// Command to trigger a calculation job.
/// Queues the job and returns the JobId for status polling.
/// </summary>
public sealed record TriggerCalculationCommand(
    Guid ClientId,
    TaxPeriod TaxPeriod,
    CalculationTrigger Trigger,
    string RequestedBy,
    Guid? EntityId = null,
    Guid? JurisdictionId = null) : IRequest<Guid>;
