using BlazorLppp.Domain.Entities;

namespace BlazorLppp.Application.Services;

public interface ITestResultDocumentService
{
    Task<string> GenerateAsync(
        TestAttempt attempt,
        CancellationToken cancellationToken = default);

    string GetAbsolutePath(string relativePath);

    string BuildFileBaseName(string lastName, string firstName, string middleName);
}
