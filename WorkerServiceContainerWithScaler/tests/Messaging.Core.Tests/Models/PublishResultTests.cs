namespace Messaging.Core.Tests.Models;

using Messaging.Core.Models;

public sealed class PublishResultTests
{
    [Fact]
    public void Success_SetsPublishedStatus()
    {
        var result = PublishResult.Success("msg-1");
        Assert.Equal(PublishStatus.Published, result.Status);
        Assert.Equal("msg-1", result.MessageId);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void Suppressed_SetsSuppressedStatus()
    {
        var result = PublishResult.Suppressed("msg-2", 15, "Too many pending");
        Assert.Equal(PublishStatus.Suppressed, result.Status);
        Assert.Equal(15, result.PendingCount);
        Assert.Equal("Too many pending", result.SuppressReason);
    }

    [Fact]
    public void Failed_SetsPublishFailedStatus()
    {
        var ex = new InvalidOperationException("boom");
        var result = PublishResult.Failed("msg-3", ex);
        Assert.Equal(PublishStatus.PublishFailed, result.Status);
        Assert.Same(ex, result.Exception);
    }
}
