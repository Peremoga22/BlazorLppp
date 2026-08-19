namespace BlazorLppp.Domain.Entities;

public class TestAnswer
{
    public Guid Id { get; set; }

    public Guid TestAttemptId { get; set; }

    public TestAttempt? TestAttempt { get; set; }

    public Guid TestQuestionId { get; set; }

    public TestQuestion? TestQuestion { get; set; }

    public Guid? SelectedOptionId { get; set; }

    public TestOption? SelectedOption { get; set; }

    public int? ScaleValue { get; set; }

    /// <summary>
    /// Для MultiChoice: список Id варіантів через кому; після «|» — вільний текст («Інше»).
    /// </summary>
    public string? TextValue { get; set; }
}
