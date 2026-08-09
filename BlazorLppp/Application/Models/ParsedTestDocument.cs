using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Models;

public class ParsedTestDocument
{
    public string Title { get; set; } = "Психологічний тест";

    public string? Instruction { get; set; }

    public List<ParsedTestQuestion> Questions { get; set; } = [];
}

public class ParsedTestQuestion
{
    public int SortOrder { get; set; }

    public string Text { get; set; } = string.Empty;

    public string? Hint { get; set; }

    public QuestionType Type { get; set; }

    public int? ScaleMin { get; set; }

    public int? ScaleMax { get; set; }

    public List<ParsedTestOption> Options { get; set; } = [];
}

public class ParsedTestOption
{
    public int SortOrder { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}
