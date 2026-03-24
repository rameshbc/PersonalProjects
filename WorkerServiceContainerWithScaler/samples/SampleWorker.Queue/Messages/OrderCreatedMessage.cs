namespace SampleWorker.Queue.Messages;

public sealed record OrderCreatedMessage(
    string OrderId,
    string CustomerId,
    decimal Amount,
    DateTimeOffset CreatedAt);
