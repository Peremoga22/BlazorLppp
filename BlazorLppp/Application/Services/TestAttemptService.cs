using BlazorLppp.Application.Models;
using BlazorLppp.Data;
using BlazorLppp.Domain;
using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Application.Services;

public class TestAttemptService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ITestDefinitionService testDefinitionService,
    ITestResultDocumentService resultDocumentService,
    IOrganizationService organizationService) : ITestAttemptService
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

        var isAnonymous = respondent.IsAnonymous || AnonymousSurveyScoring.LooksLike(document);
        if (isAnonymous)
        {
            if (respondent.AnonymousRank is null)
            {
                throw new InvalidOperationException("Оберіть категорію: солдат, сержант або офіцер.");
            }
        }
        else if (!UnitNumbers.IsValid(respondent.NumberUnit))
        {
            throw new InvalidOperationException("Оберіть підрозділ.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var hasRequiredTests = await dbContext.TestDocuments
            .AsNoTracking()
            .AnyAsync(d => d.IsRequired && d.Questions.Any(), cancellationToken);

        if (hasRequiredTests && !document.IsRequired && !isAnonymous)
        {
            throw new InvalidOperationException(
                "Цей тест зараз недоступний. Оберіть тест зі списку, визначеного адміністратором.");
        }

        var lastName = isAnonymous ? "Тест" : respondent.LastName.Trim();
        var firstName = isAnonymous ? "анонімний" : respondent.FirstName.Trim();
        var middleName = isAnonymous
            ? AnonymousRankNames.Display(respondent.AnonymousRank!.Value)
            : respondent.MiddleName.Trim();
        var numberUnit = isAnonymous ? 0 : respondent.NumberUnit;

        Guid? employeeId = null;
        if (!isAnonymous)
        {
            var employee = await organizationService.FindOrCreateEmployeeAsync(
                numberUnit,
                lastName,
                firstName,
                middleName,
                cancellationToken);
            employeeId = employee.Id;
        }

        if (!isAnonymous)
        {
            var existingInProgress = await dbContext.TestAttempts
                .FirstOrDefaultAsync(
                    a => a.Status == TestAttemptStatus.InProgress &&
                         a.TestDocumentId == document.Id &&
                         (a.EmployeeId == employeeId ||
                          (a.NumberUnit == numberUnit &&
                           a.LastName == lastName &&
                           a.FirstName == firstName &&
                           a.MiddleName == middleName)),
                    cancellationToken);

            if (existingInProgress is not null)
            {
                if (existingInProgress.EmployeeId is null)
                {
                    existingInProgress.EmployeeId = employeeId;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                return existingInProgress;
            }
        }

        var attempt = new TestAttempt
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            TestDocumentId = document.Id,
            LastName = lastName,
            FirstName = firstName,
            MiddleName = middleName,
            NumberUnit = numberUnit,
            IsAnonymous = isAnonymous,
            AnonymousRank = isAnonymous ? respondent.AnonymousRank : null,
            StartedAt = DateTime.Now,
            CompletedAt = null,
            Status = TestAttemptStatus.InProgress
        };

        dbContext.TestAttempts.Add(attempt);
        await dbContext.SaveChangesAsync(cancellationToken);

        return attempt;
    }

    public async Task<IncompleteAttemptInfo?> FindInProgressAttemptAsync(
        string lastName,
        string firstName,
        string middleName,
        int? numberUnit = null,
        Guid? testDocumentId = null,
        CancellationToken cancellationToken = default)
    {
        var ln = lastName.Trim();
        var fn = firstName.Trim();
        var mn = middleName.Trim();

        if (ln.Length < 2 || fn.Length < 2 || mn.Length < 2)
        {
            return null;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = dbContext.TestAttempts
            .AsNoTracking()
            .Include(a => a.TestDocument)
            .Where(a => a.Status == TestAttemptStatus.InProgress);

        // Порівняння без урахування регістру через нормалізацію в пам’яті після фільтра по статусу
        // (SQL Server CI collation зазвичай і так case-insensitive для nvarchar).
        if (numberUnit is int unit && UnitNumbers.IsValid(unit))
        {
            query = query.Where(a => a.NumberUnit == unit);
        }

        if (testDocumentId is Guid testId && testId != Guid.Empty)
        {
            query = query.Where(a => a.TestDocumentId == testId);
        }

        var candidates = await query
            .OrderByDescending(a => a.StartedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        var match = candidates.FirstOrDefault(a =>
            string.Equals(a.LastName.Trim(), ln, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.FirstName.Trim(), fn, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.MiddleName.Trim(), mn, StringComparison.OrdinalIgnoreCase));

        if (match is null && (numberUnit.HasValue || testDocumentId.HasValue))
        {
            // Якщо з фільтром не знайшли — шукаємо лише за ПІБ.
            candidates = await dbContext.TestAttempts
                .AsNoTracking()
                .Include(a => a.TestDocument)
                .Where(a => a.Status == TestAttemptStatus.InProgress)
                .OrderByDescending(a => a.StartedAt)
                .Take(50)
                .ToListAsync(cancellationToken);

            match = candidates.FirstOrDefault(a =>
                string.Equals(a.LastName.Trim(), ln, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.FirstName.Trim(), fn, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.MiddleName.Trim(), mn, StringComparison.OrdinalIgnoreCase));
        }

        if (match is null)
        {
            return null;
        }

        return new IncompleteAttemptInfo
        {
            AttemptId = match.Id,
            TestDocumentId = match.TestDocumentId,
            TestTitle = match.TestDocument?.Title
                        ?? match.TestDocument?.OriginalFileName
                        ?? "Незавершений тест",
            NumberUnit = match.NumberUnit,
            StartedAt = match.StartedAt
        };
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
            RespondentName = attempt.IsAnonymous
                ? $"Анонімний тест · {AnonymousRankNames.Display(attempt.AnonymousRank ?? AnonymousRank.Soldier)}"
                : $"{attempt.LastName} {attempt.FirstName} {attempt.MiddleName}".Trim(),
            TestTitle = document.Title,
            Instruction = document.Instruction,
            IsCompleted = attempt.Status == TestAttemptStatus.Completed,
            Questions = document.Questions.Select(q =>
            {
                answersByQuestion.TryGetValue(q.Id, out var answer);
                var (multiIds, freeText) = AnonymousSurveyScoring.Unpack(answer?.TextValue);
                if (multiIds.Count == 0 && answer?.SelectedOptionId is Guid selected)
                {
                    multiIds.Add(selected);
                }

                return new TestFormQuestionModel
                {
                    Id = q.Id,
                    SortOrder = q.SortOrder,
                    Text = q.Text,
                    Hint = q.Hint,
                    Type = q.Type,
                    ScaleMin = q.ScaleMin,
                    ScaleMax = q.ScaleMax,
                    MaxSelections = q.Text.Contains("до 3", StringComparison.OrdinalIgnoreCase) ? 3 : null,
                    Options = q.Options
                        .Select(o => new TestFormOptionModel
                        {
                            Id = o.Id,
                            Key = o.Key,
                            Text = o.Text
                        })
                        .ToList(),
                    SelectedOptionId = answer?.SelectedOptionId,
                    SelectedOptionIds = multiIds,
                    ScaleValue = answer?.ScaleValue,
                    TextValue = freeText
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
                ScaleValue = input.ScaleValue,
                TextValue = input.TextValue
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
        int? numberUnit = null,
        int? monthOfYear = null,
        IReadOnlyCollection<Guid>? attemptIds = null,
        bool includeAnonymous = false,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = dbContext.TestAttempts
            .AsNoTracking()
            .Include(a => a.TestDocument)
            .Where(a => a.Status == TestAttemptStatus.Completed);

        if (!includeAnonymous)
        {
            query = query.Where(a => !a.IsAnonymous);
        }

        if (numberUnit.HasValue)
        {
            query = query.Where(a => a.NumberUnit == numberUnit.Value);
        }

        if (monthOfYear is >= 1 and <= 12)
        {
            var month = monthOfYear.Value;
            query = query.Where(a => (a.CompletedAt ?? a.StartedAt).Month == month);
        }

        if (attemptIds is { Count: > 0 })
        {
            query = query.Where(a => attemptIds.Contains(a.Id));
        }

        var items = await query
            .OrderByDescending(a => a.CompletedAt ?? a.StartedAt)
            .ToListAsync(cancellationToken);

        return items.Select(MapResultListItem).ToList();
    }

    public async Task<IReadOnlyList<TestResultListItem>> GetFilteredCompletedResultsAsync(
        TestAttemptListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.Status == TestAttemptStatus.InProgress)
        {
            return [];
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var attempts = ApplyAttemptListFilters(
            dbContext.TestAttempts.AsNoTracking().Include(a => a.TestDocument),
            query,
            forceCompleted: true);

        var items = await attempts
            .OrderBy(a => a.CompletedAt ?? a.StartedAt)
            .ToListAsync(cancellationToken);

        return items.Select(MapResultListItem).ToList();
    }

    public async Task<IReadOnlyList<TestResultListItem>> GetAnonymousResultsAsync(
        AnonymousRank? rank = null,
        int? monthOfYear = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = dbContext.TestAttempts
            .AsNoTracking()
            .Include(a => a.TestDocument)
            .Where(a => a.Status == TestAttemptStatus.Completed && a.IsAnonymous);

        if (rank.HasValue)
        {
            query = query.Where(a => a.AnonymousRank == rank.Value);
        }

        if (monthOfYear is >= 1 and <= 12)
        {
            var month = monthOfYear.Value;
            query = query.Where(a => (a.CompletedAt ?? a.StartedAt).Month == month);
        }

        var items = await query
            .OrderByDescending(a => a.CompletedAt ?? a.StartedAt)
            .ToListAsync(cancellationToken);

        return items.Select(MapResultListItem).ToList();
    }

    public async Task<AnonymousSurveyStatsDto> GetAnonymousStatsAsync(
        AnonymousRank? rank = null,
        int? monthOfYear = null,
        CancellationToken cancellationToken = default)
    {
        var results = await GetAnonymousResultsAsync(rank, monthOfYear, cancellationToken);
        var soldiers = results.Count(r => r.AnonymousRank == AnonymousRank.Soldier);
        var sergeants = results.Count(r => r.AnonymousRank == AnonymousRank.Sergeant);
        var officers = results.Count(r => r.AnonymousRank == AnonymousRank.Officer);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var attemptIds = results.Select(r => r.AttemptId).ToList();
        var answers = attemptIds.Count == 0
            ? []
            : await dbContext.TestAnswers
                .AsNoTracking()
                .Include(a => a.SelectedOption)
                .Include(a => a.TestQuestion)
                .Where(a => attemptIds.Contains(a.TestAttemptId))
                .ToListAsync(cancellationToken);

        var readiness = answers
            .Where(a => a.TestQuestion != null &&
                        a.TestQuestion.SortOrder == AnonymousSurveyScoring.ReadinessSort &&
                        a.SelectedOption != null)
            .GroupBy(a => a.SelectedOption!.Text)
            .Select(g => new AnonymousSurveyChartSlice { Label = ShortChartLabel(g.Key), Count = g.Count() })
            .OrderByDescending(s => s.Count)
            .ToList();

        var combat = answers
            .Where(a => a.TestQuestion != null &&
                        a.TestQuestion.SortOrder == AnonymousSurveyScoring.CombatSort &&
                        a.SelectedOption != null)
            .GroupBy(a => a.SelectedOption!.Text)
            .Select(g => new AnonymousSurveyChartSlice { Label = ShortChartLabel(g.Key), Count = g.Count() })
            .OrderByDescending(s => s.Count)
            .ToList();

        return new AnonymousSurveyStatsDto
        {
            Total = results.Count,
            Soldiers = soldiers,
            Sergeants = sergeants,
            Officers = officers,
            Readiness = readiness,
            CombatExperience = combat
        };
    }

    private TestResultListItem MapResultListItem(TestAttempt a)
    {
        var baseName = resultDocumentService.BuildFileBaseName(a.LastName, a.FirstName, a.MiddleName);
        var hasFile = !string.IsNullOrWhiteSpace(a.ResultRelativePath) &&
                      File.Exists(resultDocumentService.GetAbsolutePath(a.ResultRelativePath));
        var display = a.IsAnonymous
            ? $"Анонімний тест · {(a.AnonymousRank is AnonymousRank rank ? AnonymousRankNames.Display(rank) : "—")}"
            : $"{a.LastName} {a.FirstName} {a.MiddleName}".Trim();

        return new TestResultListItem
        {
            AttemptId = a.Id,
            LastName = a.LastName,
            FirstName = a.FirstName,
            MiddleName = a.MiddleName,
            DisplayName = display,
            TestTitle = a.TestDocument?.Title,
            FileBaseName = baseName,
            ResultRelativePath = a.ResultRelativePath,
            ResultFileName = a.ResultFileName ?? (hasFile ? Path.GetFileName(a.ResultRelativePath) : null),
            HasResultFile = hasFile,
            StartedAt = a.StartedAt,
            CompletedAt = a.CompletedAt,
            NumberUnit = a.NumberUnit,
            IsAnonymous = a.IsAnonymous,
            AnonymousRank = a.AnonymousRank
        };
    }

    private static string ShortChartLabel(string value)
        => value.Length <= 42 ? value : value[..39] + "…";

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
            QuestionType.MultiChoice => FormatMultiChoice(question, answer),
            QuestionType.SingleChoice or QuestionType.YesNo when answer.SelectedOption is not null
                => string.IsNullOrWhiteSpace(answer.SelectedOption.Key) ||
                   answer.SelectedOption.Key is "Так" or "Ні"
                    ? answer.SelectedOption.Text
                    : $"{answer.SelectedOption.Key}. {answer.SelectedOption.Text}",
            _ => "—"
        };
    }

    private static string FormatMultiChoice(TestQuestion question, TestAnswer answer)
    {
        var (ids, extra) = AnonymousSurveyScoring.Unpack(answer.TextValue);
        if (ids.Count == 0 && answer.SelectedOptionId is Guid one)
        {
            ids.Add(one);
        }

        var labels = question.Options
            .Where(o => ids.Contains(o.Id))
            .OrderBy(o => o.SortOrder)
            .Select(o => o.Text)
            .ToList();
        if (!string.IsNullOrWhiteSpace(extra))
        {
            labels.Add(extra);
        }

        return labels.Count == 0 ? "—" : string.Join("; ", labels);
    }

    public async Task<TestAttemptListResult> GetListAsync(
        TestAttemptListQuery query,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var attempts = ApplyAttemptListFilters(
            dbContext.TestAttempts.AsNoTracking(),
            query,
            forceCompleted: false);

        var totalCount = await attempts.CountAsync(cancellationToken);
        var completedCount = query.Status switch
        {
            TestAttemptStatus.InProgress => 0,
            TestAttemptStatus.Completed => totalCount,
            _ => await attempts.CountAsync(a => a.Status == TestAttemptStatus.Completed, cancellationToken)
        };

        var items = await attempts
            .OrderByDescending(a => a.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new TestAttemptListResult
        {
            Items = items,
            TotalCount = totalCount,
            CompletedCount = completedCount,
            Page = page,
            PageSize = pageSize
        };
    }

    private static IQueryable<TestAttempt> ApplyAttemptListFilters(
        IQueryable<TestAttempt> attempts,
        TestAttemptListQuery query,
        bool forceCompleted)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            attempts = attempts.Where(a =>
                a.LastName.Contains(term) ||
                a.FirstName.Contains(term) ||
                a.MiddleName.Contains(term));
        }

        if (forceCompleted)
        {
            attempts = attempts.Where(a => a.Status == TestAttemptStatus.Completed);
        }
        else if (query.Status.HasValue)
        {
            attempts = attempts.Where(a => a.Status == query.Status.Value);
        }

        if (query.Month is >= 1 and <= 12)
        {
            var month = query.Month.Value;
            attempts = attempts.Where(a => (a.CompletedAt ?? a.StartedAt).Month == month);
        }

        return attempts;
    }

    public async Task<TestAttemptStats> GetStatsAsync(
        int? numberUnit = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var today = DateTime.Today;
        var attempts = dbContext.TestAttempts.AsNoTracking().Where(a => !a.IsAnonymous);
        if (numberUnit.HasValue)
        {
            attempts = attempts.Where(a => a.NumberUnit == numberUnit.Value);
        }

        var total = await attempts.CountAsync(cancellationToken);
        var inProgress = await attempts
            .CountAsync(a => a.Status == TestAttemptStatus.InProgress, cancellationToken);
        var completed = await attempts
            .CountAsync(a => a.Status == TestAttemptStatus.Completed, cancellationToken);
        var startedToday = await attempts
            .CountAsync(a => a.StartedAt >= today, cancellationToken);

        // Унікальні люди, які вже пройшли (завершили) хоча б один тест.
        var peopleCompleted = await attempts
            .Where(a => a.Status == TestAttemptStatus.Completed)
            .Select(a => new { a.LastName, a.FirstName, a.MiddleName, a.NumberUnit })
            .Distinct()
            .CountAsync(cancellationToken);

        return new TestAttemptStats
        {
            Total = total,
            PeopleCompleted = peopleCompleted,
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
                input.TextValue = null;
                break;

            case QuestionType.MultiChoice:
                var selected = input.SelectedOptionIds
                    .Where(id => question.Options.Any(o => o.Id == id))
                    .Distinct()
                    .ToList();
                if (selected.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Оберіть хоча б один варіант для питання {question.SortOrder}.");
                }

                var maxSelections = question.Text.Contains("до 3", StringComparison.OrdinalIgnoreCase) ? 3 : 0;
                if (maxSelections > 0 && selected.Count > maxSelections)
                {
                    throw new InvalidOperationException(
                        $"Для питання {question.SortOrder} можна обрати не більше {maxSelections} варіантів.");
                }

                input.SelectedOptionIds = selected;
                input.SelectedOptionId = selected[0];
                input.ScaleValue = null;
                input.TextValue = AnonymousSurveyScoring.Pack(selected, input.TextValue);
                break;

            default:
                throw new InvalidOperationException($"Невідомий тип питання {question.SortOrder}.");
        }
    }
}
