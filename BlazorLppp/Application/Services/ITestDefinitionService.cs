using BlazorLppp.Application.Models;
using BlazorLppp.Domain.Entities;

namespace BlazorLppp.Application.Services;

public interface ITestDefinitionService
{
    Task<TestDocument> ImportUploadedDocumentAsync(
        DocumentUploadResult upload,
        string absoluteFilePath,
        CancellationToken cancellationToken = default);

    Task<TestDocument?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<TestDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TestDocument>> ListAsync(CancellationToken cancellationToken = default);

    Task SetActiveAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task SetRequiredAsync(Guid documentId, bool isRequired, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid documentId, CancellationToken cancellationToken = default);
}
