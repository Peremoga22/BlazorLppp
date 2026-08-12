namespace BlazorLppp.Domain.Entities;

public class Employee
{
    public Guid Id { get; set; }

    public Guid DepartmentId { get; set; }

    public Department? Department { get; set; }

    public string LastName { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string MiddleName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public ICollection<TestAttempt> Attempts { get; set; } = new List<TestAttempt>();

    public string DisplayName => $"{LastName} {FirstName} {MiddleName}".Trim();
}
