namespace BlazorLppp.Application.Models;

public class DocumentUploadResult
{
    public required string FolderName { get; init; }

    public required string FileName { get; init; }

    public required string RelativePath { get; init; }

    public long SizeBytes { get; init; }
}

public class StoredDocumentInfo
{
    public required string FolderName { get; init; }

    public required string FileName { get; init; }

    public required string RelativePath { get; init; }

    public long SizeBytes { get; init; }

    public DateTime SavedAt { get; init; }
}
