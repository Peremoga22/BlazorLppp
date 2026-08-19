using BlazorLppp.Application.Models;
using BlazorLppp.Application.Services;
using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Data;

public static class NpnaDocumentSeeder
{
    private const string FolderName = "НПН-А";
    private const string StoredFileName = "НПН-А.docx";
    private const string RelativePath = $"{FolderName}/{StoredFileName}";
    private const string DisplayFileName = "НПН-А.docx";

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
        var candidateIds = await FindCandidateIdsAsync(dbContext, cancellationToken);

        foreach (var documentId in candidateIds)
        {
            await RepairDocumentAsync(dbContext, documentId, cancellationToken);
        }

        var hasComplete = false;
        foreach (var documentId in candidateIds)
        {
            var snapshot = await dbContext.TestDocuments
                .AsNoTracking()
                .Include(d => d.Questions)
                .ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

            if (snapshot is not null && IsCompleteNpnaDocument(snapshot))
            {
                hasComplete = true;
                break;
            }
        }

        if (hasComplete)
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
            return;
        }

        var existingKeys = await dbContext.TestOptions
            .AsNoTracking()
            .Where(o => questionIds.Contains(o.TestQuestionId))
            .Select(o => new { o.TestQuestionId, o.Key, o.Text })
            .ToListAsync(cancellationToken);

        var keysByQuestion = existingKeys
            .GroupBy(o => o.TestQuestionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var toAdd = new List<TestOption>();
        foreach (var questionId in questionIds)
        {
            keysByQuestion.TryGetValue(questionId, out var options);
            options ??= [];

            var hasYes = options.Any(o =>
                string.Equals(o.Key, "Так", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(o.Text, "Так", StringComparison.OrdinalIgnoreCase));
            var hasNo = options.Any(o =>
                string.Equals(o.Key, "Ні", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(o.Text, "Ні", StringComparison.OrdinalIgnoreCase));

            if (!hasYes)
            {
                toAdd.Add(new TestOption
                {
                    Id = Guid.NewGuid(),
                    TestQuestionId = questionId,
                    SortOrder = 1,
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
                    SortOrder = 2,
                    Key = "Ні",
                    Text = "Ні"
                });
            }
        }

        if (toAdd.Count == 0)
        {
            return;
        }

        dbContext.TestOptions.AddRange(toAdd);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
    }

    private static bool IsCompleteNpnaDocument(TestDocument document)
    {
        if (document.Questions.Count != NpnaDocumentTemplate.QuestionCount)
        {
            return false;
        }

        if (string.Equals(document.Title, "Психологічний тест", StringComparison.Ordinal))
        {
            return false;
        }

        return document.Questions.All(q =>
            q.Type == QuestionType.YesNo &&
            q.Options.Count >= 2 &&
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
