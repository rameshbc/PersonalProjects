using SharepointDocManager.Core.Entities;
using SharepointDocManager.Core.Models;

namespace SharepointDocManager.Application.Commands;

/// <summary>Uploads a single document and returns the created DocumentItem.</summary>
public sealed record UploadDocumentCommand(UploadRequest Request);

/// <summary>Uploads multiple documents in parallel. Returns aggregated result.</summary>
public sealed record BatchUploadDocumentsCommand(string ClientId, IReadOnlyList<UploadRequest> Requests);
