using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Services;

public sealed class SzchScoringResult
{
    public int SincerityMatches { get; init; }

    public int SincerityMax { get; init; } = 10;

    public bool IsScorable { get; init; }

    public int Score { get; init; }

    public int ScoreMax { get; init; } = 40;

    public string LevelName { get; init; } = string.Empty;

    public string Conclusion { get; init; } = string.Empty;

    public string ReliabilityNote { get; init; } = string.Empty;
}

public static class SzchScoring
{
    // Шкала щирості: «Ні» за ключем.
    private static readonly HashSet<int> SincerityNo = [1, 4, 6, 13];

    // Шкала щирості: «Так» за ключем.
    private static readonly HashSet<int> SincerityYes = [2, 5, 7, 11, 37, 40];

    // Основна шкала СЗЧ: «Так» за ключем.
    private static readonly HashSet<int> RiskYes =
    [
        8, 9, 16, 17, 18, 20, 21, 22, 25, 27, 29, 32, 33, 36, 38, 41, 42, 44, 47, 48
    ];

    // Основна шкала СЗЧ: «Ні» за ключем.
    private static readonly HashSet<int> RiskNo =
    [
        3, 10, 12, 14, 15, 19, 23, 24, 26, 28, 30, 31, 34, 35, 39, 43, 45, 46, 49, 50
    ];

    public static bool CanScore(TestDocument? document, IReadOnlyCollection<TestQuestion> questions)
    {
        if (document is null || questions.Count == 0)
        {
            return false;
        }

        if (!LooksLike(document))
        {
            return false;
        }

        return questions.Count is >= 45 and <= 55 &&
               questions.All(q => q.Type is QuestionType.YesNo or QuestionType.SingleChoice);
    }

    public static bool LooksLike(TestDocument? document)
    {
        if (document is null)
        {
            return false;
        }

        return ContainsIgnoreCase(document.RelativePath, "сзч") ||
               ContainsIgnoreCase(document.RelativePath, "szch") ||
               ContainsIgnoreCase(document.OriginalFileName, "сзч") ||
               ContainsIgnoreCase(document.OriginalFileName, "szch") ||
               ContainsIgnoreCase(document.OriginalFileName, "залишити частину") ||
               ContainsIgnoreCase(document.Title, "СЗЧ") ||
               ContainsIgnoreCase(document.Title, "залишити частину") ||
               ContainsIgnoreCase(document.Title, "самовільного залишення") ||
               ContainsIgnoreCase(document.Title, "самовільне залишення");
    }

    public static SzchScoringResult Evaluate(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var yesNoBySortOrder = new Dictionary<int, bool?>();
        foreach (var question in questions)
        {
            answersByQuestion.TryGetValue(question.Id, out var answer);
            yesNoBySortOrder[question.SortOrder] = ResolveYesNo(answer);
        }

        var sincerity = CountMatches(yesNoBySortOrder, SincerityYes, expectedYes: true)
                        + CountMatches(yesNoBySortOrder, SincerityNo, expectedYes: false);
        var scorable = sincerity >= 3;
        var score = scorable
            ? CountMatches(yesNoBySortOrder, RiskYes, expectedYes: true)
              + CountMatches(yesNoBySortOrder, RiskNo, expectedYes: false)
            : 0;

        var (levelName, conclusion) = scorable
            ? MapScore(score)
            : (
                "не оброблено",
                "За шкалою щирості методики СЗЧ-4 відповіді збігаються з ключем менше ніж у 3 випадках " +
                $"з 10 ({sincerity} збіги). Подальший аналіз тесту не проводиться; причини нещирості " +
                "слід з’ясовувати іншим способом."
            );

        return new SzchScoringResult
        {
            SincerityMatches = sincerity,
            IsScorable = scorable,
            Score = score,
            LevelName = levelName,
            Conclusion = conclusion,
            ReliabilityNote = scorable
                ? "За шкалою щирості показник достатній для обробки тесту."
                : "За шкалою щирості результат недостовірний — тест далі не аналізується."
        };
    }

    private static int CountMatches(
        IReadOnlyDictionary<int, bool?> answers,
        IEnumerable<int> keyQuestions,
        bool expectedYes)
    {
        var count = 0;
        foreach (var number in keyQuestions)
        {
            if (!answers.TryGetValue(number, out var answer) || answer is null)
            {
                continue;
            }

            if (answer.Value == expectedYes)
            {
                count++;
            }
        }

        return count;
    }

    private static bool? ResolveYesNo(TestAnswer? answer)
    {
        if (answer?.SelectedOption is null)
        {
            return null;
        }

        var value = answer.SelectedOption.Key?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            value = answer.SelectedOption.Text?.Trim();
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Equals("Так", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("+", StringComparison.Ordinal))
        {
            return true;
        }

        if (value.Equals("Ні", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("No", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("-", StringComparison.Ordinal) ||
            value.Equals("–", StringComparison.Ordinal))
        {
            return false;
        }

        return null;
    }

    private static (string Level, string Conclusion) MapScore(int score)
    {
        if (score < 15)
        {
            return (
                "вкрай низька",
                "За результатами методики СЗЧ-4 набрано " +
                $"{score} балів (до 15). Ймовірність самовільного залишення частини цим військовослужбовцем " +
                "вкрай низька, спеціальна робота з такими військовослужбовцями не потрібна.");
        }

        if (score <= 25)
        {
            return (
                "низька",
                "За результатами методики СЗЧ-4 набрано " +
                $"{score} балів (від 15 до 25). Ймовірність самовільного залишення частини низька, " +
                "але під впливом зовнішніх факторів і випадковостей воно можливе. З цією категорією " +
                "потрібна спеціальна індивідуальна робота.");
        }

        return (
            "велика",
            "За результатами методики СЗЧ-4 набрано " +
            $"{score} балів (більше 25). Ймовірність самовільного залишення частини велика " +
            "і тим більша, чим більше балів. Ця категорія потребує постійної уваги і контролю, " +
            "спеціальної роботи щодо нормалізації відносин у колективі, допомоги у самоствердженні " +
            "та приведенні психіки у врівноважений стан.");
    }

    private static bool ContainsIgnoreCase(string? source, string value)
        => !string.IsNullOrWhiteSpace(source) &&
           source.Contains(value, StringComparison.OrdinalIgnoreCase);
}
