namespace AspireContainerStarter.Contracts.Messages;

/// <summary>
/// Message placed on the "calc2-jobs" Service Bus queue
/// to trigger a Calc2 calculation job.
/// </summary>
public sealed record Calc2JobMessage(
    Guid   JobId,
    string TaxYear,
    string StateCode,
    string EntityId,
    DateTimeOffset SubmittedAt);
