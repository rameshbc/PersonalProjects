#nullable enable

namespace Messaging.Core.Abstractions;

using Messaging.Core.Models;

public interface IMessagePublisher
{
    Task<PublishResult> PublishAsync<T>(
        string destinationName,
        T payload,
        PublishOptions? options = null,
        CancellationToken ct = default) where T : class;

    Task<PublishResult> PublishAsync(
        string destinationName,
        MessageEnvelope envelope,
        PublishOptions? options = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<PublishResult>> PublishBatchAsync(
        string destinationName,
        IReadOnlyList<MessageEnvelope> envelopes,
        PublishOptions? options = null,
        CancellationToken ct = default);
}
