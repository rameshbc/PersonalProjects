namespace SharepointDocManager.Core.Enums;

/// <summary>
/// Identifies which storage platform backs a client's document library.
/// Stored in ClientConfig and used by StorageAdapterResolver to select
/// the correct IDocumentStorageAdapter implementation at runtime.
/// </summary>
public enum StorageBackend
{
    /// <summary>SharePoint Online — classic team/communication site with a document library.</summary>
    SharePointOnline,

    /// <summary>SharePoint Embedded — container-based storage, no full SP site required.</summary>
    SharePointEmbedded
}
