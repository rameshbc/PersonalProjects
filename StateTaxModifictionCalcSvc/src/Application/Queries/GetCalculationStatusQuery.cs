using Domain.Entities;
using MediatR;

namespace Application.Queries;

public sealed record GetCalculationStatusQuery(Guid JobId) : IRequest<CalculationJob?>;
