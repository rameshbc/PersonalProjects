using Application.Commands;
using Application.Interfaces;
using Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Handlers;

public sealed class TriggerCalculationHandler : IRequestHandler<TriggerCalculationCommand, Guid>
{
    private readonly ICalculationJobQueue _queue;
    private readonly ICalculationJobRepository _jobRepo;
    private readonly ILogger<TriggerCalculationHandler> _logger;

    public TriggerCalculationHandler(
        ICalculationJobQueue queue,
        ICalculationJobRepository jobRepo,
        ILogger<TriggerCalculationHandler> logger)
    {
        _queue = queue;
        _jobRepo = jobRepo;
        _logger = logger;
    }

    public async Task<Guid> Handle(TriggerCalculationCommand request, CancellationToken ct)
    {
        var job = CalculationJob.Queue(
            clientId: request.ClientId,
            taxPeriod: request.TaxPeriod,
            trigger: request.Trigger,
            requestedBy: request.RequestedBy,
            entityId: request.EntityId,
            jurisdictionId: request.JurisdictionId);

        await _jobRepo.SaveAsync(job, ct);
        await _queue.EnqueueAsync(job, ct);

        _logger.LogInformation(
            "Calculation job {JobId} queued for Client={ClientId} Period={Period} by {User}",
            job.Id, job.ClientId, job.TaxPeriod, job.RequestedBy);

        return job.Id;
    }
}
