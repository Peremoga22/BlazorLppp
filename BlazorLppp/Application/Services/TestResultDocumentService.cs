using System.Text;
using System.Text.RegularExpressions;

using BlazorLppp.Data;
using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using BlazorLppp.Application.Models;

namespace BlazorLppp.Application.Services;

public partial class TestResultDocumentService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IWebHostEnvironment environment,
    IOptions<DocumentStorageOptions> documentOptions) : ITestResultDocumentService
{
    public async Task<string> GenerateAsync(
        TestAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var document = attempt.TestDocumentId.HasValue
            ? await dbContext.TestDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == attempt.TestDocumentId.Value, cancellationToken)
            : null;

        var questions = await dbContext.TestQuestions
            .AsNoTracking()
            .Include(q => q.Options)
            .Where(q => attempt.TestDocumentId.HasValue && q.TestDocumentId == attempt.TestDocumentId.Value)
            .OrderBy(q => q.SortOrder)
            .ToListAsync(cancellationToken);

        var answers = await dbContext.TestAnswers
            .AsNoTracking()
            .Include(a => a.SelectedOption)
            .Where(a => a.TestAttemptId == attempt.Id)
            .ToListAsync(cancellationToken);

        var answersByQuestion = answers.ToDictionary(a => a.TestQuestionId);

        var baseName = BuildFileBaseName(attempt.LastName, attempt.FirstName, attempt.MiddleName);
        var root = ResolveResultsRoot();
        var folderPath = Path.Combine(root, baseName);
        Directory.CreateDirectory(folderPath);

        var fileName = $"{baseName}.docx";
        var absolutePath = Path.Combine(folderPath, fileName);

        await using (var stream = new FileStream(
            absolutePath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true))
        {
            using var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);
            var mainPart = word.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            AppendParagraph(body, document?.Title ?? "Результати психологічного тесту", bold: true, fontSize: "28");
            AppendParagraph(body, string.Empty);
            AppendParagraph(body, $"Прізвище: {attempt.LastName}");
            AppendParagraph(body, $"Ім’я: {attempt.FirstName}");
            AppendParagraph(body, $"По батькові: {attempt.MiddleName}");
            AppendParagraph(body, $"Номер: {attempt.NumberUnit}");
            AppendParagraph(body, $"Початок: {attempt.StartedAt:dd.MM.yyyy HH:mm}");
            AppendParagraph(body, $"Завершення: {(attempt.CompletedAt ?? DateTime.Now):dd.MM.yyyy HH:mm}");
            AppendParagraph(body, string.Empty);

            if (!string.IsNullOrWhiteSpace(document?.Instruction))
            {
                AppendParagraph(body, $"Інструкція: {document.Instruction}");
                AppendParagraph(body, string.Empty);
            }

            AppendParagraph(body, "Відповіді:", bold: true);
            AppendParagraph(body, string.Empty);

            foreach (var question in questions)
            {
                AppendParagraph(body, $"{question.SortOrder}. {question.Text}", bold: true);
                if (!string.IsNullOrWhiteSpace(question.Hint))
                {
                    AppendParagraph(body, question.Hint);
                }

                answersByQuestion.TryGetValue(question.Id, out var answer);
                var answerText = FormatAnswer(question, answer);
                AppendParagraph(body, $"Відповідь: {answerText}");
                AppendParagraph(body, string.Empty);
            }

            mainPart.Document.Save();
        }

        return Path.Combine(baseName, fileName).Replace('\\', '/');
    }

    public string GetAbsolutePath(string relativePath)
    {
        var root = ResolveResultsRoot();
        var combined = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Некоректний шлях до файлу результату.");
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
        var root = ResolveResultsRoot();
        if (!string.IsNullOrWhiteSpace(folderPath) &&
            !string.Equals(folderPath, root, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(folderPath) &&
            !Directory.EnumerateFileSystemEntries(folderPath).Any())
        {
            Directory.Delete(folderPath);
        }

        return Task.CompletedTask;
    }

    public string BuildFileBaseName(string lastName, string firstName, string middleName)
    {
        var surname = SanitizeSegment(lastName);
        var firstInitial = Initial(firstName);
        var middleInitial = Initial(middleName);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(surname))
        {
            parts.Add(surname);
        }

        if (!string.IsNullOrWhiteSpace(firstInitial))
        {
            parts.Add(firstInitial);
        }

        if (!string.IsNullOrWhiteSpace(middleInitial))
        {
            parts.Add(middleInitial);
        }

        return parts.Count == 0
            ? $"result-{DateTime.Now:yyyyMMdd-HHmmss}"
            : string.Join('_', parts);
    }

    private string ResolveResultsRoot()
    {
        var documentsRoot = documentOptions.Value.RootPath;
        var documentsAbsolute = Path.IsPathRooted(documentsRoot)
            ? documentsRoot
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, documentsRoot));

        var appData = Path.GetDirectoryName(documentsAbsolute)
            ?? Path.Combine(environment.ContentRootPath, "App_Data");

        return Path.GetFullPath(Path.Combine(appData, "Results"));
    }

    private static string FormatAnswer(TestQuestion question, TestAnswer? answer)
    {
        if (answer is null)
        {
            return "—";
        }

        return question.Type switch
        {
            QuestionType.Scale => answer.ScaleValue?.ToString() ?? "—",
            QuestionType.SingleChoice or QuestionType.YesNo when answer.SelectedOption is not null
                => string.IsNullOrWhiteSpace(answer.SelectedOption.Key) ||
                   answer.SelectedOption.Key is "Так" or "Ні"
                    ? answer.SelectedOption.Text
                    : $"{answer.SelectedOption.Key}. {answer.SelectedOption.Text}",
            _ => "—"
        };
    }

    private static void AppendParagraph(Body body, string text, bool bold = false, string fontSize = "22")
    {
        var runProperties = new RunProperties(new FontSize { Val = fontSize });
        if (bold)
        {
            runProperties.AppendChild(new Bold());
        }

        var paragraph = new Paragraph(
            new Run(runProperties, new Text(text)));

        body.AppendChild(paragraph);
    }

    private static string Initial(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return char.ToUpperInvariant(trimmed[0]).ToString();
    }

    private static string SanitizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (Path.GetInvalidFileNameChars().Contains(ch) || InvalidNameChars().IsMatch(ch.ToString()))
            {
                builder.Append('-');
                continue;
            }

            builder.Append(ch);
        }

        return MultiDash().Replace(builder.ToString(), "-").Trim('-', '.', ' ', '_');
    }

    [GeneratedRegex(@"[<>:""/\\|?*\x00-\x1F]")]
    private static partial Regex InvalidNameChars();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDash();
}
