using Application.Commands;
using Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Handlers;

public sealed class CancelCalculationHandler : IRequestHandler<CancelCalculationCommand>
{
    private readonly ICalculationJobRepository _jobRepo;
    private readonly ILogger<CancelCalculationHandler> _logger;

    public CancelCalculationHandler(
        ICalculationJobRepository jobRepo,
        ILogger<CancelCalculationHandler> logger)
    {
        _jobRepo = jobRepo;
        _logger = logger;
    }

    public async Task Handle(CancelCalculationCommand request, CancellationToken ct)
    {
        var job = await _jobRepo.GetByIdAsync(request.JobId, ct);
        if (job is null)
        {
            _logger.LogWarning("Cancel requested for unknown job {JobId}", request.JobId);
            return;
        }

        job.Cancel();
        await _jobRepo.SaveAsync(job, ct);

        _logger.LogInformation("Job {JobId} cancelled by {User}", request.JobId, request.CancelledBy);
    }
}
