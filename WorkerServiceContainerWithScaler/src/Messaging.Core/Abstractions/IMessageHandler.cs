#nullable enable

namespace Messaging.Core.Abstractions;

using Messaging.Core.Models;

public interface IMessageHandler<T> where T : class
{
    Task HandleAsync(T message, MessageContext context, CancellationToken ct);
}
