using BlazorLppp.Application.Models;
using BlazorLppp.Application.Services;
using BlazorLppp.Data;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Data;

public static class ZbroyaDocumentSeeder
{
    private const string FolderName = "Тест-ЗБРОЯ";
    private const string StoredFileName = "Тест-ЗБРОЯ.txt";
    private const string RelativePath = $"{FolderName}/{StoredFileName}";
    private const string DisplayFileName = "Тест ЗБРОЯ.doc";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        var definitionService = services.GetRequiredService<ITestDefinitionService>();
        var dbFactory = services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.TestDocuments
            .AsNoTracking()
            .Include(d => d.Questions)
            .FirstOrDefaultAsync(
                d => d.RelativePath == RelativePath ||
                     d.OriginalFileName == DisplayFileName ||
                     d.OriginalFileName == StoredFileName ||
                     d.Title.Contains("ЗБРОЯ"),
                cancellationToken);

        // Переімпорт, якщо раніше зчитало неповний бланк.
        if (existing is not null && existing.Questions.Count >= 24)
        {
            return;
        }

        if (existing is not null)
        {
            await definitionService.DeleteAsync(existing.Id, cancellationToken);
        }

        var sourcePath = ResolveSourcePath(environment.ContentRootPath);
        if (sourcePath is null)
        {
            return;
        }

        var documentsRoot = Path.Combine(environment.ContentRootPath, "App_Data", "Documents", FolderName);
        Directory.CreateDirectory(documentsRoot);

        var destinationPath = Path.Combine(documentsRoot, StoredFileName);
        File.Copy(sourcePath, destinationPath, overwrite: true);

        var upload = new DocumentUploadResult
        {
            FolderName = FolderName,
            FileName = DisplayFileName,
            RelativePath = RelativePath,
            AbsolutePath = destinationPath,
            SizeBytes = new FileInfo(destinationPath).Length
        };

        await definitionService.ImportUploadedDocumentAsync(upload, destinationPath, cancellationToken);
    }

    private static string? ResolveSourcePath(string contentRootPath)
    {
        var candidates = new[]
        {
            Path.Combine(contentRootPath, "SeedDocuments", StoredFileName),
            Path.Combine(contentRootPath, "SeedDocuments", "Тест ЗБРОЯ.txt"),
            Path.Combine(contentRootPath, "App_Data", "Documents", FolderName, StoredFileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
