using BlazorLppp.Domain.Entities;

namespace BlazorLppp.Application.Models;

public class DepartmentListItem
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int Number { get; init; }

    public int EmployeeCount { get; init; }

    public int StaffCount { get; init; }
}

public class EmployeeListItem
{
    public Guid Id { get; init; }

    public Guid DepartmentId { get; init; }

    public string DepartmentName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string MiddleName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public int CompletedTestsCount { get; init; }

    public DateTime? LastCompletedAt { get; init; }
}

public class EmployeeTestTab
{
    public Guid TestDocumentId { get; init; }

    public string TestTitle { get; init; } = string.Empty;

    public int SessionCount { get; init; }

    public DateTime? LastCompletedAt { get; init; }
}

public class EmployeeTestSessionItem
{
    public Guid AttemptId { get; init; }

    public Guid TestDocumentId { get; init; }

    public string TestTitle { get; init; } = string.Empty;

    public DateTime StartedAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    public string StatusName { get; init; } = string.Empty;

    public bool IsCompleted { get; init; }

    public bool HasResultFile { get; init; }

    public string? ResultFileName { get; init; }
}

public class EmployeeCardModel
{
    public required Employee Employee { get; init; }

    public required Department Department { get; init; }

    public IReadOnlyList<EmployeeTestTab> TestTabs { get; init; } = [];

    public IReadOnlyList<EmployeeTestSessionItem> Sessions { get; init; } = [];
}
