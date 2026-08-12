namespace BlazorLppp.Domain.Entities;

/// <summary>
/// Збережений бал шкали для конкретного проходження (TestAttempt / TestSession).
/// </summary>
public class TestScaleResult
{
    public Guid Id { get; set; }

    public Guid TestAttemptId { get; set; }

    public TestAttempt? TestAttempt { get; set; }

    public string ScaleCode { get; set; } = string.Empty;

    public int RawScore { get; set; }

    public int? StandardScore { get; set; }

    public string? Interpretation { get; set; }
}
