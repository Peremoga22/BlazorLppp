using BlazorLppp.Application.Models;
using BlazorLppp.Domain.Entities;

namespace BlazorLppp.Application.Services;

public interface IOrganizationService
{
    Task<IReadOnlyList<DepartmentListItem>> ListDepartmentsAsync(CancellationToken cancellationToken = default);

    Task<Department?> GetDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default);

    Task<Department> CreateDepartmentAsync(string name, CancellationToken cancellationToken = default);

    Task RenameDepartmentAsync(Guid departmentId, string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeListItem>> ListEmployeesAsync(
        Guid departmentId,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<Employee?> GetEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<EmployeeCardModel?> GetEmployeeCardAsync(
        Guid employeeId,
        Guid? selectedTestDocumentId = null,
        CancellationToken cancellationToken = default);

    Task<Employee> AddEmployeeAsync(
        Guid departmentId,
        string lastName,
        string firstName,
        string middleName,
        CancellationToken cancellationToken = default);

    Task<Employee> FindOrCreateEmployeeAsync(
        int departmentNumber,
        string lastName,
        string firstName,
        string middleName,
        CancellationToken cancellationToken = default);

    Task EnsureDefaultDepartmentsAsync(CancellationToken cancellationToken = default);

    Task BackfillEmployeesFromAttemptsAsync(CancellationToken cancellationToken = default);

    Task DeleteEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
