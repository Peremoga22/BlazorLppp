using BlazorLppp.Application.Models;

namespace BlazorLppp.Application.Services;

public interface IAnalyticsService
{
    Task<OrgAnalyticsOverviewDto> GetOrgOverviewAsync(
        CancellationToken cancellationToken = default);

    Task<AnalyticsSummaryDto> GetSummaryAsync(
        AnalyticsFilter filter,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DepartmentCoverageDto>> GetDepartmentAnalyticsAsync(
        AnalyticsFilter filter,
        string sortBy = "coverage",
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TestCompletionsDto>> GetTestCompletionsAsync(
        AnalyticsFilter filter,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttentionItemDto>> GetAttentionRequiredAsync(
        AnalyticsFilter filter,
        CancellationToken cancellationToken = default);

    Task<EmployeeTestMatrixResultDto> GetEmployeeTestMatrixAsync(
        Guid departmentId,
        string coverageFilter = "all",
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UntestedEmployeeDto>> GetUntestedEmployeesAsync(
        AnalyticsFilter filter,
        CancellationToken cancellationToken = default);

    Task<TestAnalyticsDto?> GetTestAnalyticsAsync(
        Guid testDocumentId,
        AnalyticsFilter? filter = null,
        CancellationToken cancellationToken = default);

    Task<EmployeeDynamicsDto?> GetEmployeeDynamicsAsync(
        Guid employeeId,
        Guid testDocumentId,
        CancellationToken cancellationToken = default);

    Task<DepartmentCoverageDto?> GetDepartmentSummaryAsync(
        Guid departmentId,
        CancellationToken cancellationToken = default);
}
