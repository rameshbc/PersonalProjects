using Application.Interfaces;
using Application.Queries;
using Domain.Entities;
using MediatR;

namespace Application.Handlers;

public sealed class GetCalculationStatusHandler : IRequestHandler<GetCalculationStatusQuery, CalculationJob?>
{
    private readonly ICalculationJobRepository _jobRepo;

    public GetCalculationStatusHandler(ICalculationJobRepository jobRepo)
    {
        _jobRepo = jobRepo;
    }

    public Task<CalculationJob?> Handle(GetCalculationStatusQuery request, CancellationToken ct) =>
        _jobRepo.GetByIdAsync(request.JobId, ct);
}
