using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Domain.Entities;

public class TestQuestion
{
    public Guid Id { get; set; }

    public Guid TestDocumentId { get; set; }

    public TestDocument? TestDocument { get; set; }

    public int SortOrder { get; set; }

    public string Text { get; set; } = string.Empty;

    public string? Hint { get; set; }

    public QuestionType Type { get; set; }

    public int? ScaleMin { get; set; }

    public int? ScaleMax { get; set; }

    public ICollection<TestOption> Options { get; set; } = new List<TestOption>();
}
