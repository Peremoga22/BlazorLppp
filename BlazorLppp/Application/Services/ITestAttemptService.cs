using BlazorLppp.Application.Models;
using BlazorLppp.Domain.Entities;

namespace BlazorLppp.Application.Services;

public interface ITestAttemptService
{
    Task<TestAttempt> StartAsync(
        RespondentModel respondent,
        CancellationToken cancellationToken = default);

    Task<TestAttempt?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<TestFormModel?> GetFormAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);

    Task SubmitAsync(
        Guid attemptId,
        IReadOnlyList<TestAnswerInput> answers,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TestResultListItem>> GetCompletedResultsAsync(
        CancellationToken cancellationToken = default);

    Task<TestResultDetails?> GetResultDetailsAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);

    Task EnsureResultFileAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);

    Task<TestAttemptListResult> GetListAsync(
        TestAttemptListQuery query,
        CancellationToken cancellationToken = default);

    Task<TestAttemptStats> GetStatsAsync(
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);
}
