#nullable enable

namespace Messaging.Core.Models;

public enum MessageStatus
{
    Queued,
    Published,
    PublishFailed,
    Suppressed,
    Received,
    Processing,
    Completed,
    Failed,
    DeadLettered
}
