using SharepointDocManager.Core.Entities;

namespace SharepointDocManager.Application.Queries;

/// <summary>Returns all documents in a folder for the given client.</summary>
public sealed record GetDocumentListQuery(string ClientId, string FolderId);

/// <summary>Returns the version history of a single document.</summary>
public sealed record GetVersionHistoryQuery(string ClientId, string ItemId);

/// <summary>Returns the Office Online edit URL for a document.</summary>
public sealed record GetOnlineEditUrlQuery(string ClientId, string ItemId);
