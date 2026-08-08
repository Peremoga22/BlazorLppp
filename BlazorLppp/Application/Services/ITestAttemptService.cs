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
}
