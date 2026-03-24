namespace SampleWorker.Topic.Messages;

public sealed record OrderShippedMessage(string OrderId, string TrackingNumber, DateTimeOffset ShippedAt);
