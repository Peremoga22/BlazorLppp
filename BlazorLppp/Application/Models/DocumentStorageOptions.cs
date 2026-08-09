namespace BlazorLppp.Application.Models;

public class DocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";

    /// <summary>
    /// Relative to content root, or absolute path.
    /// </summary>
    public string RootPath { get; set; } = "App_Data/Documents";

    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } =
    [
        ".pdf", ".doc", ".docx", ".xls", ".xlsx",
        ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".csv"
    ];
}
