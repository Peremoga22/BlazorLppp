using BlazorLppp.Application.Models;

using Microsoft.AspNetCore.Components.Forms;

namespace BlazorLppp.Application.Services;

public interface IDocumentStorageService
{
    Task<DocumentUploadResult> UploadAsync(
        IBrowserFile file,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredDocumentInfo>> ListAsync(
        CancellationToken cancellationToken = default);

    string GetAbsolutePath(string relativePath);
}
