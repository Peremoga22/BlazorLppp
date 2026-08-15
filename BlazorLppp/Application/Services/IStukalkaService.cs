using BlazorLppp.Application.Models;

namespace BlazorLppp.Application.Services;

public interface IStukalkaService
{
    Task<IReadOnlyList<StukalkaReport>> ListAsync(CancellationToken cancellationToken = default);

    Task<StukalkaReport> AddAsync(
        string? author,
        string subject,
        string message,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
