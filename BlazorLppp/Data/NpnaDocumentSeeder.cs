using BlazorLppp.Application.Models;
using BlazorLppp.Application.Services;
using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Data;

/// <summary>
/// Зашитий бланк НПН-А. Якщо документ уже є — лише править тип/варіанти.
/// Повторний Import видаляє питання і падає на FK TestAnswers (Restrict).
/// </summary>
public static class NpnaDocumentSeeder
{
    private const string FolderName = "НПН-А";
    private const string StoredFileName = "НПН-А.docx";
    private const string RelativePath = $"{FolderName}/{StoredFileName}";
    private const string DisplayFileName = "НПН-А.docx";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("NpnaDocumentSeeder");
        try
        {
            await SeedCoreAsync(services, log, cancellationToken);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "НПН-А: сид не вдався. Додаток запускається далі.");
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
        var candidateIds = await FindCandidateIdsAsync(dbContext, cancellationToken);

        foreach (var documentId in candidateIds)
        {
            await RepairDocumentAsync(dbContext, documentId, log, cancellationToken);
        }

        if (candidateIds.Count > 0)
        {
            log.LogInformation(
                "НПН-А: знайдено {Count} існуючих бланк(ів). Повторний імпорт пропущено.",
                candidateIds.Count);
            return;
        }

        var pathTaken = await dbContext.TestDocuments
            .AsNoTracking()
            .AnyAsync(d => d.RelativePath == RelativePath, cancellationToken);
        if (pathTaken)
        {
            log.LogWarning("НПН-А: шлях {Path} уже зайнятий, імпорт пропущено.", RelativePath);
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

    private static async Task<List<Guid>> FindCandidateIdsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var byName = await dbContext.TestDocuments
            .AsNoTracking()
            .Where(d =>
                d.RelativePath == RelativePath ||
                d.OriginalFileName == DisplayFileName ||
                d.OriginalFileName.Contains("НПН") ||
                d.OriginalFileName.Contains("нпн") ||
                d.OriginalFileName.Contains("NPN") ||
                d.Title.Contains("НПН") ||
                d.Title.Contains("нервово-психічн") ||
                d.RelativePath.Contains("НПН") ||
                d.RelativePath.Contains("нпн") ||
                (d.Instruction != null && d.Instruction.Contains("обстежуваним")))
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        var byShape = await dbContext.TestDocuments
            .AsNoTracking()
            .Where(d => d.Questions.Count == NpnaDocumentTemplate.QuestionCount)
            .Select(d => new
            {
                d.Id,
                FirstText = d.Questions
                    .Where(q => q.SortOrder == 1)
                    .Select(q => q.Text)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var ids = new HashSet<Guid>(byName);
        foreach (var item in byShape)
        {
            if (!string.IsNullOrWhiteSpace(item.FirstText) &&
                item.FirstText.Contains("негарні думки", StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(item.Id);
            }
        }

        return ids.ToList();
    }

    private static async Task RepairDocumentAsync(
        ApplicationDbContext dbContext,
        Guid documentId,
        ILogger log,
        CancellationToken cancellationToken)
    {
        await dbContext.TestDocuments
            .Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(d => d.Title, NpnaDocumentTemplate.CanonicalTitle)
                    .SetProperty(d => d.Instruction, NpnaDocumentTemplate.CanonicalInstruction),
                cancellationToken);

        await dbContext.TestQuestions
            .Where(q => q.TestDocumentId == documentId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(q => q.Type, QuestionType.YesNo)
                    .SetProperty(q => q.ScaleMin, (int?)null)
                    .SetProperty(q => q.ScaleMax, (int?)null)
                    .SetProperty(q => q.Hint, (string?)null),
                cancellationToken);

        var questionIds = await dbContext.TestQuestions
            .AsNoTracking()
            .Where(q => q.TestDocumentId == documentId)
            .Select(q => q.Id)
            .ToListAsync(cancellationToken);

        if (questionIds.Count == 0)
        {
            log.LogWarning("НПН-А: документ {Id} без питань — бланк не перезаписую (можуть бути спроби).", documentId);
            return;
        }

        var existingOptions = await dbContext.TestOptions
            .AsNoTracking()
            .Where(o => questionIds.Contains(o.TestQuestionId))
            .Select(o => new OptionSnapshot(o.TestQuestionId, o.SortOrder, o.Key, o.Text))
            .ToListAsync(cancellationToken);

        var optionsByQuestion = existingOptions
            .GroupBy(o => o.TestQuestionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var toAdd = new List<TestOption>();
        foreach (var questionId in questionIds)
        {
            optionsByQuestion.TryGetValue(questionId, out var options);
            options ??= [];

            var nextSort = options.Count == 0 ? 1 : options.Max(o => o.SortOrder) + 1;
            var hasYes = options.Any(IsYesOption);
            var hasNo = options.Any(IsNoOption);

            if (!hasYes)
            {
                toAdd.Add(new TestOption
                {
                    Id = Guid.NewGuid(),
                    TestQuestionId = questionId,
                    SortOrder = nextSort++,
                    Key = "Так",
                    Text = "Так"
                });
            }

            if (!hasNo)
            {
                toAdd.Add(new TestOption
                {
                    Id = Guid.NewGuid(),
                    TestQuestionId = questionId,
                    SortOrder = nextSort,
                    Key = "Ні",
                    Text = "Ні"
                });
            }
        }

        if (toAdd.Count == 0)
        {
            return;
        }

        try
        {
            dbContext.TestOptions.AddRange(toAdd);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            log.LogWarning(ex, "НПН-А: не вдалося дописати варіанти Так/Ні для документа {Id}.", documentId);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private static bool IsYesOption(OptionSnapshot option) =>
        string.Equals(option.Key, "Так", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option.Text, "Так", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option.Key, "+", StringComparison.OrdinalIgnoreCase);

    private static bool IsNoOption(OptionSnapshot option) =>
        string.Equals(option.Key, "Ні", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option.Text, "Ні", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option.Key, "-", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option.Key, "−", StringComparison.OrdinalIgnoreCase);

    private sealed record OptionSnapshot(Guid TestQuestionId, int SortOrder, string Key, string Text);

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
