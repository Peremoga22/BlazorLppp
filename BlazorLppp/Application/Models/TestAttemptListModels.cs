using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Models;

public class TestAttemptListQuery
{
    public string? Search { get; set; }

    public TestAttemptStatus? Status { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public class TestAttemptListResult
{
    public IReadOnlyList<TestAttempt> Items { get; init; } = [];

    public int TotalCount { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalPages => PageSize <= 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public class TestAttemptStats
{
    public int Total { get; init; }

    public int InProgress { get; init; }

    public int Completed { get; init; }

    public int StartedToday { get; init; }
}
