using BlazorLppp.Application.Models;
using BlazorLppp.Application.Services;
using BlazorLppp.Data;

using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Data;

public static class Adaptivity200DocumentSeeder
{
    private const string FolderName = "Адаптивність-200";
    private const string StoredFileName = "Адаптивність-200.docx";
    private const string RelativePath = $"{FolderName}/{StoredFileName}";
    private const string DisplayFileName = "Адаптивність-200.docx";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        var definitionService = services.GetRequiredService<ITestDefinitionService>();
        var dbFactory = services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
        var alreadyImported = await dbContext.TestDocuments
            .AsNoTracking()
            .AnyAsync(
                d => d.RelativePath == RelativePath ||
                     d.OriginalFileName == DisplayFileName ||
                     d.Title.Contains("Адаптивність-200"),
                cancellationToken);

        if (alreadyImported)
        {
            return;
        }

        var sourcePath = ResolveSourcePath(environment.ContentRootPath);
        if (sourcePath is null)
        {
            return;
        }

        var documentsRoot = Path.Combine(environment.ContentRootPath, "App_Data", "Documents", FolderName);
        Directory.CreateDirectory(documentsRoot);

        var destinationPath = Path.Combine(documentsRoot, StoredFileName);
        EnsureDocxCopy(sourcePath, destinationPath, environment.ContentRootPath);

        var upload = new DocumentUploadResult
        {
            FolderName = FolderName,
            FileName = DisplayFileName,
            RelativePath = RelativePath,
            AbsolutePath = destinationPath,
            SizeBytes = new FileInfo(destinationPath).Length
        };

        await definitionService.ImportUploadedDocumentAsync(upload, destinationPath, cancellationToken);
    }

    private static void EnsureDocxCopy(string sourcePath, string destinationPath, string contentRootPath)
    {
        if (WordDocConverter.IsDocExtension(sourcePath))
        {
            WordDocConverter.ConvertToDocx(sourcePath, destinationPath);

            var seedCopy = Path.Combine(contentRootPath, "SeedDocuments", StoredFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(seedCopy)!);
            File.Copy(destinationPath, seedCopy, overwrite: true);
            return;
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static string? ResolveSourcePath(string contentRootPath)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var candidates = new[]
        {
            // Вже сконвертований .docx у проєкті — найнадійніше
            Path.Combine(contentRootPath, "SeedDocuments", DisplayFileName),
            Path.Combine(contentRootPath, "SeedDocuments", "adaptivity-200.docx"),
            // Оригінал з робочого столу
            Path.Combine(desktop, "тест", "Адаптивність 200.doc"),
            Path.Combine(desktop, "тест", "Адаптивність-200.doc"),
            Path.Combine(desktop, "тест", "Адаптивність 200.docx"),
            Path.Combine(contentRootPath, "App_Data", "Documents", FolderName, StoredFileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
