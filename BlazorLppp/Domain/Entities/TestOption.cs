namespace BlazorLppp.Domain.Entities;

public class TestOption
{
    public Guid Id { get; set; }

    public Guid TestQuestionId { get; set; }

    public TestQuestion? TestQuestion { get; set; }

    public int SortOrder { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}
