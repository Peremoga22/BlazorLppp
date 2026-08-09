using BlazorLppp.Application.Models;
using BlazorLppp.Data;
using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Application.Services;

public class TestAttemptService(IDbContextFactory<ApplicationDbContext> dbContextFactory) : ITestAttemptService
{
    public async Task<TestAttempt> StartAsync(
        RespondentModel respondent,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var attempt = new TestAttempt
        {
            Id = Guid.NewGuid(),
            LastName = respondent.LastName.Trim(),
            FirstName = respondent.FirstName.Trim(),
            MiddleName = respondent.MiddleName.Trim(),
            NumberUnit = respondent.NumberUnit,
            StartedAt = DateTime.Now,
            CompletedAt = null,
            Status = TestAttemptStatus.InProgress
        };

        dbContext.TestAttempts.Add(attempt);
        await dbContext.SaveChangesAsync(cancellationToken);

        return attempt;
    }

    public async Task<TestAttempt?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.TestAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<TestAttemptListResult> GetListAsync(
        TestAttemptListQuery query,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var attempts = dbContext.TestAttempts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            attempts = attempts.Where(a =>
                a.LastName.Contains(term) ||
                a.FirstName.Contains(term) ||
                a.MiddleName.Contains(term));
        }

        if (query.Status.HasValue)
        {
            attempts = attempts.Where(a => a.Status == query.Status.Value);
        }

        var totalCount = await attempts.CountAsync(cancellationToken);

        var items = await attempts
            .OrderByDescending(a => a.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new TestAttemptListResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<TestAttemptStats> GetStatsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var today = DateTime.Today;

        var total = await dbContext.TestAttempts.CountAsync(cancellationToken);
        var inProgress = await dbContext.TestAttempts
            .CountAsync(a => a.Status == TestAttemptStatus.InProgress, cancellationToken);
        var completed = await dbContext.TestAttempts
            .CountAsync(a => a.Status == TestAttemptStatus.Completed, cancellationToken);
        var startedToday = await dbContext.TestAttempts
            .CountAsync(a => a.StartedAt >= today, cancellationToken);

        return new TestAttemptStats
        {
            Total = total,
            InProgress = inProgress,
            Completed = completed,
            StartedToday = startedToday
        };
    }
}
