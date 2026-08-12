using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Services;

public sealed class HorskaScaleScore
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public int Points { get; init; }

    public string LevelName { get; init; } = string.Empty;

    public string Note { get; init; } = string.Empty;
}

public sealed class HorskaScoringResult
{
    public HorskaScaleScore Anxiety { get; init; } = new();

    public HorskaScaleScore Frustration { get; init; } = new();

    public HorskaScaleScore Aggression { get; init; } = new();

    public HorskaScaleScore Rigidity { get; init; } = new();

    public int TotalPoints { get; init; }

    public string RiskLevelName { get; init; } = string.Empty;

    public string Conclusion { get; init; } = string.Empty;
}

public static class HorskaScoring
{
    // Ключі за методикою М.В. Горської (типові номери; виправлено очевидні опечатки бланка 37→27, 26→36).
    private static readonly int[] AnxietyItems = [1, 5, 9, 13, 17, 21, 25, 29, 33, 37];
    private static readonly int[] FrustrationItems = [2, 6, 10, 14, 18, 22, 26, 30, 34, 38];
    private static readonly int[] AggressionItems = [3, 7, 11, 15, 19, 23, 27, 31, 35, 39];
    private static readonly int[] RigidityItems = [4, 8, 12, 16, 20, 24, 28, 32, 36, 40];

    public static bool CanScore(TestDocument? document, IReadOnlyCollection<TestQuestion> questions)
    {
        if (document is null || questions.Count == 0)
        {
            return false;
        }

        var looksLikeHorska =
            ContainsIgnoreCase(document.RelativePath, "горськ") ||
            ContainsIgnoreCase(document.RelativePath, "gorska") ||
            ContainsIgnoreCase(document.OriginalFileName, "горськ") ||
            ContainsIgnoreCase(document.OriginalFileName, "Горська") ||
            ContainsIgnoreCase(document.Title, "Горськ") ||
            ContainsIgnoreCase(document.Title, "М.В. Горська") ||
            (ContainsIgnoreCase(document.Title, "схильності до суїцидальної") &&
             !ContainsIgnoreCase(document.Title, "СР-45") &&
             !ContainsIgnoreCase(document.Title, "Юнацкевіч"));

        if (!looksLikeHorska)
        {
            return false;
        }

        return questions.Count is >= 35 and <= 45 &&
               questions.All(q => q.Type == QuestionType.Scale);
    }

    public static HorskaScoringResult Evaluate(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var values = new Dictionary<int, int>();
        foreach (var question in questions)
        {
            answersByQuestion.TryGetValue(question.Id, out var answer);
            var value = answer?.ScaleValue;
            if (value.HasValue)
            {
                values[question.SortOrder] = Math.Clamp(value.Value, 0, 2);
            }
        }

        var anxiety = Sum(values, AnxietyItems);
        var frustration = Sum(values, FrustrationItems);
        var aggression = Sum(values, AggressionItems);
        var rigidity = Sum(values, RigidityItems);
        var total = anxiety + frustration + aggression + rigidity;

        var (riskLevel, conclusion) = MapTotal(total);

        return new HorskaScoringResult
        {
            Anxiety = new HorskaScaleScore
            {
                Name = "Шкала тривожності",
                Description = "Виявляє рівень здатності індивіда до відчуття тривоги.",
                Points = anxiety,
                LevelName = MapAnxiety(anxiety),
                Note = string.Empty
            },
            Frustration = new HorskaScaleScore
            {
                Name = "Шкала фрустрації",
                Description =
                    "Виявляє показник психічного стану, який виникає через реальні або уявні перешкоди, що заважають досягненню мети.",
                Points = frustration,
                LevelName = MapFrustration(frustration),
                Note = string.Empty
            },
            Aggression = new HorskaScaleScore
            {
                Name = "Шкала агресії",
                Description =
                    "Виявляє підвищену психологічну активність, прагнення до лідерства через застосування сили по відношенню до інших людей.",
                Points = aggression,
                LevelName = MapAggression(aggression),
                Note = "Для суїцидентів допускається зниження агресивності від 10 до 0."
            },
            Rigidity = new HorskaScaleScore
            {
                Name = "Шкала ригідності",
                Description =
                    "Утруднення в зміні визначеної суб'єктом діяльності в умовах, які об'єктивно потребують її перебудови.",
                Points = rigidity,
                LevelName = MapRigidity(rigidity),
                Note = "Для осіб із суїцидальною поведінкою — 13 балів і вище."
            },
            TotalPoints = total,
            RiskLevelName = riskLevel,
            Conclusion = conclusion
        };
    }

    private static int Sum(IReadOnlyDictionary<int, int> values, IEnumerable<int> items)
    {
        var sum = 0;
        foreach (var item in items)
        {
            if (values.TryGetValue(item, out var value))
            {
                sum += value;
            }
        }

        return sum;
    }

    private static string MapAnxiety(int points) => points switch
    {
        <= 7 => "низький рівень тривожності",
        <= 11 => "середній рівень тривожності",
        <= 16 => "високий рівень тривожності",
        _ => "дуже високий рівень тривожності"
    };

    private static string MapFrustration(int points) => points switch
    {
        <= 7 => "низький рівень фрустрації",
        <= 9 => "середній рівень фрустрації",
        <= 15 => "високий рівень фрустрації",
        _ => "дуже високий рівень фрустрації"
    };

    private static string MapAggression(int points) => points switch
    {
        <= 10 => "низький рівень агресивності",
        <= 12 => "середній рівень агресивності",
        <= 16 => "високий рівень агресивності",
        _ => "дуже високий рівень агресивності"
    };

    private static string MapRigidity(int points) => points switch
    {
        <= 10 => "низький рівень ригідності",
        <= 12 => "середній рівень ригідності",
        <= 16 => "високий рівень ригідності",
        _ => "дуже високий рівень ригідності"
    };

    private static (string Level, string Conclusion) MapTotal(int total)
    {
        if (total <= 38)
        {
            return (
                "низький",
                "За результатами методики вивчення схильності до суїцидальної поведінки (М.В. Горська) " +
                $"сумарний показник становить {total} балів. Рівень схильності до суїцидальної поведінки низький. " +
                "Рекомендовано звичайний психологічний супровід у межах планової роботи.");
        }

        if (total <= 45)
        {
            return (
                "в нормі",
                "За результатами методики вивчення схильності до суїцидальної поведінки (М.В. Горська) " +
                $"сумарний показник становить {total} балів. Рівень схильності до суїцидальної поведінки знаходиться в нормі. " +
                "Рекомендовано динамічне спостереження та підтримка психоемоційного стану.");
        }

        return (
            "високий",
            "За результатами методики вивчення схильності до суїцидальної поведінки (М.В. Горська) " +
            $"сумарний показник становить {total} балів. Рівень схильності до суїцидальної поведінки високий, " +
            "потрібна корекційна робота. Рекомендовано індивідуальну роботу з психологом та посилений супровід.");
    }

    private static bool ContainsIgnoreCase(string? source, string value)
        => !string.IsNullOrWhiteSpace(source) &&
           source.Contains(value, StringComparison.OrdinalIgnoreCase);
}
