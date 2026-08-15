using System.Text.Json;

using BlazorLppp.Application.Models;

namespace BlazorLppp.Application.Services;

public class StukalkaService(IWebHostEnvironment environment) : IStukalkaService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath = Path.Combine(
        environment.ContentRootPath,
        "App_Data",
        "Stukalka",
        "reports.json");

    public async Task<IReadOnlyList<StukalkaReport>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var reports = await ReadAsync(cancellationToken);
            return reports
                .OrderByDescending(r => r.CreatedAt)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StukalkaReport> AddAsync(
        string? author,
        string subject,
        string message,
        CancellationToken cancellationToken = default)
    {
        var trimmedSubject = subject?.Trim() ?? string.Empty;
        var trimmedMessage = message?.Trim() ?? string.Empty;
        var trimmedAuthor = string.IsNullOrWhiteSpace(author) ? "Анонімно" : author.Trim();

        if (trimmedSubject.Length == 0)
        {
            throw new InvalidOperationException("Вкажіть тему звернення.");
        }

        if (trimmedMessage.Length == 0)
        {
            throw new InvalidOperationException("Вкажіть текст звернення.");
        }

        if (trimmedAuthor.Length > 100)
        {
            throw new InvalidOperationException("Ім’я автора занадто довге.");
        }

        if (trimmedSubject.Length > 200)
        {
            throw new InvalidOperationException("Тема занадто довга.");
        }

        if (trimmedMessage.Length > 4000)
        {
            throw new InvalidOperationException("Текст звернення занадто довгий.");
        }

        var report = new StukalkaReport
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.Now,
            Author = trimmedAuthor,
            Subject = trimmedSubject,
            Message = trimmedMessage
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var reports = await ReadAsync(cancellationToken);
            reports.Add(report);
            await WriteAsync(reports, cancellationToken);
            return report;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var reports = await ReadAsync(cancellationToken);
            var removed = reports.RemoveAll(r => r.Id == id);
            if (removed == 0)
            {
                throw new InvalidOperationException("Звернення не знайдено.");
            }

            await WriteAsync(reports, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<StukalkaReport>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        await using var stream = new FileStream(
            _filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        var reports = await JsonSerializer.DeserializeAsync<List<StukalkaReport>>(
            stream,
            JsonOptions,
            cancellationToken);

        return reports ?? [];
    }

    private async Task WriteAsync(List<StukalkaReport> reports, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _filePath + ".tmp";
        await using (var stream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, reports, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }
}
