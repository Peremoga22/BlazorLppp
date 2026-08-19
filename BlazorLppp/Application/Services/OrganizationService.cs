using BlazorLppp.Application.Models;
using BlazorLppp.Data;
using BlazorLppp.Domain;
using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Application.Services;

public class OrganizationService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ITestResultDocumentService resultDocumentService) : IOrganizationService
{
    public async Task EnsureDefaultDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Сідимо 1–5 лише якщо підрозділів ще немає (щоб видалені не з’являлись знову).
        if (await db.Departments.AnyAsync(cancellationToken))
        {
            return;
        }

        foreach (var number in UnitNumbers.All)
        {
            db.Departments.Add(new Department
            {
                Id = Guid.NewGuid(),
                Number = number,
                Name = $"Підрозділ {number}",
                CreatedAt = DateTime.Now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task BackfillEmployeesFromAttemptsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultDepartmentsAsync(cancellationToken);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var departments = await db.Departments.ToDictionaryAsync(d => d.Number, cancellationToken);

        var orphans = await db.TestAttempts
            .Where(a => a.EmployeeId == null)
            .OrderBy(a => a.StartedAt)
            .ToListAsync(cancellationToken);

        if (orphans.Count == 0)
        {
            return;
        }

        var employeeCache = await db.Employees
            .Include(e => e.Department)
            .ToListAsync(cancellationToken);

        foreach (var attempt in orphans)
        {
            if (!departments.TryGetValue(attempt.NumberUnit, out var department))
            {
                continue;
            }

            var lastName = attempt.LastName.Trim();
            var firstName = attempt.FirstName.Trim();
            var middleName = attempt.MiddleName.Trim();

            var employee = employeeCache.FirstOrDefault(e =>
                e.DepartmentId == department.Id &&
                string.Equals(e.LastName, lastName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.FirstName, firstName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.MiddleName, middleName, StringComparison.OrdinalIgnoreCase));

            if (employee is null)
            {
                employee = new Employee
                {
                    Id = Guid.NewGuid(),
                    DepartmentId = department.Id,
                    LastName = lastName,
                    FirstName = firstName,
                    MiddleName = middleName,
                    CreatedAt = attempt.StartedAt
                };
                db.Employees.Add(employee);
                employeeCache.Add(employee);
            }

            attempt.EmployeeId = employee.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DepartmentListItem>> ListDepartmentsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Departments
            .AsNoTracking()
            .OrderBy(d => d.Number)
            .ThenBy(d => d.Name)
            .Select(d => new DepartmentListItem
            {
                Id = d.Id,
                Name = d.Name,
                Number = d.Number,
                EmployeeCount = d.Employees.Count,
                StaffCount = d.StaffCount
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<Department?> GetDepartmentAsync(
        Guid departmentId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == departmentId, cancellationToken);
    }

    public async Task<Department> CreateDepartmentAsync(
        string? name = null,
        int staffCount = 0,
        CancellationToken cancellationToken = default)
    {
        staffCount = NormalizeStaffCount(staffCount);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nextNumber = await db.Departments.AnyAsync(cancellationToken)
            ? await db.Departments.MaxAsync(d => d.Number, cancellationToken) + 1
            : 1;

        var autoName = $"Підрозділ {nextNumber}";
        var trimmed = name?.Trim() ?? string.Empty;

        // Порожня назва або шаблон «Підрозділ N» → завжди наступний номер.
        string finalName;
        if (string.IsNullOrWhiteSpace(trimmed) ||
            IsDefaultDepartmentName(trimmed))
        {
            finalName = autoName;
        }
        else
        {
            var nameTaken = await db.Departments.AnyAsync(
                d => d.Name == trimmed,
                cancellationToken);
            if (nameTaken)
            {
                throw new InvalidOperationException($"Підрозділ «{trimmed}» уже існує.");
            }

            finalName = trimmed;
        }

        var department = new Department
        {
            Id = Guid.NewGuid(),
            Name = finalName,
            Number = nextNumber,
            StaffCount = staffCount,
            CreatedAt = DateTime.Now
        };

        db.Departments.Add(department);
        await db.SaveChangesAsync(cancellationToken);
        return department;
    }

    private static bool IsDefaultDepartmentName(string name)
    {
        // «Підрозділ 1», «підрозділ 12» тощо — трактуємо як автоінкремент.
        const string prefix = "Підрозділ ";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = name[prefix.Length..].Trim();
        return int.TryParse(suffix, out _);
    }

    public async Task UpdateDepartmentAsync(
        Guid departmentId,
        string name,
        int staffCount,
        CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        if (trimmed.Length < 2)
        {
            throw new InvalidOperationException("Вкажіть назву підрозділу.");
        }

        staffCount = NormalizeStaffCount(staffCount);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var department = await db.Departments
            .FirstOrDefaultAsync(d => d.Id == departmentId, cancellationToken)
            ?? throw new InvalidOperationException("Підрозділ не знайдено.");

        department.Name = trimmed;
        department.StaffCount = staffCount;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static int NormalizeStaffCount(int staffCount)
    {
        if (staffCount < 0)
        {
            throw new InvalidOperationException("Чисельність підрозділу не може бути від’ємною.");
        }

        if (staffCount > 100_000)
        {
            throw new InvalidOperationException("Чисельність підрозділу занадто велика.");
        }

        return staffCount;
    }

    public async Task<IReadOnlyList<EmployeeListItem>> ListEmployeesAsync(
        Guid departmentId,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Employees
            .AsNoTracking()
            .Include(e => e.Department)
            .Include(e => e.Attempts)
            .Where(e => e.DepartmentId == departmentId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e =>
                e.LastName.Contains(term) ||
                e.FirstName.Contains(term) ||
                e.MiddleName.Contains(term));
        }

        var employees = await query
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ThenBy(e => e.MiddleName)
            .ToListAsync(cancellationToken);

        return employees.Select(e =>
        {
            var completed = e.Attempts
                .Where(a => a.Status == TestAttemptStatus.Completed)
                .ToList();
            return new EmployeeListItem
            {
                Id = e.Id,
                DepartmentId = e.DepartmentId,
                DepartmentName = e.Department?.Name ?? string.Empty,
                LastName = e.LastName,
                FirstName = e.FirstName,
                MiddleName = e.MiddleName,
                DisplayName = e.DisplayName,
                CompletedTestsCount = completed.Count,
                LastCompletedAt = completed.Count == 0
                    ? null
                    : completed.Max(a => a.CompletedAt ?? a.StartedAt)
            };
        }).ToList();
    }

    public async Task<Employee?> GetEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Employees
            .AsNoTracking()
            .Include(e => e.Department)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
    }

    public async Task<EmployeeCardModel?> GetEmployeeCardAsync(
        Guid employeeId,
        Guid? selectedTestDocumentId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var employee = await db.Employees
            .AsNoTracking()
            .Include(e => e.Department)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        if (employee?.Department is null)
        {
            return null;
        }

        var attempts = await db.TestAttempts
            .AsNoTracking()
            .Include(a => a.TestDocument)
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.StartedAt)
            .ToListAsync(cancellationToken);

        var tabs = attempts
            .Where(a => a.TestDocumentId.HasValue)
            .GroupBy(a => a.TestDocumentId!.Value)
            .Select(g =>
            {
                var completed = g.Where(a => a.Status == TestAttemptStatus.Completed).ToList();
                return new EmployeeTestTab
                {
                    TestDocumentId = g.Key,
                    TestTitle = g.First().TestDocument?.Title ?? "Тест",
                    SessionCount = g.Count(),
                    LastCompletedAt = completed.Count == 0
                        ? null
                        : completed.Max(a => a.CompletedAt ?? a.StartedAt)
                };
            })
            .OrderBy(t => t.TestTitle)
            .ToList();

        var activeTestId = selectedTestDocumentId
            ?? tabs.FirstOrDefault()?.TestDocumentId;

        var sessions = attempts
            .Where(a => !activeTestId.HasValue || a.TestDocumentId == activeTestId)
            .Select(a =>
            {
                var hasFile = !string.IsNullOrWhiteSpace(a.ResultRelativePath) &&
                              File.Exists(resultDocumentService.GetAbsolutePath(a.ResultRelativePath));
                return new EmployeeTestSessionItem
                {
                    AttemptId = a.Id,
                    TestDocumentId = a.TestDocumentId ?? Guid.Empty,
                    TestTitle = a.TestDocument?.Title ?? "Тест",
                    StartedAt = a.StartedAt,
                    CompletedAt = a.CompletedAt,
                    StatusName = a.Status == TestAttemptStatus.Completed ? "Завершено" : "В процесі",
                    IsCompleted = a.Status == TestAttemptStatus.Completed,
                    HasResultFile = hasFile,
                    ResultFileName = a.ResultFileName
                };
            })
            .ToList();

        return new EmployeeCardModel
        {
            Employee = employee,
            Department = employee.Department,
            TestTabs = tabs,
            Sessions = sessions
        };
    }

    public async Task<Employee> AddEmployeeAsync(
        Guid departmentId,
        string lastName,
        string firstName,
        string middleName,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var department = await db.Departments
            .FirstOrDefaultAsync(d => d.Id == departmentId, cancellationToken)
            ?? throw new InvalidOperationException("Підрозділ не знайдено.");

        return await FindOrCreateCoreAsync(
            db,
            department,
            lastName.Trim(),
            firstName.Trim(),
            middleName.Trim(),
            cancellationToken);
    }

    public async Task<Employee> FindOrCreateEmployeeAsync(
        int departmentNumber,
        string lastName,
        string firstName,
        string middleName,
        CancellationToken cancellationToken = default)
    {
        await EnsureDefaultDepartmentsAsync(cancellationToken);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var department = await db.Departments
            .FirstOrDefaultAsync(d => d.Number == departmentNumber, cancellationToken)
            ?? throw new InvalidOperationException("Підрозділ не знайдено.");

        return await FindOrCreateCoreAsync(
            db,
            department,
            lastName.Trim(),
            firstName.Trim(),
            middleName.Trim(),
            cancellationToken);
    }

    private static async Task<Employee> FindOrCreateCoreAsync(
        ApplicationDbContext db,
        Department department,
        string lastName,
        string firstName,
        string middleName,
        CancellationToken cancellationToken)
    {
        if (lastName.Length < 2 || firstName.Length < 2 || middleName.Length < 2)
        {
            throw new InvalidOperationException("Вкажіть повне П.І.Б. працівника.");
        }

        var existing = await db.Employees.FirstOrDefaultAsync(
            e => e.DepartmentId == department.Id &&
                 e.LastName == lastName &&
                 e.FirstName == firstName &&
                 e.MiddleName == middleName,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            DepartmentId = department.Id,
            LastName = lastName,
            FirstName = firstName,
            MiddleName = middleName,
            CreatedAt = DateTime.Now
        };

        db.Employees.Add(employee);
        await db.SaveChangesAsync(cancellationToken);
        return employee;
    }

    public async Task DeleteEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var employee = await db.Employees
            .Include(e => e.Department)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
            ?? throw new InvalidOperationException("Працівника не знайдено.");

        if (employee.Department is null)
        {
            throw new InvalidOperationException("Підрозділ працівника не знайдено.");
        }

        var departmentNumber = employee.Department.Number;
        var attempts = await db.TestAttempts
            .Where(a => a.EmployeeId == employeeId ||
                        (a.NumberUnit == departmentNumber &&
                         a.LastName == employee.LastName &&
                         a.FirstName == employee.FirstName &&
                         a.MiddleName == employee.MiddleName))
            .ToListAsync(cancellationToken);

        var resultPaths = attempts
            .Select(a => a.ResultRelativePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (attempts.Count > 0)
        {
            db.TestAttempts.RemoveRange(attempts);
        }

        db.Employees.Remove(employee);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var path in resultPaths)
        {
            await resultDocumentService.DeleteAsync(path!, cancellationToken);
        }
    }

    public async Task DeleteDepartmentAsync(
        Guid departmentId,
        CancellationToken cancellationToken = default)
    {
        List<Guid> employeeIds;
        int departmentNumber;

        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var department = await db.Departments
                .AsNoTracking()
                .Include(d => d.Employees)
                .FirstOrDefaultAsync(d => d.Id == departmentId, cancellationToken)
                ?? throw new InvalidOperationException("Підрозділ не знайдено.");

            employeeIds = department.Employees.Select(e => e.Id).ToList();
            departmentNumber = department.Number;
        }

        foreach (var employeeId in employeeIds)
        {
            await DeleteEmployeeAsync(employeeId, cancellationToken);
        }

        await using var deleteDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var toDelete = await deleteDb.Departments
            .FirstOrDefaultAsync(d => d.Id == departmentId, cancellationToken)
            ?? throw new InvalidOperationException("Підрозділ не знайдено.");

        var orphanAttempts = await deleteDb.TestAttempts
            .Where(a => a.NumberUnit == departmentNumber && a.EmployeeId == null)
            .ToListAsync(cancellationToken);

        var resultPaths = orphanAttempts
            .Select(a => a.ResultRelativePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (orphanAttempts.Count > 0)
        {
            deleteDb.TestAttempts.RemoveRange(orphanAttempts);
        }

        deleteDb.Departments.Remove(toDelete);
        await deleteDb.SaveChangesAsync(cancellationToken);

        foreach (var path in resultPaths)
        {
            await resultDocumentService.DeleteAsync(path!, cancellationToken);
        }
    }
}
