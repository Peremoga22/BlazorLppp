namespace BlazorLppp.Application.Models;

public class AnalyticsFilter
{
    public Guid? DepartmentId { get; set; }

    public Guid? EmployeeId { get; set; }

    public Guid? TestDocumentId { get; set; }

    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    public string? EmployeeSearch { get; set; }

    /// <summary>all | tested | untested</summary>
    public string EmployeeCoverage { get; set; } = "all";
}

public class AnalyticsSummaryDto
{
    public int EmployeesTotal { get; init; }

    public int EmployeesTested { get; init; }

    public int CompletionsTotal { get; init; }

    public int AttentionRequiredEmployees { get; init; }

    public double CoveragePercent => EmployeesTotal <= 0
        ? 0
        : Math.Round(EmployeesTested * 100.0 / EmployeesTotal, 1);
}

public class DepartmentCoverageDto
{
    public Guid DepartmentId { get; init; }

    public string DepartmentName { get; init; } = string.Empty;

    public int DepartmentNumber { get; init; }

    public int EmployeesTotal { get; init; }

    public int EmployeesTested { get; init; }

    public int CompletionsTotal { get; init; }

    public int AttentionRequiredEmployees { get; init; }

    public int UntestedEmployees => Math.Max(0, EmployeesTotal - EmployeesTested);

    public double CoveragePercent => EmployeesTotal <= 0
        ? 0
        : Math.Round(EmployeesTested * 100.0 / EmployeesTotal, 1);
}

public class TestCompletionsDto
{
    public Guid TestDocumentId { get; init; }

    public string TestTitle { get; init; } = string.Empty;

    public int Completions { get; init; }

    public int UniqueEmployees { get; init; }
}

public class AttentionItemDto
{
    public Guid AttemptId { get; init; }

    public Guid? EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public Guid? DepartmentId { get; init; }

    public string DepartmentName { get; init; } = string.Empty;

    public Guid? TestDocumentId { get; init; }

    public string TestTitle { get; init; } = string.Empty;

    public DateTime? CompletedAt { get; init; }

    public string Reason { get; init; } = string.Empty;
}

public class EmployeeTestMatrixDto
{
    public Guid EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public DateTime? LastCompletedAt { get; init; }

    public bool HasAnyCompletion { get; init; }

    public IReadOnlyList<EmployeeTestMatrixCellDto> Cells { get; init; } = [];
}

public class EmployeeTestMatrixCellDto
{
    public Guid TestDocumentId { get; init; }

    public int Completions { get; init; }

    public DateTime? LastCompletedAt { get; init; }
}

public class EmployeeTestMatrixResultDto
{
    public IReadOnlyList<EmployeeTestMatrixColumnDto> Columns { get; init; } = [];

    public IReadOnlyList<EmployeeTestMatrixDto> Rows { get; init; } = [];
}

public class EmployeeTestMatrixColumnDto
{
    public Guid TestDocumentId { get; init; }

    public string TestTitle { get; init; } = string.Empty;
}

public class UntestedEmployeeDto
{
    public Guid EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public Guid DepartmentId { get; init; }

    public string DepartmentName { get; init; } = string.Empty;
}

public class TestAnalyticsDto
{
    public Guid TestDocumentId { get; init; }

    public string TestTitle { get; init; } = string.Empty;

    public int Completions { get; init; }

    public int UniqueEmployees { get; init; }

    public int RepeatCompletions { get; init; }

    public int UnreliableCount { get; init; }

    public int AttentionCount { get; init; }

    public IReadOnlyList<AnalyticsLevelCountDto> LevelDistribution { get; init; } = [];

    public IReadOnlyList<AnalyticsScaleSummaryDto> ScaleSummaries { get; init; } = [];
}

public class AnalyticsLevelCountDto
{
    public string LevelName { get; init; } = string.Empty;

    public int Count { get; init; }
}

public class AnalyticsScaleSummaryDto
{
    public string ScaleCode { get; init; } = string.Empty;

    public string ScaleName { get; init; } = string.Empty;

    public double AverageRaw { get; init; }

    public double? AverageSten { get; init; }

    public int SampleCount { get; init; }
}

public class EmployeeDynamicsDto
{
    public Guid EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public Guid TestDocumentId { get; init; }

    public string TestTitle { get; init; } = string.Empty;

    public IReadOnlyList<string> ScaleCodes { get; init; } = [];

    public IReadOnlyList<EmployeeDynamicsPointDto> Points { get; init; } = [];
}

public class EmployeeDynamicsPointDto
{
    public Guid AttemptId { get; init; }

    public DateTime CompletedAt { get; init; }

    public IReadOnlyDictionary<string, double> ScaleValues { get; init; }
        = new Dictionary<string, double>();
}

public class OrgAnalyticsOverviewDto
{
    public AnalyticsSummaryDto Summary { get; init; } = new();

    public IReadOnlyList<DepartmentCoverageDto> DepartmentCoverage { get; init; } = [];

    public IReadOnlyList<TestCompletionsDto> TestCompletions { get; init; } = [];
}
