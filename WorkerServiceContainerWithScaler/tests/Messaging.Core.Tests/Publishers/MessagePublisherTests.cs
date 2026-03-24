namespace Messaging.Core.Tests.Publishers;

using Messaging.Core.Abstractions;
using Messaging.Core.Audit.Models;
using Messaging.Core.Compression;
using Messaging.Core.Models;
using Messaging.Core.Options;
using Messaging.Core.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

public sealed class MessagePublisherTests
{
    [Fact]
    public async Task PublishAsync_WhenPendingCheckSuppresses_ReturnsSuppressed()
    {
        var auditMock = new Mock<IAuditRepository>();
        auditMock
            .Setup(r => r.CountPendingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(20);  // over threshold of 10

        auditMock
            .Setup(r => r.InsertAsync(It.IsAny<MessageAuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        auditMock
            .Setup(r => r.UpdateStatusAsync(It.IsAny<long>(), It.IsAny<MessageStatus>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var opts = new MessagingOptions
        {
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdA==",
            ServiceName = "TestService",
            Audit = new AuditOptions
            {
                Enabled = true,
                ConnectionString = "Server=.;Database=Test;",
                PendingCheck = new PendingCheckOptions
                {
                    Enabled = true,
                    MaxPendingBeforeSuppress = 10,
                    LookbackWindowMinutes = 60
                }
            }
        };

        // We need a real ServiceBusClient but won't actually send — test only reaches suppression gate
        var client = new Azure.Messaging.ServiceBus.ServiceBusClient(opts.ConnectionString);
        var publisher = new MessagePublisher(
            client,
            opts,
            new GZipPayloadCompressor(),
            auditMock.Object,
            NullLogger<MessagePublisher>.Instance);

        var envelope = new MessageEnvelope
        {
            ClientId = "test-client",
            Subject  = "TestMessage",
            Body     = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}")
        };

        var result = await publisher.PublishAsync("test-queue", envelope);

        Assert.Equal(PublishStatus.Suppressed, result.Status);
        Assert.Equal(20, result.PendingCount);
    }

    [Fact]
    public async Task PublishAsync_WhenPendingCheckDisabled_DoesNotQueryDb()
    {
        var auditMock = new Mock<IAuditRepository>();
        auditMock
            .Setup(r => r.InsertAsync(It.IsAny<MessageAuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        auditMock
            .Setup(r => r.UpdateStatusAsync(It.IsAny<long>(), It.IsAny<MessageStatus>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var opts = new MessagingOptions
        {
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdA==",
            ServiceName = "TestService",
            Audit = new AuditOptions
            {
                Enabled = true,
                ConnectionString = "Server=.;Database=Test;",
                PendingCheck = new PendingCheckOptions { Enabled = false }
            }
        };

        var client    = new Azure.Messaging.ServiceBus.ServiceBusClient(opts.ConnectionString);
        var publisher = new MessagePublisher(client, opts,
            new GZipPayloadCompressor(), auditMock.Object,
            NullLogger<MessagePublisher>.Instance);

        var envelope = new MessageEnvelope { ClientId = "c1", Body = "{}".Select(c => (byte)c).ToArray() };

        // Send will fail (no real SB) but CountPendingAsync must never be called
        _ = await publisher.PublishAsync("q", envelope);

        auditMock.Verify(r => r.CountPendingAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
