using BlazorLppp.Application.Models;
using BlazorLppp.Application.Services;
using BlazorLppp.Domain.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Data;

public static class SzchDocumentSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("SzchDocumentSeeder");
        try
        {
            await SeedCoreAsync(services, log, cancellationToken);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "СЗЧ-4: сид не вдався. Додаток запускається далі.");
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
                d.RelativePath == SzchDocumentTemplate.RelativePath ||
                d.OriginalFileName == SzchDocumentTemplate.DisplayFileName ||
                d.OriginalFileName.Contains("СЗЧ") ||
                d.OriginalFileName.Contains("сзч") ||
                d.Title.Contains("СЗЧ") ||
                d.Title.Contains("залишити частину") ||
                d.RelativePath.Contains("СЗЧ") ||
                d.RelativePath.Contains("сзч"))
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
                log.LogWarning("СЗЧ-4 уже має спроби — бланк не перезаписую.");
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
            SzchDocumentTemplate.FolderName);
        Directory.CreateDirectory(documentsRoot);

        var destinationPath = Path.Combine(documentsRoot, SzchDocumentTemplate.DisplayFileName);
        File.Copy(sourcePath, destinationPath, overwrite: true);

        var upload = new DocumentUploadResult
        {
            FolderName = SzchDocumentTemplate.FolderName,
            FileName = SzchDocumentTemplate.DisplayFileName,
            RelativePath = SzchDocumentTemplate.RelativePath,
            AbsolutePath = destinationPath,
            SizeBytes = new FileInfo(destinationPath).Length
        };

        await definitionService.ImportUploadedDocumentAsync(upload, destinationPath, cancellationToken);
    }

    private static bool IsComplete(Domain.Entities.TestDocument document)
        => document.Questions.Count == SzchDocumentTemplate.QuestionCount &&
           document.Questions.All(q =>
               q.Type is QuestionType.YesNo or QuestionType.SingleChoice &&
               q.Options.Count >= 2 &&
               !string.IsNullOrWhiteSpace(q.Text) &&
               q.Text.Any(char.IsLetter)) &&
           !string.Equals(document.Title, "Психологічний тест", StringComparison.Ordinal);

    private static string? ResolveSourcePath(string contentRootPath)
    {
        var candidates = new[]
        {
            Path.Combine(contentRootPath, "SeedDocuments", SzchDocumentTemplate.DisplayFileName),
            Path.Combine(contentRootPath, "App_Data", "Documents", SzchDocumentTemplate.FolderName, SzchDocumentTemplate.DisplayFileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
