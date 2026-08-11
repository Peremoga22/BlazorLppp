using BlazorLppp.Application.Models;
using BlazorLppp.Application.Services;
using BlazorLppp.Data;
using BlazorLppp.Domain.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Data;

public static class ZbroyaDocumentSeeder
{
    private const string FolderName = "Тест-ЗБРОЯ";
    private const string StoredFileName = "Тест_зброя.docx";
    private const string RelativePath = $"{FolderName}/{StoredFileName}";
    private const string DisplayFileName = "Тест_зброя.docx";

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
            .ThenInclude(q => q.Options)
            .Where(d =>
                d.RelativePath == RelativePath ||
                d.OriginalFileName == DisplayFileName ||
                d.OriginalFileName == "Тест ЗБРОЯ.doc" ||
                d.OriginalFileName == "Тест-ЗБРОЯ.txt" ||
                d.Title.Contains("ЗБРОЯ") ||
                d.Title.Contains("зброя") ||
                d.OriginalFileName.Contains("зброя") ||
                d.OriginalFileName.Contains("ЗБРОЯ") ||
                d.RelativePath.Contains("ЗБРОЯ") ||
                d.RelativePath.Contains("зброя") ||
                d.Questions.Any(q => q.Text.StartsWith("Я спокійний")))
            .ToListAsync(cancellationToken);

        var incompleteIds = candidates
            .Where(d => !IsCompleteZbroyaDocument(d))
            .Select(d => d.Id)
            .ToList();

        foreach (var id in incompleteIds)
        {
            await definitionService.DeleteAsync(id, cancellationToken);
        }

        var hasComplete = candidates.Any(d =>
            !incompleteIds.Contains(d.Id) &&
            string.Equals(d.RelativePath, RelativePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(d.OriginalFileName, DisplayFileName, StringComparison.OrdinalIgnoreCase) &&
            IsCompleteZbroyaDocument(d));

        if (hasComplete)
        {
            return;
        }

        // Видалити застарілий повний бланк з іншим ім’ям файлу, щоб підставити Тест_зброя.docx.
        foreach (var stale in candidates.Where(d =>
                     !incompleteIds.Contains(d.Id) &&
                     (!string.Equals(d.RelativePath, RelativePath, StringComparison.OrdinalIgnoreCase) ||
                      !string.Equals(d.OriginalFileName, DisplayFileName, StringComparison.OrdinalIgnoreCase))))
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

    private static bool IsCompleteZbroyaDocument(Domain.Entities.TestDocument document)
    {
        if (document.Questions.Count < 24)
        {
            return false;
        }

        var reactive = document.Questions
            .Where(q => q.SortOrder is >= 1 and <= 20)
            .ToList();

        if (reactive.Count < 20)
        {
            return false;
        }

        // Типовий зламаний імпорт: одне питання зі шкалою 1–10 замість 1–4.
        return reactive.All(q =>
            q.Type == QuestionType.SingleChoice &&
            q.Options.Count >= 4);
    }

    private static string? ResolveSourcePath(string contentRootPath)
    {
        var candidates = new[]
        {
            Path.Combine(contentRootPath, "SeedDocuments", StoredFileName),
            Path.Combine(contentRootPath, "SeedDocuments", "Тест ЗБРОЯ.docx"),
            Path.Combine(contentRootPath, "SeedDocuments", "Тест-ЗБРОЯ.txt"),
            Path.Combine(contentRootPath, "SeedDocuments", "Тест ЗБРОЯ.doc"),
            Path.Combine(contentRootPath, "App_Data", "Documents", FolderName, StoredFileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
