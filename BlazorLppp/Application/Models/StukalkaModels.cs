namespace BlazorLppp.Application.Models;

public class StukalkaReport
{
    public Guid Id { get; set; }

    public DateTime CreatedAt { get; set; }

    public string Author { get; set; } = "Анонімно";

    public string Subject { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
