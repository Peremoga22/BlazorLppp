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
        var parsed = parser.Parse(absoluteFilePath);

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
