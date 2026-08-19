using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Services;

public sealed class AssingerScoringResult
{
    public int TotalPoints { get; init; }

    public int ScoreThreeCount { get; init; }

    public int ScoreOneCount { get; init; }

    public string LevelName { get; init; } = string.Empty;

    public string PatternNote { get; init; } = string.Empty;

    public string Conclusion { get; init; } = string.Empty;
}

public static class AssingerScoring
{
    public static bool CanScore(TestDocument? document, IReadOnlyCollection<TestQuestion> questions)
    {
        if (document is null || questions.Count == 0)
        {
            return false;
        }

        var looksLikeAssinger =
            ContainsIgnoreCase(document.RelativePath, "агресивн") ||
            ContainsIgnoreCase(document.RelativePath, "ассингер") ||
            ContainsIgnoreCase(document.RelativePath, "assinger") ||
            ContainsIgnoreCase(document.OriginalFileName, "агресивн") ||
            ContainsIgnoreCase(document.OriginalFileName, "ассингер") ||
            ContainsIgnoreCase(document.OriginalFileName, "assinger") ||
            ContainsIgnoreCase(document.Title, "Ассингер") ||
            ContainsIgnoreCase(document.Title, "Assinger") ||
            ContainsIgnoreCase(document.Title, "агресивності у відношен") ||
            ContainsIgnoreCase(document.Title, "оцінка агресивності");

        if (!looksLikeAssinger)
        {
            return false;
        }

        return questions.Count is >= 18 and <= 22 &&
               questions.All(q => q.Type == QuestionType.SingleChoice && q.Options.Count >= 3);
    }

    public static AssingerScoringResult Evaluate(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var scores = new List<int>(questions.Count);
        foreach (var question in questions.OrderBy(q => q.SortOrder))
        {
            answersByQuestion.TryGetValue(question.Id, out var answer);
            var value = ResolveScore(question, answer);
            if (value.HasValue)
            {
                scores.Add(value.Value);
            }
        }

        var total = scores.Sum();
        var scoreThreeCount = scores.Count(v => v == 3);
        var scoreOneCount = scores.Count(v => v == 1);
        var (levelName, conclusion) = MapTotal(total);
        var patternNote = MapPattern(scoreThreeCount, scoreOneCount);

        if (!string.IsNullOrWhiteSpace(patternNote))
        {
            conclusion = $"{conclusion} {patternNote}";
        }

        return new AssingerScoringResult
        {
            TotalPoints = total,
            ScoreThreeCount = scoreThreeCount,
            ScoreOneCount = scoreOneCount,
            LevelName = levelName,
            PatternNote = patternNote,
            Conclusion = conclusion
        };
    }

    private static int? ResolveScore(TestQuestion question, TestAnswer? answer)
    {
        if (answer?.SelectedOption is null)
        {
            return null;
        }

        var key = answer.SelectedOption.Key?.Trim();
        if (int.TryParse(key, out var fromKey) && fromKey is >= 1 and <= 3)
        {
            return fromKey;
        }

        if (answer.SelectedOption.SortOrder is >= 1 and <= 3)
        {
            var matched = question.Options.FirstOrDefault(o => o.Id == answer.SelectedOption.Id);
            if (matched is not null && matched.SortOrder is >= 1 and <= 3)
            {
                return matched.SortOrder;
            }
        }

        return null;
    }

    private static (string Level, string Conclusion) MapTotal(int total)
    {
        if (total >= 45)
        {
            return (
                "надмірна агресивність",
                "За результатами тесту А. Ассингера сума номерів відповідей становить " +
                $"{total} балів (45 і більше). Виявлено надмірну агресивність: людина нерідко буває " +
                "неврівноваженою і жорстокою по відношенню до інших, може жертвувати інтересами оточення " +
                "задля власного успіху. Рекомендовано індивідуальну бесіду з психологом та контроль " +
                "поведінки у службових конфліктах.");
        }

        if (total >= 36)
        {
            return (
                "помірна агресивність",
                "За результатами тесту А. Ассингера сума номерів відповідей становить " +
                $"{total} балів (36–44). Агресивність помірна: достатньо здорового честолюбства " +
                "і самовпевненості для успішного просування, без ознак надмірної конфліктності за цією методикою. " +
                "Рекомендовано звичайний психологічний супровід.");
        }

        return (
            "надмірна миролюбність",
            "За результатами тесту А. Ассингера сума номерів відповідей становить " +
            $"{total} балів (35 і менше). Виявлено надмірну миролюбність, що може бути зумовлено " +
            "недостатньою впевненістю у власних силах і можливостях. Рекомендовано розвивати рішучість " +
            "та навички конструктивного відстоювання позиції.");
    }

    private static string MapPattern(int scoreThreeCount, int scoreOneCount)
    {
        if (scoreThreeCount >= 7 && scoreOneCount < 7)
        {
            return "За додатковим ключем (7 і більше відповідей «3» і менше ніж 7 відповідей «1») " +
                   "вибухи агресивності носять швидше руйнівний, ніж конструктивний характер: " +
                   "схильність до непродуманих вчинків, запеклих дискусій і провокування конфліктів.";
        }

        if (scoreOneCount >= 7 && scoreThreeCount < 7)
        {
            return "За додатковим ключем (7 і більше відповідей «1» і менше ніж 7 відповідей «3») " +
                   "виявлено надмірну замкнутість: спалахи агресивності можливі, але пригнічуються надто ретельно.";
        }

        return string.Empty;
    }

    private static bool ContainsIgnoreCase(string? source, string value)
        => !string.IsNullOrWhiteSpace(source) &&
           source.Contains(value, StringComparison.OrdinalIgnoreCase);
}
