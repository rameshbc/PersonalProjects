using MediatR;

namespace Application.Commands;

public sealed record CancelCalculationCommand(Guid JobId, string CancelledBy) : IRequest;
