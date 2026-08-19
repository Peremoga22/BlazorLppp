using BlazorLppp.Application.Models;
using BlazorLppp.Application.Services;
using BlazorLppp.Data;
using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Data;

public static class AssingerDocumentSeeder
{
    private const string FolderName = "Оцінка-Агресивності";
    private const string StoredFileName = "Оцінка Агресивності.docx";
    private const string RelativePath = $"{FolderName}/{StoredFileName}";
    private const string DisplayFileName = "Оцінка Агресивності.docx";

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
                d.OriginalFileName.Contains("Агресивн") ||
                d.OriginalFileName.Contains("агресивн") ||
                d.OriginalFileName.Contains("Ассингер") ||
                d.Title.Contains("Ассингер") ||
                d.Title.Contains("агресивн") ||
                d.RelativePath.Contains("Агресивн") ||
                d.RelativePath.Contains("агресивн"))
            .ToListAsync(cancellationToken);

        var incompleteIds = candidates
            .Where(d => !IsCompleteAssingerDocument(d))
            .Select(d => d.Id)
            .ToList();

        foreach (var id in incompleteIds)
        {
            await definitionService.DeleteAsync(id, cancellationToken);
        }

        var hasComplete = candidates.Any(d =>
            !incompleteIds.Contains(d.Id) &&
            string.Equals(d.RelativePath, RelativePath, StringComparison.OrdinalIgnoreCase) &&
            IsCompleteAssingerDocument(d));

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

    private static bool IsCompleteAssingerDocument(TestDocument document)
    {
        if (document.Questions.Count != 20)
        {
            return false;
        }

        if (string.Equals(document.Title, "Психологічний тест", StringComparison.Ordinal))
        {
            return false;
        }

        return document.Questions.All(q =>
            q.Type == QuestionType.SingleChoice &&
            q.Options.Count >= 3 &&
            !string.IsNullOrWhiteSpace(q.Text) &&
            q.Text.Any(char.IsLetter));
    }

    private static string? ResolveSourcePath(string contentRootPath)
    {
        var candidates = new[]
        {
            Path.Combine(contentRootPath, "SeedDocuments", DisplayFileName),
            Path.Combine(contentRootPath, "SeedDocuments", StoredFileName),
            Path.Combine(contentRootPath, "App_Data", "Documents", FolderName, StoredFileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
