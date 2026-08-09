using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Domain.Entities;

public class TestAttempt
{
    public Guid Id { get; set; }

    public string LastName { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string MiddleName { get; set; } = string.Empty;

    public int NumberUnit { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public TestAttemptStatus Status { get; set; }
}
