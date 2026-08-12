using BlazorLppp.Application.Models;
using BlazorLppp.Application.Services;
using BlazorLppp.Data;
using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Data;

public static class Adaptivity200DocumentSeeder
{
    private const string FolderName = "Адаптивність-200";
    private const string StoredFileName = "Адаптивність-200.docx";
    private const string RelativePath = $"{FolderName}/{StoredFileName}";
    private const string DisplayFileName = "Адаптивність-200.docx";

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
                d.OriginalFileName.Contains("Адаптивність") ||
                d.OriginalFileName.Contains("адаптивн") ||
                d.Title.Contains("Адаптивність") ||
                d.Title.Contains("АДАПТИВ") ||
                d.Title.Contains("БОО") ||
                d.RelativePath.Contains("адаптивн") ||
                d.RelativePath.Contains("Адаптивність"))
            .ToListAsync(cancellationToken);

        var incompleteIds = candidates
            .Where(d => !IsCompleteAdaptivityDocument(d))
            .Select(d => d.Id)
            .ToList();

        foreach (var id in incompleteIds)
        {
            await definitionService.DeleteAsync(id, cancellationToken);
        }

        var hasComplete = candidates.Any(d =>
            !incompleteIds.Contains(d.Id) &&
            string.Equals(d.RelativePath, RelativePath, StringComparison.OrdinalIgnoreCase) &&
            IsCompleteAdaptivityDocument(d));

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
        EnsureDocxCopy(sourcePath, destinationPath, environment.ContentRootPath);

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

    private static bool IsCompleteAdaptivityDocument(TestDocument document)
    {
        if (document.Questions.Count is < 180 or > 220)
        {
            return false;
        }

        if (document.Title.Contains("ЗБРОЯ", StringComparison.OrdinalIgnoreCase) ||
            document.Title.Contains("зброя", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var withText = document.Questions.Count(q =>
            !string.IsNullOrWhiteSpace(q.Text) &&
            q.Text.Any(char.IsLetter));

        // Зламаний імпорт: питання отримали шкалу 1–4 від шаблону ЗБРОЯ.
        var wronglyScoredAsZbroya = document.Questions.Count(q =>
            q.SortOrder is >= 1 and <= 20 &&
            q.Type == QuestionType.SingleChoice &&
            q.Options.Count >= 4);

        return withText >= 180 && wronglyScoredAsZbroya < 10;
    }

    private static void EnsureDocxCopy(string sourcePath, string destinationPath, string contentRootPath)
    {
        if (WordDocConverter.IsDocExtension(sourcePath))
        {
            try
            {
                WordDocConverter.ConvertToDocx(sourcePath, destinationPath);
            }
            catch (Exception)
            {
                var seedDocx = Path.Combine(contentRootPath, "SeedDocuments", StoredFileName);
                if (!File.Exists(seedDocx))
                {
                    throw;
                }

                File.Copy(seedDocx, destinationPath, overwrite: true);
                return;
            }

            var seedCopy = Path.Combine(contentRootPath, "SeedDocuments", StoredFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(seedCopy)!);
            File.Copy(destinationPath, seedCopy, overwrite: true);
            return;
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static string? ResolveSourcePath(string contentRootPath)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var candidates = new[]
        {
            // Вже сконвертований .docx у проєкті — найнадійніше
            Path.Combine(contentRootPath, "SeedDocuments", DisplayFileName),
            Path.Combine(contentRootPath, "SeedDocuments", "adaptivity-200.docx"),
            Path.Combine(contentRootPath, "SeedDocuments", "Адаптивність 200.doc"),
            // Оригінал з робочого столу
            Path.Combine(desktop, "тест", "Адаптивність 200.doc"),
            Path.Combine(desktop, "тест", "Адаптивність-200.doc"),
            Path.Combine(desktop, "тест", "Адаптивність 200.docx"),
            Path.Combine(contentRootPath, "App_Data", "Documents", FolderName, StoredFileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
