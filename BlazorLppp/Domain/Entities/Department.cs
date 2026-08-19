namespace BlazorLppp.Domain.Entities;

public class Department
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Номер підрозділу (legacy NumberUnit / сортування).</summary>
    public int Number { get; set; }

    /// <summary>Заявлена чисельність підрозділу (штат). 0 — не вказано.</summary>
    public int StaffCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
