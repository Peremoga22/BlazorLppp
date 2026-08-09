using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Models;

public class TestResultListItem
{
    public Guid AttemptId { get; init; }

    public string LastName { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string MiddleName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string FileBaseName { get; init; } = string.Empty;

    public string? ResultRelativePath { get; init; }

    public string? ResultFileName { get; init; }

    public bool HasResultFile { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    public int NumberUnit { get; init; }
}

public class TestResultDetails
{
    public required TestAttempt Attempt { get; init; }

    public string? TestTitle { get; init; }

    public string FileBaseName { get; init; } = string.Empty;

    public string? ResultRelativePath { get; init; }

    public bool HasResultFile { get; init; }

    public IReadOnlyList<TestResultAnswerLine> Answers { get; init; } = [];
}

public class TestResultAnswerLine
{
    public int SortOrder { get; init; }

    public string QuestionText { get; init; } = string.Empty;

    public string AnswerText { get; init; } = string.Empty;

    public QuestionType Type { get; init; }
}
