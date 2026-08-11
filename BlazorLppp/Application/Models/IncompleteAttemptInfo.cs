namespace BlazorLppp.Application.Models;

public sealed class IncompleteAttemptInfo
{
    public Guid AttemptId { get; init; }

    public Guid? TestDocumentId { get; init; }

    public string TestTitle { get; init; } = string.Empty;

    public int NumberUnit { get; init; }

    public DateTime StartedAt { get; init; }
}
