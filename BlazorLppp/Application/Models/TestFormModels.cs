using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Models;

public class TestFormModel
{
    public Guid AttemptId { get; init; }

    public string RespondentName { get; init; } = string.Empty;

    public string TestTitle { get; init; } = string.Empty;

    public string? Instruction { get; init; }

    public bool IsCompleted { get; init; }

    public IReadOnlyList<TestFormQuestionModel> Questions { get; init; } = [];
}

public class TestFormQuestionModel
{
    public Guid Id { get; init; }

    public int SortOrder { get; init; }

    public string Text { get; init; } = string.Empty;

    public string? Hint { get; init; }

    public QuestionType Type { get; init; }

    public int? ScaleMin { get; init; }

    public int? ScaleMax { get; init; }

    public IReadOnlyList<TestFormOptionModel> Options { get; init; } = [];

    public Guid? SelectedOptionId { get; set; }

    public List<Guid> SelectedOptionIds { get; set; } = [];

    public int? ScaleValue { get; set; }

    public string? TextValue { get; set; }

    public int? MaxSelections { get; set; }
}

public class TestFormOptionModel
{
    public Guid Id { get; init; }

    public string Key { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;
}

public class TestAnswerInput
{
    public Guid QuestionId { get; set; }

    public Guid? SelectedOptionId { get; set; }

    public List<Guid> SelectedOptionIds { get; set; } = [];

    public int? ScaleValue { get; set; }

    public string? TextValue { get; set; }
}
