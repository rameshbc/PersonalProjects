using Microsoft.AspNetCore.SignalR;

namespace SharepointDocManager.Api.Hubs;

/// <summary>
/// SignalR hub that pushes real-time upload progress events to connected browsers.
///
/// Connection flow (JavaScript client):
///   const conn = new HubConnectionBuilder()
///       .withUrl("/hubs/upload-progress")
///       .withAutomaticReconnect()
///       .build();
///
///   // Join the client's upload group to receive progress for that client
///   await conn.start();
///   await conn.invoke("JoinClientGroup", clientId);
///
///   conn.on("uploadProgress", (data) => {
///       console.log(`${data.fileName}: ${data.completed}/${data.total}`);
///   });
///
/// Server pushes:
///   Each completed file in BatchUploadDocumentsHandler calls:
///     _hub.Clients.Group("{clientId}-uploads").SendAsync("uploadProgress", ...)
/// </summary>
public sealed class UploadProgressHub : Hub
{
    private readonly ILogger<UploadProgressHub> _logger;

    public UploadProgressHub(ILogger<UploadProgressHub> logger) => _logger = logger;

    /// <summary>
    /// Client calls this after connecting to subscribe to a specific client's upload events.
    /// </summary>
    public async Task JoinClientGroup(string clientId)
    {
        var groupName = $"{clientId}-uploads";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("Connection {Id} joined group '{Group}'.", Context.ConnectionId, groupName);
    }

    /// <summary>Client calls this to stop receiving events for a client.</summary>
    public async Task LeaveClientGroup(string clientId)
    {
        var groupName = $"{clientId}-uploads";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }
}
