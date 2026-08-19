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
        var candidates = await dbContext.TestDocuments
            .Include(d => d.Questions)
            .ThenInclude(q => q.Options)
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
            .ToListAsync(cancellationToken);

        var byShape = await dbContext.TestDocuments
            .Include(d => d.Questions)
            .ThenInclude(q => q.Options)
            .Where(d => d.Questions.Count == NpnaDocumentTemplate.QuestionCount)
            .ToListAsync(cancellationToken);

        foreach (var extra in byShape.Where(d => NpnaScoring.LooksLikeNpna(d, d.Questions)))
        {
            if (candidates.All(c => c.Id != extra.Id))
            {
                candidates.Add(extra);
            }
        }

        var repaired = false;
        foreach (var document in candidates)
        {
            if (RepairInPlace(document))
            {
                repaired = true;
            }
        }

        if (repaired)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (candidates.Any(IsCompleteNpnaDocument))
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

    private static bool RepairInPlace(TestDocument document)
    {
        var changed = false;
        if (!string.Equals(document.Title, NpnaDocumentTemplate.CanonicalTitle, StringComparison.Ordinal))
        {
            document.Title = NpnaDocumentTemplate.CanonicalTitle;
            changed = true;
        }

        if (!string.Equals(document.Instruction, NpnaDocumentTemplate.CanonicalInstruction, StringComparison.Ordinal))
        {
            document.Instruction = NpnaDocumentTemplate.CanonicalInstruction;
            changed = true;
        }

        foreach (var question in document.Questions)
        {
            if (question.Type != QuestionType.YesNo ||
                question.ScaleMin.HasValue ||
                question.ScaleMax.HasValue)
            {
                question.Type = QuestionType.YesNo;
                question.ScaleMin = null;
                question.ScaleMax = null;
                question.Hint = null;
                changed = true;
            }

            var hasYes = question.Options.Any(o =>
                o.Key.Equals("Так", StringComparison.OrdinalIgnoreCase) ||
                o.Text.Equals("Так", StringComparison.OrdinalIgnoreCase));
            var hasNo = question.Options.Any(o =>
                o.Key.Equals("Ні", StringComparison.OrdinalIgnoreCase) ||
                o.Text.Equals("Ні", StringComparison.OrdinalIgnoreCase));

            if (!hasYes)
            {
                question.Options.Add(new TestOption
                {
                    Id = Guid.NewGuid(),
                    TestQuestionId = question.Id,
                    SortOrder = 1,
                    Key = "Так",
                    Text = "Так"
                });
                changed = true;
            }

            if (!hasNo)
            {
                question.Options.Add(new TestOption
                {
                    Id = Guid.NewGuid(),
                    TestQuestionId = question.Id,
                    SortOrder = 2,
                    Key = "Ні",
                    Text = "Ні"
                });
                changed = true;
            }
        }

        return changed;
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
