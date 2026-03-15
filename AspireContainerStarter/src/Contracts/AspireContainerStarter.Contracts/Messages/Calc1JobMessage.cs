namespace AspireContainerStarter.Contracts.Messages;

/// <summary>
/// Message placed on the "calc1-jobs" Service Bus queue
/// to trigger a Calc1 calculation job.
/// </summary>
public sealed record Calc1JobMessage(
    Guid   JobId,
    string TaxYear,
    string EntityId,
    DateTimeOffset SubmittedAt);
