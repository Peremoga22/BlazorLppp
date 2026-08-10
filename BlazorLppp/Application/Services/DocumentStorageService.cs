using System.Text;
using System.Text.RegularExpressions;

using BlazorLppp.Application.Models;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;

namespace BlazorLppp.Application.Services;

public partial class DocumentStorageService(
    IWebHostEnvironment environment,
    IOptions<DocumentStorageOptions> options) : IDocumentStorageService
{
    private readonly DocumentStorageOptions _options = options.Value;

    public async Task<DocumentUploadResult> UploadAsync(
        IBrowserFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (string.IsNullOrWhiteSpace(file.Name))
        {
            throw new InvalidOperationException("Файл не має імені.");
        }

        if (file.Size <= 0)
        {
            throw new InvalidOperationException("Файл порожній.");
        }

        if (file.Size > _options.MaxFileSizeBytes)
        {
            var maxMb = _options.MaxFileSizeBytes / (1024d * 1024d);
            throw new InvalidOperationException(
                $"Файл завеликий. Максимальний розмір — {maxMb:0.#} МБ.");
        }

        var originalName = Path.GetFileName(file.Name.Trim());
        var extension = Path.GetExtension(originalName);
        if (string.IsNullOrWhiteSpace(extension) ||
            !_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Тип файлу «{extension}» не підтримується.");
        }

        var folderName = BuildFolderName(originalName);
        var rootPath = ResolveRootPath();
        var folderPath = Path.Combine(rootPath, folderName);

        Directory.CreateDirectory(folderPath);

        var safeFileName = SanitizeFileName(originalName);
        var destinationPath = Path.Combine(folderPath, safeFileName);

        await using (var readStream = file.OpenReadStream(_options.MaxFileSizeBytes, cancellationToken))
        await using (var writeStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true))
        {
            await readStream.CopyToAsync(writeStream, cancellationToken);
        }

        var relativePath = Path.Combine(folderName, safeFileName)
            .Replace('\\', '/');

        return new DocumentUploadResult
        {
            FolderName = folderName,
            FileName = safeFileName,
            RelativePath = relativePath,
            SizeBytes = file.Size,
            AbsolutePath = destinationPath
        };
    }

    public string GetAbsolutePath(string relativePath)
    {
        var rootPath = ResolveRootPath();
        var combined = Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Некоректний шлях до документа.");
        }

        return combined;
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Task.CompletedTask;
        }

        var absolutePath = GetAbsolutePath(relativePath);
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        var folderPath = Path.GetDirectoryName(absolutePath);
        var rootPath = ResolveRootPath();
        if (!string.IsNullOrWhiteSpace(folderPath) &&
            !string.Equals(folderPath, rootPath, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(folderPath) &&
            !Directory.EnumerateFileSystemEntries(folderPath).Any())
        {
            Directory.Delete(folderPath);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredDocumentInfo>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rootPath = ResolveRootPath();
        if (!Directory.Exists(rootPath))
        {
            return Task.FromResult<IReadOnlyList<StoredDocumentInfo>>([]);
        }

        var items = new List<StoredDocumentInfo>();

        foreach (var folderPath in Directory.EnumerateDirectories(rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folderName = Path.GetFileName(folderPath);
            foreach (var filePath in Directory.EnumerateFiles(folderPath))
            {
                var info = new FileInfo(filePath);
                items.Add(new StoredDocumentInfo
                {
                    FolderName = folderName,
                    FileName = info.Name,
                    RelativePath = Path.Combine(folderName, info.Name).Replace('\\', '/'),
                    SizeBytes = info.Length,
                    SavedAt = info.LastWriteTime
                });
            }
        }

        return Task.FromResult<IReadOnlyList<StoredDocumentInfo>>(
            items
                .OrderByDescending(i => i.SavedAt)
                .ToList());
    }

    private string ResolveRootPath()
    {
        var configured = _options.RootPath;
        if (Path.IsPathRooted(configured))
        {
            return configured;
        }

        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, configured));
    }

    private static string BuildFolderName(string fileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var folderName = SanitizeSegment(nameWithoutExtension);

        if (string.IsNullOrWhiteSpace(folderName))
        {
            folderName = $"document-{DateTime.Now:yyyyMMdd-HHmmss}";
        }

        return folderName;
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var safeName = SanitizeSegment(name);

        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "document";
        }

        return safeName + extension.ToLowerInvariant();
    }

    private static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (InvalidPathChars().IsMatch(ch.ToString()) ||
                Path.GetInvalidFileNameChars().Contains(ch))
            {
                builder.Append('-');
                continue;
            }

            builder.Append(ch);
        }

        var cleaned = MultiDash().Replace(builder.ToString(), "-").Trim('-', '.', ' ');
        return cleaned;
    }

    [GeneratedRegex(@"[<>:""/\\|?*\x00-\x1F]")]
    private static partial Regex InvalidPathChars();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDash();
}
