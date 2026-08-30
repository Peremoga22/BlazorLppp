using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Models;

public class ConstructorTestDraft
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Instruction { get; set; }

    public bool IsActive { get; set; }

    public bool IsRequired { get; set; }

    public List<ConstructorQuestionDraft> Questions { get; set; } = [];
}

public class ConstructorQuestionDraft
{
    public Guid Id { get; set; }

    public int SortOrder { get; set; }

    public string Text { get; set; } = string.Empty;

    public string? Hint { get; set; }

    public QuestionType Type { get; set; } = QuestionType.SingleChoice;

    public AnswerOptionStyle AnswerStyle { get; set; } = AnswerOptionStyle.Default;

    public int? ScaleMin { get; set; }

    public int? ScaleMax { get; set; }

    public List<ConstructorOptionDraft> Options { get; set; } = [];
}

public class ConstructorOptionDraft
{
    public Guid Id { get; set; }

    public int SortOrder { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}
