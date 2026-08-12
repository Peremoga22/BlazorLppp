using BlazorLppp.Application.Models;
using BlazorLppp.Application.Services;
using BlazorLppp.Data;
using BlazorLppp.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Data;

public static class TestDocumentSeeder
{
    private const string FolderName = "TEST-suicid";
    private const string StoredFileName = "TEST-suicid.docx";
    private const string RelativePath = $"{FolderName}/{StoredFileName}";
    private const string DisplayFileName = "ТЕСТ суїцид.docx";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        var definitionService = services.GetRequiredService<ITestDefinitionService>();
        var dbFactory = services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        var sourcePath = ResolveSourcePath(environment.ContentRootPath);
        if (sourcePath is null)
        {
            return;
        }

        await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await dbContext.TestDocuments
            .AsNoTracking()
            .Include(d => d.Questions)
            .Where(d =>
                d.RelativePath == RelativePath ||
                d.OriginalFileName == DisplayFileName ||
                d.OriginalFileName == StoredFileName ||
                d.Title.Contains("суїцид") ||
                d.Title.Contains("СР-45") ||
                d.Title.Contains("CP-45") ||
                d.RelativePath.Contains("TEST-suicid"))
            .ToListAsync(cancellationToken);

        var incompleteIds = candidates
            .Where(d => !IsCompleteSuicideDocument(d))
            .Select(d => d.Id)
            .ToList();

        foreach (var id in incompleteIds)
        {
            await definitionService.DeleteAsync(id, cancellationToken);
        }

        var hasComplete = candidates.Any(d =>
            !incompleteIds.Contains(d.Id) &&
            string.Equals(d.RelativePath, RelativePath, StringComparison.OrdinalIgnoreCase) &&
            IsCompleteSuicideDocument(d));

        if (hasComplete)
        {
            return;
        }

        foreach (var stale in candidates.Where(d =>
                     !incompleteIds.Contains(d.Id) &&
                     !string.Equals(d.RelativePath, RelativePath, StringComparison.OrdinalIgnoreCase)))
        {
            await definitionService.DeleteAsync(stale.Id, cancellationToken);
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

    private static bool IsCompleteSuicideDocument(TestDocument document)
    {
        if (document.Questions.Count < 40)
        {
            return false;
        }

        // Зламаний імпорт / старий UI-баг маскувався порожніми текстами в БД.
        var withText = document.Questions.Count(q =>
            !string.IsNullOrWhiteSpace(q.Text) &&
            q.Text.Any(char.IsLetter));

        return withText >= 40;
    }

    private static string? ResolveSourcePath(string contentRootPath)
    {
        var candidates = new[]
        {
            Path.Combine(contentRootPath, "SeedDocuments", DisplayFileName),
            Path.Combine(contentRootPath, "SeedDocuments", StoredFileName),
            Path.Combine(contentRootPath, "App_Data", "Documents", FolderName, StoredFileName),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "тест",
                DisplayFileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
