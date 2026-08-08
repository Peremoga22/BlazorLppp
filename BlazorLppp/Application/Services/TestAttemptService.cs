using BlazorLppp.Application.Models;
using BlazorLppp.Data;
using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Application.Services;

public class TestAttemptService(ApplicationDbContext dbContext) : ITestAttemptService
{
    public async Task<TestAttempt> StartAsync(
        RespondentModel respondent,
        CancellationToken cancellationToken = default)
    {
        var attempt = new TestAttempt
        {
            Id = Guid.NewGuid(),
            LastName = respondent.LastName.Trim(),
            FirstName = respondent.FirstName.Trim(),
            MiddleName = respondent.MiddleName.Trim(),
            StartedAt = DateTime.UtcNow,
            CompletedAt = null,
            Status = TestAttemptStatus.InProgress
        };

        dbContext.TestAttempts.Add(attempt);
        await dbContext.SaveChangesAsync(cancellationToken);

        return attempt;
    }

    public Task<TestAttempt?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return dbContext.TestAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }
}
