using BlazorLppp.Domain.Entities;

namespace BlazorLppp.Application.Services;

public interface ITestResultDocumentService
{
    Task<string> GenerateAsync(
        TestAttempt attempt,
        CancellationToken cancellationToken = default);

    string GetAbsolutePath(string relativePath);

    string BuildFileBaseName(string lastName, string firstName, string middleName);

    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Об'єднує кілька готових файлів результатів в один .docx (з розривом сторінки між ними).
    /// </summary>
    Task<(string AbsolutePath, string DownloadFileName)> CombineResultDocumentsAsync(
        IReadOnlyList<string> resultRelativePaths,
        string? fileNameHint = null,
        CancellationToken cancellationToken = default);
}
