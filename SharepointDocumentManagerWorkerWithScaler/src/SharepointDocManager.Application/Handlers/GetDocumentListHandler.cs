using SharepointDocManager.Application.Queries;
using SharepointDocManager.Application.Services;
using SharepointDocManager.Core.Entities;

namespace SharepointDocManager.Application.Handlers;

/// <summary>Handles GetDocumentListQuery — lists documents in a folder.</summary>
public sealed class GetDocumentListHandler
{
    private readonly StorageAdapterResolver _resolver;

    public GetDocumentListHandler(StorageAdapterResolver resolver) => _resolver = resolver;

    public async Task<IReadOnlyList<DocumentItem>> HandleAsync(
        GetDocumentListQuery query, CancellationToken ct)
    {
        var adapter = await _resolver.ResolveAsync(query.ClientId, ct);
        return await adapter.ListDocumentsAsync(query.ClientId, query.FolderId, ct);
    }
}

/// <summary>Handles GetVersionHistoryQuery.</summary>
public sealed class GetVersionHistoryHandler
{
    private readonly StorageAdapterResolver _resolver;

    public GetVersionHistoryHandler(StorageAdapterResolver resolver) => _resolver = resolver;

    public async Task<IReadOnlyList<DocumentVersion>> HandleAsync(
        GetVersionHistoryQuery query, CancellationToken ct)
    {
        var adapter = await _resolver.ResolveAsync(query.ClientId, ct);
        return await adapter.GetVersionHistoryAsync(query.ClientId, query.ItemId, ct);
    }
}

/// <summary>Handles GetOnlineEditUrlQuery — returns Office Online edit URL.</summary>
public sealed class GetOnlineEditUrlHandler
{
    private readonly StorageAdapterResolver _resolver;

    public GetOnlineEditUrlHandler(StorageAdapterResolver resolver) => _resolver = resolver;

    public async Task<string> HandleAsync(GetOnlineEditUrlQuery query, CancellationToken ct)
    {
        var adapter = await _resolver.ResolveAsync(query.ClientId, ct);
        return await adapter.GetOnlineEditUrlAsync(query.ClientId, query.ItemId, ct);
    }
}
