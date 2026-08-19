using BlazorLppp.Application.Models;
using BlazorLppp.Application.Services;
using BlazorLppp.Data;
using BlazorLppp.Domain.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Data;

public static class AnonymousSurveyDocumentSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("AnonymousSurveyDocumentSeeder");
        try
        {
            await SeedCoreAsync(services, log, cancellationToken);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Анонімне опитування: сид не вдався. Додаток запускається далі.");
        }
    }

    private static async Task SeedCoreAsync(
        IServiceProvider services,
        ILogger log,
        CancellationToken cancellationToken)
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
                d.RelativePath == AnonymousSurveyDocumentTemplate.RelativePath ||
                d.OriginalFileName == AnonymousSurveyDocumentTemplate.DisplayFileName ||
                d.OriginalFileName.Contains("анонімне") ||
                d.Title.Contains("Анонімне опитуван") ||
                d.RelativePath.Contains("анонімне"))
            .ToListAsync(cancellationToken);

        if (candidates.Any(IsComplete))
        {
            return;
        }

        if (candidates.Count > 0)
        {
            var ids = candidates.Select(d => d.Id).ToList();
            var hasAttempts = await dbContext.TestAttempts
                .AsNoTracking()
                .AnyAsync(a => a.TestDocumentId != null && ids.Contains(a.TestDocumentId.Value), cancellationToken);
            if (hasAttempts)
            {
                log.LogWarning("Анонімне опитування вже має спроби — бланк не перезаписую.");
                return;
            }

            foreach (var stale in candidates)
            {
                await definitionService.DeleteAsync(stale.Id, cancellationToken);
            }
        }

        var documentsRoot = Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "Documents",
            AnonymousSurveyDocumentTemplate.FolderName);
        Directory.CreateDirectory(documentsRoot);

        var destinationPath = Path.Combine(documentsRoot, AnonymousSurveyDocumentTemplate.DisplayFileName);
        File.Copy(sourcePath, destinationPath, overwrite: true);

        var upload = new DocumentUploadResult
        {
            FolderName = AnonymousSurveyDocumentTemplate.FolderName,
            FileName = AnonymousSurveyDocumentTemplate.DisplayFileName,
            RelativePath = AnonymousSurveyDocumentTemplate.RelativePath,
            AbsolutePath = destinationPath,
            SizeBytes = new FileInfo(destinationPath).Length
        };

        await definitionService.ImportUploadedDocumentAsync(upload, destinationPath, cancellationToken);
    }

    private static bool IsComplete(Domain.Entities.TestDocument document)
        => document.Questions.Count == AnonymousSurveyDocumentTemplate.QuestionCount &&
           document.Questions.Count(q => q.Type == QuestionType.MultiChoice) >= 2 &&
           !string.Equals(document.Title, "Психологічний тест", StringComparison.Ordinal);

    private static string? ResolveSourcePath(string contentRootPath)
    {
        var candidates = new[]
        {
            Path.Combine(contentRootPath, "SeedDocuments", AnonymousSurveyDocumentTemplate.DisplayFileName),
            Path.Combine(contentRootPath, "App_Data", "Documents", AnonymousSurveyDocumentTemplate.FolderName, AnonymousSurveyDocumentTemplate.DisplayFileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
