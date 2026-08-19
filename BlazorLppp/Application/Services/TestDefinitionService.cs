using BlazorLppp.Application.Models;
using BlazorLppp.Data;
using BlazorLppp.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Application.Services;

public class TestDefinitionService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ITestDocumentParser parser,
    IDocumentStorageService documentStorageService,
    ITestResultDocumentService resultDocumentService) : ITestDefinitionService
{
    public async Task<TestDocument> ImportUploadedDocumentAsync(
        DocumentUploadResult upload,
        string absoluteFilePath,
        CancellationToken cancellationToken = default)
    {
        var parsed = ParseOrFallback(upload, absoluteFilePath);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.TestDocuments
            .Include(d => d.Questions)
            .ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(d => d.RelativePath == upload.RelativePath, cancellationToken);

        if (existing is not null)
        {
            dbContext.TestQuestions.RemoveRange(existing.Questions);
            dbContext.TestDocuments.Remove(existing);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await dbContext.TestDocuments
            .Where(d => d.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, false), cancellationToken);

        var document = new TestDocument
        {
            Id = Guid.NewGuid(),
            Title = parsed.Title,
            Instruction = parsed.Instruction,
            OriginalFileName = upload.FileName,
            FolderName = upload.FolderName,
            RelativePath = upload.RelativePath,
            UploadedAt = DateTime.Now,
            IsActive = true
        };

        foreach (var parsedQuestion in parsed.Questions.OrderBy(q => q.SortOrder))
        {
            var question = new TestQuestion
            {
                Id = Guid.NewGuid(),
                TestDocumentId = document.Id,
                SortOrder = parsedQuestion.SortOrder,
                Text = parsedQuestion.Text,
                Hint = parsedQuestion.Hint,
                Type = parsedQuestion.Type,
                ScaleMin = parsedQuestion.ScaleMin,
                ScaleMax = parsedQuestion.ScaleMax
            };

            foreach (var parsedOption in parsedQuestion.Options.OrderBy(o => o.SortOrder))
            {
                question.Options.Add(new TestOption
                {
                    Id = Guid.NewGuid(),
                    TestQuestionId = question.Id,
                    SortOrder = parsedOption.SortOrder,
                    Key = parsedOption.Key,
                    Text = parsedOption.Text
                });
            }

            document.Questions.Add(question);
        }

        dbContext.TestDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        return document;
    }

    private ParsedTestDocument ParseOrFallback(DocumentUploadResult upload, string absoluteFilePath)
    {
        var looksLikeAssinger =
            TestDocumentParser.IsAssingerFileName(absoluteFilePath) ||
            TestDocumentParser.IsAssingerFileName(upload.FileName) ||
            TestDocumentParser.IsAssingerFileName(upload.FolderName) ||
            TestDocumentParser.IsAssingerTitle(upload.FileName);

        var looksLikeNpna =
            TestDocumentParser.IsNpnaFileName(absoluteFilePath) ||
            TestDocumentParser.IsNpnaFileName(upload.FileName) ||
            TestDocumentParser.IsNpnaFileName(upload.FolderName) ||
            TestDocumentParser.IsNpnaTitle(upload.FileName);

        var looksLikeAnonymous =
            TestDocumentParser.IsAnonymousSurveyFileName(absoluteFilePath) ||
            TestDocumentParser.IsAnonymousSurveyFileName(upload.FileName) ||
            TestDocumentParser.IsAnonymousSurveyFileName(upload.FolderName) ||
            TestDocumentParser.IsAnonymousSurveyTitle(upload.FileName);

        ParsedTestDocument parsed;
        try
        {
            parsed = parser.Parse(absoluteFilePath);
        }
        catch (Exception) when (looksLikeAssinger)
        {
            parsed = AssingerDocumentTemplate.Create();
        }
        catch (Exception) when (looksLikeNpna)
        {
            parsed = NpnaDocumentTemplate.Create();
        }
        catch (Exception) when (looksLikeAnonymous)
        {
            parsed = AnonymousSurveyDocumentTemplate.Create();
        }

        if (looksLikeAssinger &&
            (parsed.Questions.Count != 20 ||
             parsed.Questions.Any(q => q.Options.Count < 3 || string.IsNullOrWhiteSpace(q.Text))))
        {
            parsed = AssingerDocumentTemplate.Create();
        }

        if (looksLikeNpna &&
            (parsed.Questions.Count != NpnaDocumentTemplate.QuestionCount ||
             parsed.Questions.Any(q => string.IsNullOrWhiteSpace(q.Text) || !q.Text.Any(char.IsLetter))))
        {
            parsed = NpnaDocumentTemplate.Create();
        }

        if (looksLikeAnonymous)
        {
            parsed = AnonymousSurveyDocumentTemplate.Create();
        }

        return parsed;
    }

    public async Task<TestDocument?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.TestDocuments
            .AsNoTracking()
            .Include(d => d.Questions.OrderBy(q => q.SortOrder))
            .ThenInclude(q => q.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(d => d.IsActive, cancellationToken);
    }

    public async Task<TestDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.TestDocuments
            .AsNoTracking()
            .Include(d => d.Questions.OrderBy(q => q.SortOrder))
            .ThenInclude(q => q.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<TestDocument>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.TestDocuments
            .AsNoTracking()
            .Include(d => d.Questions)
            .ThenInclude(q => q.Options)
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task SetActiveAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var document = await dbContext.TestDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new InvalidOperationException("Документ тесту не знайдено.");

        await dbContext.TestDocuments
            .Where(d => d.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, false), cancellationToken);

        document.IsActive = true;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetRequiredAsync(
        Guid documentId,
        bool isRequired,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var document = await dbContext.TestDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new InvalidOperationException("Документ тесту не знайдено.");

        document.IsRequired = isRequired;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var document = await dbContext.TestDocuments
            .Include(d => d.Questions)
            .ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new InvalidOperationException("Документ тесту не знайдено.");

        var questionIds = document.Questions.Select(q => q.Id).ToList();
        if (questionIds.Count > 0)
        {
            var answers = await dbContext.TestAnswers
                .Where(a => questionIds.Contains(a.TestQuestionId))
                .ToListAsync(cancellationToken);
            dbContext.TestAnswers.RemoveRange(answers);
        }

        var relatedAttempts = await dbContext.TestAttempts
            .Where(a => a.TestDocumentId == documentId)
            .ToListAsync(cancellationToken);
        var resultPaths = relatedAttempts
            .Select(a => a.ResultRelativePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (relatedAttempts.Count > 0)
        {
            dbContext.TestAttempts.RemoveRange(relatedAttempts);
        }

        var relativePath = document.RelativePath;
        dbContext.TestDocuments.Remove(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var resultPath in resultPaths)
        {
            await resultDocumentService.DeleteAsync(resultPath!, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            await documentStorageService.DeleteAsync(relativePath, cancellationToken);
        }
    }
}
