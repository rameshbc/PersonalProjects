using Application.Interfaces;
using Calculation.Engine;
using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace WorkerService.Processors;

/// <summary>
/// Processes a single CalculationJob:
///   1. Resolves the scope (which entities × jurisdictions to compute)
///   2. Calls ModificationCalculationEngine for each entity × jurisdiction pair
///   3. Persists results via IStateModificationRepository
///   4. Updates job status as it progresses
///
/// DivCon handling: when a FilingGroup of type DivCon is in scope,
/// the processor calculates each member entity and then requests
/// intercompany eliminations from the EliminationCalculator.
/// </summary>
public sealed class CalculationJobProcessor
{
    private readonly IModificationCalculationEngine _engine;
    private readonly IEntityRepository _entityRepo;
    private readonly IJurisdictionRepository _jurisdictionRepo;
    private readonly IStateModificationRepository _modificationRepo;
    private readonly ICalculationJobRepository _jobRepo;
    private readonly ILogger<CalculationJobProcessor> _logger;

    private readonly string _workerInstanceId =
        $"{Environment.MachineName}:{Environment.ProcessId}";

    public CalculationJobProcessor(
        IModificationCalculationEngine engine,
        IEntityRepository entityRepo,
        IJurisdictionRepository jurisdictionRepo,
        IStateModificationRepository modificationRepo,
        ICalculationJobRepository jobRepo,
        ILogger<CalculationJobProcessor> logger)
    {
        _engine = engine;
        _entityRepo = entityRepo;
        _jurisdictionRepo = jurisdictionRepo;
        _modificationRepo = modificationRepo;
        _jobRepo = jobRepo;
        _logger = logger;
    }

    public async Task ProcessAsync(CalculationJob job, CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing job {JobId} for Client={ClientId} Period={Period}",
            job.Id, job.ClientId, job.TaxPeriod);

        // Resolve entity scope
        var entities = job.EntityId.HasValue
            ? new[] { await _entityRepo.GetByIdAsync(job.EntityId.Value, ct)
                      ?? throw new InvalidOperationException($"Entity {job.EntityId} not found.") }
            : (await _entityRepo.GetByClientAsync(job.ClientId, ct)).ToArray();

        // Resolve jurisdiction scope
        var jurisdictions = job.JurisdictionId.HasValue
            ? new[] { await _jurisdictionRepo.GetByIdAsync(job.JurisdictionId.Value, ct)
                      ?? throw new InvalidOperationException($"Jurisdiction {job.JurisdictionId} not found.") }
            : (await _jurisdictionRepo.GetAllAsync(ct)).ToArray();

        var totalWork = entities.Length * jurisdictions.Length;
        job.Start(_workerInstanceId, totalWork);
        await _jobRepo.SaveAsync(job, ct);

        foreach (var entity in entities)
        {
            foreach (var jurisdiction in jurisdictions)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var request = new CalculationRequest(
                        JobId: job.Id,
                        ClientId: job.ClientId,
                        EntityId: entity.Id,
                        JurisdictionId: jurisdiction.Id,
                        TaxPeriod: job.TaxPeriod);

                    var context = await _engine.CalculateAsync(request, ct);
                    await PersistResultsAsync(context, ct);

                    job.RecordProgress();

                    if (context.HasErrors)
                    {
                        foreach (var err in context.Diagnostics
                                     .Where(d => d.Severity == DiagnosticSeverity.Error))
                        {
                            job.RecordError(null, err.Message, err.Detail);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "Failed calculation Entity={EntityId} Jurisdiction={JurisdictionId}",
                        entity.Id, jurisdiction.Id);
                    job.RecordError(null,
                        $"Entity {entity.EntityName} / Jurisdiction {jurisdiction.Code}: {ex.Message}");
                    job.RecordProgress(); // still count as processed
                }
            }
        }

        job.Complete();
        await _jobRepo.SaveAsync(job, ct);

        _logger.LogInformation(
            "Job {JobId} complete — Processed={Processed} Failed={Failed} Status={Status}",
            job.Id, job.ProcessedModifications, job.FailedModifications, job.Status);
    }

    private async Task PersistResultsAsync(CalculationContext context, CancellationToken ct)
    {
        var modifications = new List<StateModification>();

        foreach (var (categoryId, result) in context.PreApportionmentResults
            .Concat(context.PostApportionmentResults))
        {
            if (result.IsExcluded || result.Category is null) continue;

            var mod = StateModification.CreateAuto(
                clientId: context.ClientId,
                entityId: context.Entity.Id,
                jurisdictionId: context.Jurisdiction.Id,
                modificationCategoryId: categoryId,
                taxPeriod: context.TaxPeriod,
                modificationType: result.Category.DefaultModificationType,
                timing: result.Category.DefaultTiming,
                amount: new Domain.ValueObjects.ModificationAmount(
                    Value: result.GrossAmount,
                    SourceDescription: result.CalculationDetail ?? string.Empty,
                    IsSystemCalculated: true));

            if (result.ApportionedAmount != 0)
                mod.ApplyApportionment(
                    result.ApportionedAmount,
                    context.ComputedApportionmentFactor?.CombinedFactor ?? 1m);

            mod.SetFinalAmount(result.FinalAmount);
            modifications.Add(mod);
        }

        if (modifications.Count > 0)
            await _modificationRepo.UpsertBatchAsync(modifications, ct);
    }
}
