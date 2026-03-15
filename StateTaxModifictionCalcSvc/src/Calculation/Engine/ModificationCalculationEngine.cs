using Calculation.Pipeline;
using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Calculation.Engine;

/// <summary>
/// Orchestrates context building and delegates execution to the pipeline.
/// All jurisdiction-specific logic lives in the Strategy layer; this class
/// is intentionally jurisdiction-agnostic.
/// </summary>
public sealed class ModificationCalculationEngine : IModificationCalculationEngine
{
    private readonly ICalculationPipeline _pipeline;
    private readonly IEntityRepository _entityRepo;
    private readonly IJurisdictionRepository _jurisdictionRepo;
    private readonly IModificationCategoryRepository _categoryRepo;
    private readonly IReportLineRepository _reportLineRepo;
    private readonly ILogger<ModificationCalculationEngine> _logger;

    public ModificationCalculationEngine(
        ICalculationPipeline pipeline,
        IEntityRepository entityRepo,
        IJurisdictionRepository jurisdictionRepo,
        IModificationCategoryRepository categoryRepo,
        IReportLineRepository reportLineRepo,
        ILogger<ModificationCalculationEngine> logger)
    {
        _pipeline = pipeline;
        _entityRepo = entityRepo;
        _jurisdictionRepo = jurisdictionRepo;
        _categoryRepo = categoryRepo;
        _reportLineRepo = reportLineRepo;
        _logger = logger;
    }

    public async Task<CalculationContext> CalculateAsync(
        CalculationRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting calculation — Job={JobId} Entity={EntityId} Jurisdiction={JurisdictionId} Period={Period}",
            request.JobId, request.EntityId, request.JurisdictionId, request.TaxPeriod);

        var entity = await _entityRepo.GetByIdAsync(request.EntityId, ct)
            ?? throw new InvalidOperationException($"Entity {request.EntityId} not found.");

        var jurisdiction = await _jurisdictionRepo.GetByIdAsync(request.JurisdictionId, ct)
            ?? throw new InvalidOperationException($"Jurisdiction {request.JurisdictionId} not found.");

        var categories = await _categoryRepo.GetApplicableAsync(
            request.JurisdictionId, entity.EntityType, ct);

        // Bulk-fetch all federal report lines needed for these categories in one round-trip.
        var requiredLines = categories
            .Where(c => c.FederalSourceLine is not null)
            .Select(c => c.FederalSourceLine!)
            .Distinct();

        var federalLines = await _reportLineRepo.GetLineValuesAsync(
            request.EntityId, requiredLines, request.TaxPeriod, ct);

        var context = new CalculationContext
        {
            JobId = request.JobId,
            ClientId = request.ClientId,
            Entity = entity,
            Jurisdiction = jurisdiction,
            TaxPeriod = request.TaxPeriod,
            FilingMethod = jurisdiction.DefaultFilingMethod,
            FederalReportLines = federalLines,
            ApplicableCategories = categories
        };

        await _pipeline.ExecuteAsync(context, ct);

        _logger.LogInformation(
            "Calculation complete — Job={JobId} Entity={EntityId} " +
            "PreMods={Pre} PostMods={Post} Errors={Errors}",
            request.JobId, request.EntityId,
            context.PreApportionmentResults.Count,
            context.PostApportionmentResults.Count,
            context.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));

        return context;
    }
}
