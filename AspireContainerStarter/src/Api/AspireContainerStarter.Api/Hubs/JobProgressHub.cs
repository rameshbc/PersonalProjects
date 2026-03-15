using AspireContainerStarter.Contracts.Messages;
using Microsoft.AspNetCore.SignalR;

namespace AspireContainerStarter.Api.Hubs;

/// <summary>
/// SignalR hub that streams real-time job progress to connected clients.
///
/// Clients join a group named after the job ID and receive
/// <see cref="JobProgressMessage"/> updates as the worker processes the job.
///
/// Client-side connection example (JavaScript):
/// <code>
///   const conn = new signalR.HubConnectionBuilder()
///       .withUrl("/hubs/job-progress")
///       .build();
///   await conn.start();
///   await conn.invoke("JoinJob", jobId);
///   conn.on("ReceiveProgress", (progress) => console.log(progress));
/// </code>
/// </summary>
public sealed class JobProgressHub : Hub
{
    /// <summary>Adds the caller to the group for the specified job.</summary>
    public async Task JoinJob(string jobId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(jobId));

    /// <summary>Removes the caller from the job group (optional clean-up).</summary>
    public async Task LeaveJob(string jobId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(jobId));

    internal static string GroupName(string jobId) => $"job-{jobId}";
}
