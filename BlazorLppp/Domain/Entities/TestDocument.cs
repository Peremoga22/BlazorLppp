namespace BlazorLppp.Domain.Entities;

public class TestDocument
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Instruction { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    public string FolderName { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; }

    public bool IsActive { get; set; }

    public bool IsRequired { get; set; }

    public ICollection<TestQuestion> Questions { get; set; } = new List<TestQuestion>();
}
