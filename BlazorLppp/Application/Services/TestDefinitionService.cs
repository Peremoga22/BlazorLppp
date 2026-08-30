using BlazorLppp.Application.Models;
using BlazorLppp.Data;
using BlazorLppp.Domain;
using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

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

        var looksLikeSzch =
            TestDocumentParser.IsSzchFileName(absoluteFilePath) ||
            TestDocumentParser.IsSzchFileName(upload.FileName) ||
            TestDocumentParser.IsSzchFileName(upload.FolderName) ||
            TestDocumentParser.IsSzchTitle(upload.FileName);

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
        catch (Exception) when (looksLikeSzch)
        {
            parsed = SzchDocumentTemplate.Create();
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

        if (looksLikeSzch)
        {
            parsed = SzchDocumentTemplate.Create();
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
        var isManual = document.IsManual;
        dbContext.TestDocuments.Remove(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var resultPath in resultPaths)
        {
            await resultDocumentService.DeleteAsync(resultPath!, cancellationToken);
        }

        if (!isManual && !string.IsNullOrWhiteSpace(relativePath))
        {
            await documentStorageService.DeleteAsync(relativePath, cancellationToken);
        }
    }

    public async Task<TestDocument> CreateManualAsync(
        string title,
        string? instruction = null,
        CancellationToken cancellationToken = default)
    {
        var trimmedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            throw new InvalidOperationException("Вкажіть назву тесту.");
        }

        if (trimmedTitle.Length > 300)
        {
            throw new InvalidOperationException("Назва тесту задовга.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var id = Guid.NewGuid();
        var document = new TestDocument
        {
            Id = id,
            Title = trimmedTitle,
            Instruction = NormalizeInstruction(instruction),
            OriginalFileName = "constructor",
            FolderName = $"constructor-{id:N}",
            RelativePath = $"constructor/{id:N}",
            UploadedAt = DateTime.Now,
            IsActive = false,
            IsRequired = false,
            IsManual = true
        };

        dbContext.TestDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);
        return document;
    }

    public async Task<ConstructorTestDraft?> GetConstructorDraftAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await GetByIdAsync(documentId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        return new ConstructorTestDraft
        {
            Id = document.Id,
            Title = document.Title,
            Instruction = document.Instruction,
            IsActive = document.IsActive,
            IsRequired = document.IsRequired,
            Questions = document.Questions
                .OrderBy(q => q.SortOrder)
                .Select(q => new ConstructorQuestionDraft
                {
                    Id = q.Id,
                    SortOrder = q.SortOrder,
                    Text = q.Text,
                    Hint = q.Hint,
                    Type = q.Type,
                    AnswerStyle = AnswerOptionStyle.Default,
                    ScaleMin = q.ScaleMin,
                    ScaleMax = q.ScaleMax,
                    Options = q.Options
                        .OrderBy(o => o.SortOrder)
                        .Select(o => new ConstructorOptionDraft
                        {
                            Id = o.Id,
                            SortOrder = o.SortOrder,
                            Key = o.Key,
                            Text = o.Text
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    public async Task SaveConstructorDraftAsync(
        ConstructorTestDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var title = draft.Title.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Вкажіть назву тесту.");
        }

        if (draft.Questions.Count == 0)
        {
            throw new InvalidOperationException("Додайте хоча б одне питання.");
        }

        var incomingQuestions = draft.Questions
            .Select((q, index) => NormalizeQuestion(q, index + 1))
            .ToList();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var document = await dbContext.TestDocuments
            .Include(d => d.Questions)
            .ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(d => d.Id == draft.Id, cancellationToken)
            ?? throw new InvalidOperationException("Тест не знайдено.");

        document.Title = title;
        document.Instruction = NormalizeInstruction(draft.Instruction);
        document.IsManual = true;

        var existingQuestionIds = document.Questions.Select(q => q.Id).ToList();
        if (existingQuestionIds.Count > 0)
        {
            var answers = await dbContext.TestAnswers
                .Where(a => existingQuestionIds.Contains(a.TestQuestionId))
                .ToListAsync(cancellationToken);
            if (answers.Count > 0)
            {
                dbContext.TestAnswers.RemoveRange(answers);
            }

            dbContext.TestQuestions.RemoveRange(document.Questions.ToList());
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        foreach (var incoming in incomingQuestions)
        {
            var question = new TestQuestion
            {
                Id = Guid.NewGuid(),
                TestDocumentId = document.Id,
                SortOrder = incoming.SortOrder,
                Text = incoming.Text,
                Hint = incoming.Hint,
                Type = incoming.Type,
                AnswerStyle = AnswerOptionStyle.Default,
                ScaleMin = incoming.ScaleMin,
                ScaleMax = incoming.ScaleMax
            };

            foreach (var option in incoming.Options)
            {
                question.Options.Add(new TestOption
                {
                    Id = Guid.NewGuid(),
                    TestQuestionId = question.Id,
                    SortOrder = option.SortOrder,
                    Key = option.Key,
                    Text = option.Text
                });
            }

            dbContext.TestQuestions.Add(question);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static ConstructorQuestionDraft NormalizeQuestion(ConstructorQuestionDraft question, int sortOrder)
    {
        var text = question.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"Питання {sortOrder}: вкажіть текст.");
        }

        var type = question.Type;

        var options = question.Options
            .Select(o => new ConstructorOptionDraft
            {
                Id = o.Id,
                Key = o.Key.Trim(),
                Text = o.Text.Trim()
            })
            .Where(o => !string.IsNullOrWhiteSpace(o.Key) || !string.IsNullOrWhiteSpace(o.Text))
            .ToList();

        if (type == QuestionType.YesNo)
        {
            options = EnsureYesNoOptions(options);
        }
        else if (type == QuestionType.Scale)
        {
            var min = question.ScaleMin ?? 1;
            var max = question.ScaleMax ?? 5;
            if (max < min)
            {
                (min, max) = (max, min);
            }

            if (max - min > 20)
            {
                throw new InvalidOperationException($"Питання {sortOrder}: занадто широка шкала.");
            }

            question.ScaleMin = min;
            question.ScaleMax = max;
            if (options.Count == 0)
            {
                options = Enumerable.Range(min, max - min + 1)
                    .Select(value => new ConstructorOptionDraft
                    {
                        Key = value.ToString(),
                        Text = ScaleLabels.ForValue(value)
                    })
                    .ToList();
            }
        }

        if (type is QuestionType.SingleChoice or QuestionType.MultiChoice or QuestionType.YesNo)
        {
            if (options.Count < 2)
            {
                throw new InvalidOperationException($"Питання {sortOrder}: додайте щонайменше два варіанти відповіді.");
            }
        }

        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            option.SortOrder = i + 1;
            if (string.IsNullOrWhiteSpace(option.Text))
            {
                option.Text = string.IsNullOrWhiteSpace(option.Key) ? (i + 1).ToString() : option.Key;
            }

            option.Key = type == QuestionType.Scale
                ? ((question.ScaleMin ?? 1) + i).ToString()
                : (i + 1).ToString();
            if (option.Key.Length > 20)
            {
                option.Key = option.Key[..20];
            }
        }

        question.SortOrder = sortOrder;
        question.Text = text;
        question.Hint = string.IsNullOrWhiteSpace(question.Hint) ? null : question.Hint.Trim();
        question.Type = type;
        question.AnswerStyle = AnswerOptionStyle.Default;
        question.Options = options;
        return question;
    }

    private static List<ConstructorOptionDraft> EnsureYesNoOptions(List<ConstructorOptionDraft> options)
    {
        if (options.Count >= 2)
        {
            return options.Take(2).ToList();
        }

        return
        [
            new ConstructorOptionDraft { Key = "Так", Text = "Так" },
            new ConstructorOptionDraft { Key = "Ні", Text = "Ні" }
        ];
    }

    private static string? NormalizeInstruction(string? instruction)
    {
        var value = instruction?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length > 2000 ? value[..2000] : value;
    }
}
