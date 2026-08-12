namespace BlazorLppp.Domain.Entities;

public class Department
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Номер підрозділу (legacy NumberUnit / сортування).</summary>
    public int Number { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
