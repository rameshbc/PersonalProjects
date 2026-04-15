using SharepointDocManager.Core.Models;

namespace SharepointDocManager.Application.Commands;

/// <summary>Provisions the full folder tree for a client's DocLibrary-A.</summary>
public sealed record CreateFolderStructureCommand(FolderStructureSpec Spec);
