using BlazorLppp.Application.Models;
using BlazorLppp.Data;
using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Application.Services;

public class AnalyticsService(IDbContextFactory<ApplicationDbContext> dbContextFactory) : IAnalyticsService
{
    public async Task<OrgAnalyticsOverviewDto> GetOrgOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        var filter = new AnalyticsFilter();
        var summary = await GetSummaryAsync(filter, cancellationToken);
        var coverage = await GetDepartmentAnalyticsAsync(filter, "number", cancellationToken);
        var tests = await GetTestCompletionsAsync(filter, cancellationToken);

        return new OrgAnalyticsOverviewDto
        {
            Summary = summary,
            DepartmentCoverage = coverage,
            TestCompletions = tests
        };
    }

    public async Task<AnalyticsSummaryDto> GetSummaryAsync(
        AnalyticsFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var employeesQuery = ApplyEmployeeFilter(db.Employees.AsNoTracking(), filter);
        var employeesTotal = await employeesQuery.CountAsync(cancellationToken);

        var completed = ApplyAttemptFilter(
            db.TestAttempts.AsNoTracking().Where(a => a.Status == TestAttemptStatus.Completed),
            filter);

        var completionsTotal = await completed.CountAsync(cancellationToken);

        var testedIds = await completed
            .Where(a => a.EmployeeId != null)
            .Select(a => a.EmployeeId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (filter.DepartmentId.HasValue || !string.IsNullOrWhiteSpace(filter.EmployeeSearch))
        {
            var allowed = await employeesQuery.Select(e => e.Id).ToListAsync(cancellationToken);
            var allowedSet = allowed.ToHashSet();
            testedIds = testedIds.Where(allowedSet.Contains).ToList();
        }

        var attention = await GetAttentionRequiredAsync(filter, cancellationToken);
        var attentionEmployees = attention
            .Where(a => a.EmployeeId.HasValue)
            .Select(a => a.EmployeeId!.Value)
            .Distinct()
            .Count();

        var staffQuery = db.Departments.AsNoTracking();
        if (filter.DepartmentId.HasValue)
        {
            staffQuery = staffQuery.Where(d => d.Id == filter.DepartmentId.Value);
        }

        var staffTotal = await staffQuery.SumAsync(d => d.StaffCount, cancellationToken);

        return new AnalyticsSummaryDto
        {
            EmployeesTotal = employeesTotal,
            EmployeesTested = testedIds.Count,
            CompletionsTotal = completionsTotal,
            AttentionRequiredEmployees = attentionEmployees,
            StaffTotal = staffTotal
        };
    }

    public async Task<IReadOnlyList<DepartmentCoverageDto>> GetDepartmentAnalyticsAsync(
        AnalyticsFilter filter,
        string sortBy = "coverage",
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var departments = await db.Departments.AsNoTracking()
            .OrderBy(d => d.Number)
            .Select(d => new { d.Id, d.Name, d.Number, d.StaffCount })
            .ToListAsync(cancellationToken);

        if (filter.DepartmentId.HasValue)
        {
            departments = departments.Where(d => d.Id == filter.DepartmentId.Value).ToList();
        }

        var employees = await db.Employees.AsNoTracking()
            .Select(e => new { e.Id, e.DepartmentId })
            .ToListAsync(cancellationToken);

        var completed = await ApplyAttemptFilter(
                db.TestAttempts.AsNoTracking().Where(a => a.Status == TestAttemptStatus.Completed),
                filter)
            .Where(a => a.EmployeeId != null)
            .Select(a => new { EmployeeId = a.EmployeeId!.Value, a.Id })
            .ToListAsync(cancellationToken);

        var attention = await GetAttentionRequiredAsync(filter, cancellationToken);
        var attentionByDept = attention
            .Where(a => a.DepartmentId.HasValue && a.EmployeeId.HasValue)
            .GroupBy(a => a.DepartmentId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.EmployeeId!.Value).Distinct().Count());

        var result = new List<DepartmentCoverageDto>(departments.Count);
        foreach (var dept in departments)
        {
            var deptEmployees = employees.Where(e => e.DepartmentId == dept.Id).Select(e => e.Id).ToHashSet();
            var tested = completed
                .Where(c => deptEmployees.Contains(c.EmployeeId))
                .Select(c => c.EmployeeId)
                .Distinct()
                .Count();
            var completions = completed.Count(c => deptEmployees.Contains(c.EmployeeId));

            attentionByDept.TryGetValue(dept.Id, out var attentionCount);

            result.Add(new DepartmentCoverageDto
            {
                DepartmentId = dept.Id,
                DepartmentName = dept.Name,
                DepartmentNumber = dept.Number,
                EmployeesTotal = deptEmployees.Count,
                EmployeesTested = tested,
                CompletionsTotal = completions,
                AttentionRequiredEmployees = attentionCount,
                StaffCount = dept.StaffCount
            });
        }

        return sortBy switch
        {
            "employees" => result.OrderByDescending(x => x.EmployeesTotal).ThenBy(x => x.DepartmentNumber).ToList(),
            "staff" => result.OrderByDescending(x => x.StaffCount).ThenBy(x => x.DepartmentNumber).ToList(),
            "rate" => result.OrderByDescending(x => x.CompletionRatePercent ?? -1).ThenBy(x => x.DepartmentNumber).ToList(),
            "completions" => result.OrderByDescending(x => x.CompletionsTotal).ThenBy(x => x.DepartmentNumber).ToList(),
            "name" => result.OrderBy(x => x.DepartmentName).ToList(),
            "number" => result.OrderBy(x => x.DepartmentNumber).ToList(),
            _ => result.OrderByDescending(x => x.CoveragePercent).ThenBy(x => x.DepartmentNumber).ToList()
        };
    }

    public async Task<IReadOnlyList<TestCompletionsDto>> GetTestCompletionsAsync(
        AnalyticsFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var completed = ApplyAttemptFilter(
            db.TestAttempts.AsNoTracking().Where(a => a.Status == TestAttemptStatus.Completed),
            filter);

        var rows = await completed
            .Where(a => a.TestDocumentId != null)
            .GroupBy(a => a.TestDocumentId!.Value)
            .Select(g => new
            {
                TestDocumentId = g.Key,
                Completions = g.Count(),
                UniqueEmployees = g.Where(x => x.EmployeeId != null).Select(x => x.EmployeeId!.Value).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        var titles = await db.TestDocuments.AsNoTracking()
            .Where(d => rows.Select(r => r.TestDocumentId).Contains(d.Id))
            .Select(d => new { d.Id, d.Title, d.OriginalFileName })
            .ToListAsync(cancellationToken);

        var titleMap = titles.ToDictionary(
            t => t.Id,
            t => string.IsNullOrWhiteSpace(t.Title) ? t.OriginalFileName : t.Title);

        return rows
            .Select(r => new TestCompletionsDto
            {
                TestDocumentId = r.TestDocumentId,
                TestTitle = titleMap.GetValueOrDefault(r.TestDocumentId, "Тест"),
                Completions = r.Completions,
                UniqueEmployees = r.UniqueEmployees
            })
            .OrderByDescending(r => r.Completions)
            .ThenBy(r => r.TestTitle)
            .ToList();
    }

    public async Task<IReadOnlyList<AttentionItemDto>> GetAttentionRequiredAsync(
        AnalyticsFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var attempts = await ApplyAttemptFilter(
                db.TestAttempts.AsNoTracking().Where(a => a.Status == TestAttemptStatus.Completed),
                filter)
            .Include(a => a.Employee)
                .ThenInclude(e => e!.Department)
            .Include(a => a.TestDocument)
            .Include(a => a.Answers)
                .ThenInclude(ans => ans.SelectedOption)
            .OrderByDescending(a => a.CompletedAt ?? a.StartedAt)
            .ToListAsync(cancellationToken);

        var documentIds = attempts
            .Where(a => a.TestDocumentId.HasValue)
            .Select(a => a.TestDocumentId!.Value)
            .Distinct()
            .ToList();

        var questions = await db.TestQuestions.AsNoTracking()
            .Include(q => q.Options)
            .Where(q => documentIds.Contains(q.TestDocumentId))
            .OrderBy(q => q.SortOrder)
            .ToListAsync(cancellationToken);

        var questionsByDoc = questions
            .GroupBy(q => q.TestDocumentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TestQuestion>)g.ToList());

        var items = new List<AttentionItemDto>();
        foreach (var attempt in attempts)
        {
            if (!attempt.TestDocumentId.HasValue)
            {
                continue;
            }

            questionsByDoc.TryGetValue(attempt.TestDocumentId.Value, out var docQuestions);
            docQuestions ??= Array.Empty<TestQuestion>();

            var answers = attempt.Answers.ToDictionary(a => a.TestQuestionId);
            var evaluation = AttemptAttentionEvaluator.Evaluate(attempt.TestDocument, docQuestions, answers);
            if (!evaluation.NeedsAttention)
            {
                continue;
            }

            items.Add(new AttentionItemDto
            {
                AttemptId = attempt.Id,
                EmployeeId = attempt.EmployeeId,
                EmployeeName = attempt.Employee?.DisplayName
                    ?? $"{attempt.LastName} {attempt.FirstName} {attempt.MiddleName}".Trim(),
                DepartmentId = attempt.Employee?.DepartmentId,
                DepartmentName = attempt.Employee?.Department?.Name
                    ?? $"Підрозділ {attempt.NumberUnit}",
                TestDocumentId = attempt.TestDocumentId,
                TestTitle = attempt.TestDocument?.Title
                    ?? attempt.TestDocument?.OriginalFileName
                    ?? "Тест",
                CompletedAt = attempt.CompletedAt,
                Reason = evaluation.Reason
            });
        }

        // One row per employee+test: latest attention-worthy attempt wins.
        return items
            .GroupBy(i => (i.EmployeeId, i.TestDocumentId))
            .Select(g => g.OrderByDescending(x => x.CompletedAt).First())
            .OrderByDescending(i => i.CompletedAt)
            .ToList();
    }

    public async Task<EmployeeTestMatrixResultDto> GetEmployeeTestMatrixAsync(
        Guid departmentId,
        string coverageFilter = "all",
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var tests = await db.TestDocuments.AsNoTracking()
            .Where(d => d.IsActive || d.IsRequired || d.Questions.Any())
            .OrderByDescending(d => d.IsRequired)
            .ThenByDescending(d => d.IsActive)
            .ThenBy(d => d.Title)
            .Select(d => new { d.Id, Title = string.IsNullOrWhiteSpace(d.Title) ? d.OriginalFileName : d.Title })
            .ToListAsync(cancellationToken);

        var usedTestIds = await db.TestAttempts.AsNoTracking()
            .Where(a => a.Status == TestAttemptStatus.Completed &&
                        a.Employee != null &&
                        a.Employee.DepartmentId == departmentId &&
                        a.TestDocumentId != null)
            .Select(a => a.TestDocumentId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (usedTestIds.Count > 0)
        {
            var usedSet = usedTestIds.ToHashSet();
            tests = tests
                .OrderByDescending(t => usedSet.Contains(t.Id))
                .ThenBy(t => t.Title)
                .ToList();
        }

        var employeesQuery = db.Employees.AsNoTracking()
            .Where(e => e.DepartmentId == departmentId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            employeesQuery = employeesQuery.Where(e =>
                e.LastName.Contains(term) ||
                e.FirstName.Contains(term) ||
                e.MiddleName.Contains(term));
        }

        var employees = await employeesQuery
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(cancellationToken);

        var employeeIds = employees.Select(e => e.Id).ToList();

        var completions = await db.TestAttempts.AsNoTracking()
            .Where(a => a.Status == TestAttemptStatus.Completed &&
                        a.EmployeeId != null &&
                        a.TestDocumentId != null &&
                        employeeIds.Contains(a.EmployeeId.Value))
            .Select(a => new
            {
                EmployeeId = a.EmployeeId!.Value,
                TestDocumentId = a.TestDocumentId!.Value,
                a.CompletedAt
            })
            .ToListAsync(cancellationToken);

        var rows = new List<EmployeeTestMatrixDto>();
        foreach (var employee in employees)
        {
            var empCompletions = completions.Where(c => c.EmployeeId == employee.Id).ToList();
            var hasAny = empCompletions.Count > 0;

            if (coverageFilter == "tested" && !hasAny)
            {
                continue;
            }

            if (coverageFilter == "untested" && hasAny)
            {
                continue;
            }

            var cells = tests.Select(t =>
            {
                var cellItems = empCompletions.Where(c => c.TestDocumentId == t.Id).ToList();
                return new EmployeeTestMatrixCellDto
                {
                    TestDocumentId = t.Id,
                    Completions = cellItems.Count,
                    LastCompletedAt = cellItems.Count == 0 ? null : cellItems.Max(c => c.CompletedAt)
                };
            }).ToList();

            rows.Add(new EmployeeTestMatrixDto
            {
                EmployeeId = employee.Id,
                EmployeeName = employee.DisplayName,
                LastCompletedAt = empCompletions.Count == 0 ? null : empCompletions.Max(c => c.CompletedAt),
                HasAnyCompletion = hasAny,
                Cells = cells
            });
        }

        return new EmployeeTestMatrixResultDto
        {
            Columns = tests.Select(t => new EmployeeTestMatrixColumnDto
            {
                TestDocumentId = t.Id,
                TestTitle = t.Title
            }).ToList(),
            Rows = rows
        };
    }

    public async Task<IReadOnlyList<UntestedEmployeeDto>> GetUntestedEmployeesAsync(
        AnalyticsFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var employees = await ApplyEmployeeFilter(db.Employees.AsNoTracking().Include(e => e.Department), filter)
            .OrderBy(e => e.Department!.Number)
            .ThenBy(e => e.LastName)
            .ToListAsync(cancellationToken);

        var testedIds = await ApplyAttemptFilter(
                db.TestAttempts.AsNoTracking().Where(a => a.Status == TestAttemptStatus.Completed),
                filter)
            .Where(a => a.EmployeeId != null)
            .Select(a => a.EmployeeId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var testedSet = testedIds.ToHashSet();

        return employees
            .Where(e => !testedSet.Contains(e.Id))
            .Select(e => new UntestedEmployeeDto
            {
                EmployeeId = e.Id,
                EmployeeName = e.DisplayName,
                DepartmentId = e.DepartmentId,
                DepartmentName = e.Department?.Name ?? "—"
            })
            .ToList();
    }

    public async Task<TestAnalyticsDto?> GetTestAnalyticsAsync(
        Guid testDocumentId,
        AnalyticsFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= new AnalyticsFilter();
        filter.TestDocumentId = testDocumentId;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var document = await db.TestDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == testDocumentId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var attempts = await ApplyAttemptFilter(
                db.TestAttempts.AsNoTracking().Where(a => a.Status == TestAttemptStatus.Completed),
                filter)
            .Include(a => a.Answers)
                .ThenInclude(ans => ans.SelectedOption)
            .OrderBy(a => a.CompletedAt ?? a.StartedAt)
            .ToListAsync(cancellationToken);

        var questions = await db.TestQuestions.AsNoTracking()
            .Include(q => q.Options)
            .Where(q => q.TestDocumentId == testDocumentId)
            .OrderBy(q => q.SortOrder)
            .ToListAsync(cancellationToken);

        var uniqueEmployees = attempts
            .Where(a => a.EmployeeId.HasValue)
            .Select(a => a.EmployeeId!.Value)
            .Distinct()
            .Count();

        var repeats = Math.Max(0, attempts.Count - uniqueEmployees);

        var unreliable = 0;
        var attention = 0;
        var levelCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var scaleBuckets = new Dictionary<string, List<(double Raw, double? Sten)>>(StringComparer.Ordinal);

        foreach (var attempt in attempts)
        {
            var answers = attempt.Answers.ToDictionary(a => a.TestQuestionId);
            var evaluation = AttemptAttentionEvaluator.Evaluate(document, questions, answers);

            if (evaluation.IsUnreliable)
            {
                unreliable++;
            }

            if (evaluation.NeedsAttention)
            {
                attention++;
            }

            if (!string.IsNullOrWhiteSpace(evaluation.LevelName) && !evaluation.IsUnreliable)
            {
                levelCounts[evaluation.LevelName] = levelCounts.GetValueOrDefault(evaluation.LevelName) + 1;
            }

            if (evaluation.IsUnreliable)
            {
                continue;
            }

            foreach (var (code, raw) in evaluation.ScaleRaw)
            {
                if (!scaleBuckets.TryGetValue(code, out var list))
                {
                    list = [];
                    scaleBuckets[code] = list;
                }

                evaluation.ScaleSten.TryGetValue(code, out var sten);
                list.Add((raw, evaluation.ScaleSten.ContainsKey(code) ? sten : null));
            }
        }

        var scaleSummaries = scaleBuckets
            .Where(kv => kv.Key is not "Д" and not "Оцінка" and not "L" and not "Sr")
            .Select(kv => new AnalyticsScaleSummaryDto
            {
                ScaleCode = kv.Key,
                ScaleName = kv.Key,
                AverageRaw = Math.Round(kv.Value.Average(v => v.Raw), 1),
                AverageSten = kv.Value.Any(v => v.Sten.HasValue)
                    ? Math.Round(kv.Value.Where(v => v.Sten.HasValue).Average(v => v.Sten!.Value), 1)
                    : null,
                SampleCount = kv.Value.Count
            })
            .ToList();

        return new TestAnalyticsDto
        {
            TestDocumentId = testDocumentId,
            TestTitle = string.IsNullOrWhiteSpace(document.Title) ? document.OriginalFileName : document.Title,
            Completions = attempts.Count,
            UniqueEmployees = uniqueEmployees,
            RepeatCompletions = repeats,
            UnreliableCount = unreliable,
            AttentionCount = attention,
            LevelDistribution = levelCounts
                .Select(kv => new AnalyticsLevelCountDto { LevelName = kv.Key, Count = kv.Value })
                .OrderByDescending(x => x.Count)
                .ToList(),
            ScaleSummaries = scaleSummaries
        };
    }

    public async Task<EmployeeDynamicsDto?> GetEmployeeDynamicsAsync(
        Guid employeeId,
        Guid testDocumentId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var employee = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        var document = await db.TestDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == testDocumentId, cancellationToken);
        if (employee is null || document is null)
        {
            return null;
        }

        var attempts = await db.TestAttempts.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId &&
                        a.TestDocumentId == testDocumentId &&
                        a.Status == TestAttemptStatus.Completed)
            .Include(a => a.Answers)
                .ThenInclude(ans => ans.SelectedOption)
            .OrderBy(a => a.CompletedAt ?? a.StartedAt)
            .ToListAsync(cancellationToken);

        var questions = await db.TestQuestions.AsNoTracking()
            .Include(q => q.Options)
            .Where(q => q.TestDocumentId == testDocumentId)
            .OrderBy(q => q.SortOrder)
            .ToListAsync(cancellationToken);

        var points = new List<EmployeeDynamicsPointDto>();
        var scaleCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attempt in attempts)
        {
            var answers = attempt.Answers.ToDictionary(a => a.TestQuestionId);
            var evaluation = AttemptAttentionEvaluator.Evaluate(document, questions, answers);
            if (evaluation.IsUnreliable)
            {
                continue;
            }

            var values = new Dictionary<string, double>();
            foreach (var (code, value) in evaluation.ScaleSten.Count > 0 ? evaluation.ScaleSten : evaluation.ScaleRaw)
            {
                if (code is "Д" or "L" or "Sr" or "Оцінка" or "Сумарно")
                {
                    continue;
                }

                values[code] = value;
                scaleCodes.Add(code);
            }

            points.Add(new EmployeeDynamicsPointDto
            {
                AttemptId = attempt.Id,
                CompletedAt = attempt.CompletedAt ?? attempt.StartedAt,
                ScaleValues = values
            });
        }

        return new EmployeeDynamicsDto
        {
            EmployeeId = employeeId,
            EmployeeName = employee.DisplayName,
            TestDocumentId = testDocumentId,
            TestTitle = string.IsNullOrWhiteSpace(document.Title) ? document.OriginalFileName : document.Title,
            ScaleCodes = scaleCodes.OrderBy(x => x).ToList(),
            Points = points
        };
    }

    public async Task<DepartmentCoverageDto?> GetDepartmentSummaryAsync(
        Guid departmentId,
        CancellationToken cancellationToken = default)
    {
        var list = await GetDepartmentAnalyticsAsync(
            new AnalyticsFilter { DepartmentId = departmentId },
            "number",
            cancellationToken);
        return list.FirstOrDefault();
    }

    private static IQueryable<Employee> ApplyEmployeeFilter(
        IQueryable<Employee> query,
        AnalyticsFilter filter)
    {
        if (filter.DepartmentId.HasValue)
        {
            query = query.Where(e => e.DepartmentId == filter.DepartmentId.Value);
        }

        if (filter.EmployeeId.HasValue)
        {
            query = query.Where(e => e.Id == filter.EmployeeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.EmployeeSearch))
        {
            var term = filter.EmployeeSearch.Trim();
            query = query.Where(e =>
                e.LastName.Contains(term) ||
                e.FirstName.Contains(term) ||
                e.MiddleName.Contains(term));
        }

        return query;
    }

    private static IQueryable<TestAttempt> ApplyAttemptFilter(
        IQueryable<TestAttempt> query,
        AnalyticsFilter filter)
    {
        query = query.Where(a => !a.IsAnonymous);

        if (filter.DepartmentId.HasValue)
        {
            query = query.Where(a => a.Employee != null && a.Employee.DepartmentId == filter.DepartmentId.Value);
        }

        if (filter.EmployeeId.HasValue)
        {
            query = query.Where(a => a.EmployeeId == filter.EmployeeId.Value);
        }

        if (filter.TestDocumentId.HasValue)
        {
            query = query.Where(a => a.TestDocumentId == filter.TestDocumentId.Value);
        }

        if (filter.DateFrom.HasValue)
        {
            var from = filter.DateFrom.Value.Date;
            query = query.Where(a => (a.CompletedAt ?? a.StartedAt) >= from);
        }

        if (filter.DateTo.HasValue)
        {
            var toExclusive = filter.DateTo.Value.Date.AddDays(1);
            query = query.Where(a => (a.CompletedAt ?? a.StartedAt) < toExclusive);
        }

        if (!string.IsNullOrWhiteSpace(filter.EmployeeSearch))
        {
            var term = filter.EmployeeSearch.Trim();
            query = query.Where(a =>
                a.LastName.Contains(term) ||
                a.FirstName.Contains(term) ||
                a.MiddleName.Contains(term) ||
                (a.Employee != null && (
                    a.Employee.LastName.Contains(term) ||
                    a.Employee.FirstName.Contains(term) ||
                    a.Employee.MiddleName.Contains(term))));
        }

        return query;
    }
}
