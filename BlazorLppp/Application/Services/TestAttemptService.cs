using BlazorLppp.Application.Models;
using BlazorLppp.Data;
using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Application.Services;

public class TestAttemptService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ITestDefinitionService testDefinitionService,
    ITestResultDocumentService resultDocumentService) : ITestAttemptService
{
    public async Task<TestAttempt> StartAsync(
        RespondentModel respondent,
        CancellationToken cancellationToken = default)
    {
        if (!respondent.TestDocumentId.HasValue || respondent.TestDocumentId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("Оберіть тест для проходження.");
        }

        var document = await testDefinitionService.GetByIdAsync(
                respondent.TestDocumentId.Value,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Обраний тест не знайдено. Зверніться до адміністратора.");

        if (document.Questions.Count == 0)
        {
            throw new InvalidOperationException(
                "Обраний тест не містить питань. Зверніться до адміністратора.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var attempt = new TestAttempt
        {
            Id = Guid.NewGuid(),
            TestDocumentId = document.Id,
            LastName = respondent.LastName.Trim(),
            FirstName = respondent.FirstName.Trim(),
            MiddleName = respondent.MiddleName.Trim(),
            NumberUnit = respondent.NumberUnit,
            StartedAt = DateTime.Now,
            CompletedAt = null,
            Status = TestAttemptStatus.InProgress
        };

        dbContext.TestAttempts.Add(attempt);
        await dbContext.SaveChangesAsync(cancellationToken);

        return attempt;
    }

    public async Task<TestAttempt?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.TestAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<TestFormModel?> GetFormAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var attempt = await dbContext.TestAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken);

        if (attempt is null)
        {
            return null;
        }

        TestDocument? document = null;
        if (attempt.TestDocumentId.HasValue)
        {
            document = await dbContext.TestDocuments
                .AsNoTracking()
                .Include(d => d.Questions.OrderBy(q => q.SortOrder))
                .ThenInclude(q => q.Options.OrderBy(o => o.SortOrder))
                .FirstOrDefaultAsync(d => d.Id == attempt.TestDocumentId.Value, cancellationToken);
        }

        document ??= await dbContext.TestDocuments
            .AsNoTracking()
            .Include(d => d.Questions.OrderBy(q => q.SortOrder))
            .ThenInclude(q => q.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(d => d.IsActive, cancellationToken);

        if (document is null)
        {
            throw new InvalidOperationException("Для цієї спроби не знайдено документ тесту.");
        }

        var existingAnswers = await dbContext.TestAnswers
            .AsNoTracking()
            .Where(a => a.TestAttemptId == attemptId)
            .ToListAsync(cancellationToken);

        var answersByQuestion = existingAnswers.ToDictionary(a => a.TestQuestionId);

        return new TestFormModel
        {
            AttemptId = attempt.Id,
            RespondentName = $"{attempt.LastName} {attempt.FirstName} {attempt.MiddleName}".Trim(),
            TestTitle = document.Title,
            Instruction = document.Instruction,
            IsCompleted = attempt.Status == TestAttemptStatus.Completed,
            Questions = document.Questions.Select(q =>
            {
                answersByQuestion.TryGetValue(q.Id, out var answer);
                return new TestFormQuestionModel
                {
                    Id = q.Id,
                    SortOrder = q.SortOrder,
                    Text = q.Text,
                    Hint = q.Hint,
                    Type = q.Type,
                    ScaleMin = q.ScaleMin,
                    ScaleMax = q.ScaleMax,
                    Options = q.Options
                        .Select(o => new TestFormOptionModel
                        {
                            Id = o.Id,
                            Key = o.Key,
                            Text = o.Text
                        })
                        .ToList(),
                    SelectedOptionId = answer?.SelectedOptionId,
                    ScaleValue = answer?.ScaleValue
                };
            }).ToList()
        };
    }

    public async Task SubmitAsync(
        Guid attemptId,
        IReadOnlyList<TestAnswerInput> answers,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var attempt = await dbContext.TestAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken)
            ?? throw new InvalidOperationException("Спробу не знайдено.");

        if (attempt.Status == TestAttemptStatus.Completed)
        {
            throw new InvalidOperationException("Цей тест уже завершено.");
        }

        if (!attempt.TestDocumentId.HasValue)
        {
            throw new InvalidOperationException("До спроби не прив’язано документ тесту.");
        }

        var questions = await dbContext.TestQuestions
            .Include(q => q.Options)
            .Where(q => q.TestDocumentId == attempt.TestDocumentId.Value)
            .ToListAsync(cancellationToken);

        if (questions.Count == 0)
        {
            throw new InvalidOperationException("У тесті немає питань.");
        }

        var answerMap = answers.ToDictionary(a => a.QuestionId);
        foreach (var question in questions)
        {
            if (!answerMap.TryGetValue(question.Id, out var input))
            {
                throw new InvalidOperationException($"Немає відповіді на питання {question.SortOrder}.");
            }

            ValidateAnswer(question, input);
        }

        var existing = await dbContext.TestAnswers
            .Where(a => a.TestAttemptId == attemptId)
            .ToListAsync(cancellationToken);
        dbContext.TestAnswers.RemoveRange(existing);

        foreach (var question in questions)
        {
            var input = answerMap[question.Id];
            dbContext.TestAnswers.Add(new TestAnswer
            {
                Id = Guid.NewGuid(),
                TestAttemptId = attemptId,
                TestQuestionId = question.Id,
                SelectedOptionId = input.SelectedOptionId,
                ScaleValue = input.ScaleValue
            });
        }

        attempt.Status = TestAttemptStatus.Completed;
        attempt.CompletedAt = DateTime.Now;

        await dbContext.SaveChangesAsync(cancellationToken);

        var relativePath = await resultDocumentService.GenerateAsync(attempt, cancellationToken);
        attempt.ResultRelativePath = relativePath;
        attempt.ResultFileName = Path.GetFileName(relativePath);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TestResultListItem>> GetCompletedResultsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var items = await dbContext.TestAttempts
            .AsNoTracking()
            .Where(a => a.Status == TestAttemptStatus.Completed)
            .OrderByDescending(a => a.CompletedAt ?? a.StartedAt)
            .ToListAsync(cancellationToken);

        return items.Select(a =>
        {
            var baseName = resultDocumentService.BuildFileBaseName(a.LastName, a.FirstName, a.MiddleName);
            var hasFile = !string.IsNullOrWhiteSpace(a.ResultRelativePath) &&
                          File.Exists(resultDocumentService.GetAbsolutePath(a.ResultRelativePath));

            return new TestResultListItem
            {
                AttemptId = a.Id,
                LastName = a.LastName,
                FirstName = a.FirstName,
                MiddleName = a.MiddleName,
                DisplayName = $"{a.LastName} {a.FirstName} {a.MiddleName}".Trim(),
                FileBaseName = baseName,
                ResultRelativePath = a.ResultRelativePath,
                ResultFileName = a.ResultFileName ?? (hasFile ? Path.GetFileName(a.ResultRelativePath) : null),
                HasResultFile = hasFile,
                StartedAt = a.StartedAt,
                CompletedAt = a.CompletedAt,
                NumberUnit = a.NumberUnit
            };
        }).ToList();
    }

    public async Task<TestResultDetails?> GetResultDetailsAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var attempt = await dbContext.TestAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken);

        if (attempt is null || attempt.Status != TestAttemptStatus.Completed)
        {
            return null;
        }

        string? title = null;
        if (attempt.TestDocumentId.HasValue)
        {
            title = await dbContext.TestDocuments
                .AsNoTracking()
                .Where(d => d.Id == attempt.TestDocumentId.Value)
                .Select(d => d.Title)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var questions = await dbContext.TestQuestions
            .AsNoTracking()
            .Include(q => q.Options)
            .Where(q => attempt.TestDocumentId.HasValue && q.TestDocumentId == attempt.TestDocumentId.Value)
            .OrderBy(q => q.SortOrder)
            .ToListAsync(cancellationToken);

        var answers = await dbContext.TestAnswers
            .AsNoTracking()
            .Include(a => a.SelectedOption)
            .Where(a => a.TestAttemptId == attemptId)
            .ToListAsync(cancellationToken);

        var answerMap = answers.ToDictionary(a => a.TestQuestionId);
        var lines = questions.Select(q =>
        {
            answerMap.TryGetValue(q.Id, out var answer);
            return new TestResultAnswerLine
            {
                SortOrder = q.SortOrder,
                QuestionText = q.Text,
                Type = q.Type,
                AnswerText = FormatAnswerText(q, answer)
            };
        }).ToList();

        var hasFile = !string.IsNullOrWhiteSpace(attempt.ResultRelativePath) &&
                      File.Exists(resultDocumentService.GetAbsolutePath(attempt.ResultRelativePath));

        return new TestResultDetails
        {
            Attempt = attempt,
            TestTitle = title,
            FileBaseName = resultDocumentService.BuildFileBaseName(
                attempt.LastName,
                attempt.FirstName,
                attempt.MiddleName),
            ResultRelativePath = attempt.ResultRelativePath,
            HasResultFile = hasFile,
            Answers = lines
        };
    }

    public async Task EnsureResultFileAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var attempt = await dbContext.TestAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken)
            ?? throw new InvalidOperationException("Спробу не знайдено.");

        if (attempt.Status != TestAttemptStatus.Completed)
        {
            throw new InvalidOperationException("Результат доступний лише для завершених тестів.");
        }

        var relativePath = await resultDocumentService.GenerateAsync(attempt, cancellationToken);
        attempt.ResultRelativePath = relativePath;
        attempt.ResultFileName = Path.GetFileName(relativePath);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string FormatAnswerText(TestQuestion question, TestAnswer? answer)
    {
        if (answer is null)
        {
            return "—";
        }

        return question.Type switch
        {
            QuestionType.Scale => answer.ScaleValue?.ToString() ?? "—",
            QuestionType.SingleChoice or QuestionType.YesNo when answer.SelectedOption is not null
                => string.IsNullOrWhiteSpace(answer.SelectedOption.Key) ||
                   answer.SelectedOption.Key is "Так" or "Ні"
                    ? answer.SelectedOption.Text
                    : $"{answer.SelectedOption.Key}. {answer.SelectedOption.Text}",
            _ => "—"
        };
    }

    public async Task<TestAttemptListResult> GetListAsync(
        TestAttemptListQuery query,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var attempts = dbContext.TestAttempts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            attempts = attempts.Where(a =>
                a.LastName.Contains(term) ||
                a.FirstName.Contains(term) ||
                a.MiddleName.Contains(term));
        }

        if (query.Status.HasValue)
        {
            attempts = attempts.Where(a => a.Status == query.Status.Value);
        }

        var totalCount = await attempts.CountAsync(cancellationToken);

        var items = await attempts
            .OrderByDescending(a => a.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new TestAttemptListResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<TestAttemptStats> GetStatsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var today = DateTime.Today;

        var total = await dbContext.TestAttempts.CountAsync(cancellationToken);
        var inProgress = await dbContext.TestAttempts
            .CountAsync(a => a.Status == TestAttemptStatus.InProgress, cancellationToken);
        var completed = await dbContext.TestAttempts
            .CountAsync(a => a.Status == TestAttemptStatus.Completed, cancellationToken);
        var startedToday = await dbContext.TestAttempts
            .CountAsync(a => a.StartedAt >= today, cancellationToken);

        return new TestAttemptStats
        {
            Total = total,
            InProgress = inProgress,
            Completed = completed,
            StartedToday = startedToday
        };
    }

    public async Task DeleteAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var attempt = await dbContext.TestAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken)
            ?? throw new InvalidOperationException("Спробу не знайдено.");

        var resultRelativePath = attempt.ResultRelativePath;

        dbContext.TestAttempts.Remove(attempt);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(resultRelativePath))
        {
            await resultDocumentService.DeleteAsync(resultRelativePath, cancellationToken);
        }
    }

    private static void ValidateAnswer(TestQuestion question, TestAnswerInput input)
    {
        switch (question.Type)
        {
            case QuestionType.SingleChoice:
            case QuestionType.YesNo:
                if (!input.SelectedOptionId.HasValue ||
                    question.Options.All(o => o.Id != input.SelectedOptionId.Value))
                {
                    throw new InvalidOperationException(
                        $"Оберіть варіант відповіді для питання {question.SortOrder}.");
                }

                input.ScaleValue = null;
                break;

            case QuestionType.Scale:
                var min = question.ScaleMin ?? 1;
                var max = question.ScaleMax ?? 10;
                if (!input.ScaleValue.HasValue ||
                    input.ScaleValue.Value < min ||
                    input.ScaleValue.Value > max)
                {
                    throw new InvalidOperationException(
                        $"Для питання {question.SortOrder} оберіть значення від {min} до {max}.");
                }

                input.SelectedOptionId = null;
                break;

            default:
                throw new InvalidOperationException($"Невідомий тип питання {question.SortOrder}.");
        }
    }
}
