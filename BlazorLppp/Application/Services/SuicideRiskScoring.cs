using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Services;

public sealed class SuicideRiskScoringResult
{
    public int LieMatches { get; init; }

    public int LieMax { get; init; } = 10;

    public double LieCoefficient { get; init; }

    public int RiskMatches { get; init; }

    public int RiskMax { get; init; } = 35;

    public double RiskCoefficient { get; init; }

    public int Score { get; init; }

    public string RiskLevelName { get; init; } = string.Empty;

    public string Conclusion { get; init; } = string.Empty;

    public bool IsReliabilityLow { get; init; }

    public string ReliabilityNote { get; init; } = string.Empty;
}

public static class SuicideRiskScoring
{
    // Шкала «неправди» (L): відповідь «Так» за ключем
    private static readonly HashSet<int> LieYes =
    [
        11, 12, 18, 21, 23, 25, 29, 34, 39
    ];

    // Шкала «неправди» (L): відповідь «Ні» за ключем
    private static readonly HashSet<int> LieNo = [42];

    // Шкала Sr: відповідь «Так» за ключем
    private static readonly HashSet<int> RiskYes =
    [
        1, 2, 3, 5, 7, 9, 13, 14, 15, 16, 19, 22, 24, 28, 31, 33, 35, 36, 37, 38, 40, 41, 43, 44
    ];

    // Шкала Sr: відповідь «Ні» за ключем
    private static readonly HashSet<int> RiskNo =
    [
        4, 6, 8, 10, 17, 20, 26, 27, 30, 32, 45
    ];

    public static bool CanScore(TestDocument? document, IReadOnlyCollection<TestQuestion> questions)
    {
        if (document is null || questions.Count == 0)
        {
            return false;
        }

        var looksLikeSuicideTest =
            ContainsIgnoreCase(document.RelativePath, "TEST-suicid") ||
            ContainsIgnoreCase(document.OriginalFileName, "суїцид") ||
            ContainsIgnoreCase(document.Title, "суїцид") ||
            ContainsIgnoreCase(document.Title, "СР-45") ||
            ContainsIgnoreCase(document.Title, "CP-45");

        if (!looksLikeSuicideTest ||
            ContainsIgnoreCase(document.Title, "Горськ") ||
            ContainsIgnoreCase(document.OriginalFileName, "горськ"))
        {
            return false;
        }

        return questions.Count >= 40 &&
               questions.All(q => q.Type is QuestionType.YesNo or QuestionType.SingleChoice);
    }

    public static SuicideRiskScoringResult Evaluate(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var yesNoBySortOrder = new Dictionary<int, bool?>();

        foreach (var question in questions)
        {
            answersByQuestion.TryGetValue(question.Id, out var answer);
            yesNoBySortOrder[question.SortOrder] = ResolveYesNo(answer);
        }

        var lieMatches = CountMatches(yesNoBySortOrder, LieYes, expectedYes: true)
                         + CountMatches(yesNoBySortOrder, LieNo, expectedYes: false);
        var riskMatches = CountMatches(yesNoBySortOrder, RiskYes, expectedYes: true)
                          + CountMatches(yesNoBySortOrder, RiskNo, expectedYes: false);

        var lieCoefficient = lieMatches / 10d;
        var riskCoefficient = riskMatches / 35d;
        var (score, levelName, conclusion) = MapScore(riskCoefficient);
        var reliabilityLow = lieCoefficient >= 0.6;

        return new SuicideRiskScoringResult
        {
            LieMatches = lieMatches,
            LieCoefficient = lieCoefficient,
            RiskMatches = riskMatches,
            RiskCoefficient = riskCoefficient,
            Score = score,
            RiskLevelName = levelName,
            Conclusion = conclusion,
            IsReliabilityLow = reliabilityLow,
            ReliabilityNote = reliabilityLow
                ? "Увага: за шкалою «неправди» (L) отримано підвищений показник. Це може свідчити про прагнення представити себе у вигідному світлі та знижує достовірність результатів обстеження."
                : "За шкалою «неправди» (L) показник у межах прийнятної достовірності результатів обстеження."
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

    private static (int Score, string LevelName, string Conclusion) MapScore(double sr)
    {
        // За методикою: 0,01–0,23 → 5; ...; 0,75–1,00 → 1. Значення 0 трактуємо як найнижчий ризик.
        if (sr <= 0.23)
        {
            return (
                5,
                "Низький",
                "За результатами методики СР-45 у працівника виявлено низький рівень схильності до суїцидальних реакцій. Психологічний стан на момент обстеження не дає підстав для віднесення до групи суїцидального ризику. Рекомендовано звичайний психологічний супровід у межах планової роботи.");
        }

        if (sr <= 0.38)
        {
            return (
                4,
                "Нижче середнього",
                "За результатами методики СР-45 у працівника виявлено рівень схильності до суїцидальних реакцій нижче середнього. Суїцидальна реакція може виникнути переважно на тлі тривалої психічної травматизації або реактивних станів. Рекомендовано посилити увагу до психоемоційного стану та забезпечити доступність психологічної підтримки.");
        }

        if (sr <= 0.59)
        {
            return (
                3,
                "Середній",
                "За результатами методики СР-45 у працівника виявлено середній рівень схильності до суїцидальних реакцій. Потенціал схильності не відзначається високою стійкістю, проте потребує спостереження. Рекомендовано провести додаткову індивідуальну психологічну бесіду та динамічний контроль стану.");
        }

        if (sr <= 0.74)
        {
            return (
                2,
                "Вище середнього",
                "За результатами методики СР-45 працівник належить до групи суїцидального ризику з рівнем прояву схильності вище середнього. Можливі складнощі в адаптації та прояви саморуйнівної поведінки. Рекомендовано посилений психологічний супровід, індивідуальну роботу з психологом і контроль з боку командування (керівництва).");
        }

        return (
            1,
            "Високий",
            "За результатами методики СР-45 у працівника виявлено високий рівень схильності до суїцидальних реакцій. Стан може свідчити про внутрішній та/або зовнішній конфлікт. Рекомендовано терміново забезпечити медико-психологічну допомогу, посилене спостереження та обмежити фактори додаткового психоемоційного навантаження до стабілізації стану.");
    }

    private static bool ContainsIgnoreCase(string? source, string value)
        => !string.IsNullOrWhiteSpace(source) &&
           source.Contains(value, StringComparison.OrdinalIgnoreCase);
}
