using BlazorLppp.Application.Models;
using BlazorLppp.Application.Services;
using BlazorLppp.Data;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Data;

public static class HorskaDocumentSeeder
{
    private const string FolderName = "Методика-Горська";
    private const string StoredFileName = "Методика-Горська.txt";
    private const string RelativePath = $"{FolderName}/{StoredFileName}";
    private const string DisplayFileName = "Методика вивчення схильності до суїцидальної поведінки м.горська.docx";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        var definitionService = services.GetRequiredService<ITestDefinitionService>();
        var dbFactory = services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
        var alreadyImported = await dbContext.TestDocuments
            .AsNoTracking()
            .AnyAsync(
                d => d.RelativePath == RelativePath ||
                     d.OriginalFileName == DisplayFileName ||
                     d.OriginalFileName == StoredFileName ||
                     d.Title.Contains("Горська"),
                cancellationToken);

        if (alreadyImported)
        {
            return;
        }

        var sourcePath = ResolveSourcePath(environment.ContentRootPath);
        if (sourcePath is null)
        {
            return;
        }

        var documentsRoot = Path.Combine(environment.ContentRootPath, "App_Data", "Documents", FolderName);
        Directory.CreateDirectory(documentsRoot);

        var destinationName = Path.GetExtension(sourcePath).Equals(".txt", StringComparison.OrdinalIgnoreCase)
            ? StoredFileName
            : Path.GetFileName(sourcePath);
        var destinationPath = Path.Combine(documentsRoot, destinationName);
        File.Copy(sourcePath, destinationPath, overwrite: true);

        var relative = $"{FolderName}/{destinationName}".Replace('\\', '/');
        var upload = new DocumentUploadResult
        {
            FolderName = FolderName,
            FileName = DisplayFileName,
            RelativePath = relative,
            AbsolutePath = destinationPath,
            SizeBytes = new FileInfo(destinationPath).Length
        };

        await definitionService.ImportUploadedDocumentAsync(upload, destinationPath, cancellationToken);
    }

    private static string? ResolveSourcePath(string contentRootPath)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var candidates = new[]
        {
            Path.Combine(contentRootPath, "SeedDocuments", StoredFileName),
            Path.Combine(contentRootPath, "SeedDocuments", "Методика-Горська.docx"),
            Path.Combine(desktop, "тест", "Методика вивчення схильності до суїцидальної поведінки м.горська.docx"),
            Path.Combine(contentRootPath, "App_Data", "Documents", FolderName, StoredFileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
