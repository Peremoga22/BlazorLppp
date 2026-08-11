using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Services;

public sealed class ZbroyaScoringResult
{
    public int SumDirect { get; init; }

    public int SumCalm { get; init; }

    public int ReactiveAnxiety { get; init; }

    public string AnxietyLevelName { get; init; } = string.Empty;

    public int WellBeingPercent { get; init; }

    public int ActivityPercent { get; init; }

    public int MoodPercent { get; init; }

    public double SanIndex { get; init; }

    public string SanLevelName { get; init; } = string.Empty;

    public bool? ReadyForWeaponDuty { get; init; }

    public string Conclusion { get; init; } = string.Empty;
}

public static class ZbroyaScoring
{
    // За бланком: Σ1 (тривожні пункти) та Σ2 (решта). Пункти 8 і 9 додано за стандартною
    // розстановкою шкали (у бланку між 7→12 та 5→10 вони пропущені через форматування).
    private static readonly HashSet<int> DirectItems = [3, 4, 6, 7, 9, 12, 13, 14, 17, 18];
    private static readonly HashSet<int> CalmItems = [1, 2, 5, 8, 10, 11, 15, 16, 19, 20];

    private const int ReactiveAnxietyOffset = 35;
    private const int WellBeingSortOrder = 21;
    private const int ActivitySortOrder = 22;
    private const int MoodSortOrder = 23;
    private const int ReadinessSortOrder = 24;

    public static bool CanScore(TestDocument? document, IReadOnlyCollection<TestQuestion> questions)
    {
        if (document is null || questions.Count == 0)
        {
            return false;
        }

        var looksLikeZbroya =
            ContainsIgnoreCase(document.RelativePath, "зброя") ||
            ContainsIgnoreCase(document.RelativePath, "zbroya") ||
            ContainsIgnoreCase(document.OriginalFileName, "зброя") ||
            ContainsIgnoreCase(document.OriginalFileName, "ZBROYA") ||
            ContainsIgnoreCase(document.Title, "зброя") ||
            ContainsIgnoreCase(document.Title, "зі зброєю") ||
            ContainsIgnoreCase(document.Title, "служби зі зброєю");

        if (!looksLikeZbroya)
        {
            return false;
        }

        return questions.Count >= 20;
    }

    public static ZbroyaScoringResult Evaluate(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var scores = new Dictionary<int, int>();
        foreach (var question in questions)
        {
            answersByQuestion.TryGetValue(question.Id, out var answer);
            var value = ResolveScore(question, answer);
            if (value.HasValue)
            {
                scores[question.SortOrder] = value.Value;
            }
        }

        var sumDirect = SumItems(scores, DirectItems);
        var sumCalm = SumItems(scores, CalmItems);
        var rt = sumDirect - sumCalm + ReactiveAnxietyOffset;
        var anxietyLevel = MapAnxiety(rt);

        var wellBeing = ToPercent(scores.GetValueOrDefault(WellBeingSortOrder));
        var activity = ToPercent(scores.GetValueOrDefault(ActivitySortOrder));
        var mood = ToPercent(scores.GetValueOrDefault(MoodSortOrder));
        var san = (wellBeing + activity + mood) / 3d;
        var sanLevel = MapSan(san);

        var ready = ResolveReady(questions, answersByQuestion);

        return new ZbroyaScoringResult
        {
            SumDirect = sumDirect,
            SumCalm = sumCalm,
            ReactiveAnxiety = rt,
            AnxietyLevelName = anxietyLevel,
            WellBeingPercent = wellBeing,
            ActivityPercent = activity,
            MoodPercent = mood,
            SanIndex = Math.Round(san, 1),
            SanLevelName = sanLevel,
            ReadyForWeaponDuty = ready,
            Conclusion = BuildConclusion(rt, anxietyLevel, san, sanLevel, ready)
        };
    }

    private static int SumItems(IReadOnlyDictionary<int, int> scores, IEnumerable<int> items)
    {
        var sum = 0;
        foreach (var item in items)
        {
            if (scores.TryGetValue(item, out var value))
            {
                sum += value;
            }
        }

        return sum;
    }

    private static int? ResolveScore(TestQuestion question, TestAnswer? answer)
    {
        if (answer is null)
        {
            return null;
        }

        if (question.Type == QuestionType.Scale && answer.ScaleValue.HasValue)
        {
            return answer.ScaleValue.Value;
        }

        var key = answer.SelectedOption?.Key?.Trim();
        var text = answer.SelectedOption?.Text?.Trim();

        if (int.TryParse(key, out var fromKey))
        {
            return fromKey;
        }

        if (!string.IsNullOrWhiteSpace(key) && key.Length == 1)
        {
            var letter = char.ToUpperInvariant(key[0]);
            return letter switch
            {
                'A' or 'А' => 1,
                'B' or 'Б' => 2,
                'C' or 'В' or 'C' => 3,
                'D' or 'Г' => 4,
                _ => null
            };
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (text.StartsWith("Ні", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("не так", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (text.Contains("Мабуть", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (text.Equals("Правильно", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (text.Contains("Абсолютно", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (text.Equals("Так", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Yes", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (text.Equals("Ні", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("No", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return null;
    }

    private static bool? ResolveReady(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var readiness = questions.FirstOrDefault(q => q.SortOrder == ReadinessSortOrder)
                        ?? questions.FirstOrDefault(q =>
                            q.Text.Contains("Готовий нести службу зі зброєю", StringComparison.OrdinalIgnoreCase) ||
                            q.Text.Contains("Чи згодні Ви", StringComparison.OrdinalIgnoreCase));

        if (readiness is null)
        {
            return null;
        }

        answersByQuestion.TryGetValue(readiness.Id, out var answer);
        if (answer?.SelectedOption is null)
        {
            return null;
        }

        var value = answer.SelectedOption.Key?.Trim() ?? answer.SelectedOption.Text?.Trim() ?? string.Empty;
        if (value.Equals("Так", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("A", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("Ні", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("No", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("B", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("2", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static int ToPercent(int scale0To10)
        => Math.Clamp(scale0To10, 0, 10) * 10;

    private static string MapAnxiety(int rt) => rt switch
    {
        <= 30 => "низька",
        <= 45 => "помірна",
        _ => "висока"
    };

    private static string MapSan(double san) => san switch
    {
        <= 20 => "низький",
        <= 60 => "середній",
        _ => "високий"
    };

    private static string BuildConclusion(
        int rt,
        string anxietyLevel,
        double san,
        string sanLevel,
        bool? ready)
    {
        var readinessText = ready switch
        {
            true => "Працівник підтвердив готовність нести службу зі зброєю.",
            false => "Працівник не підтвердив готовність нести службу зі зброєю.",
            null => "Відмітка про готовність до служби зі зброєю відсутня."
        };

        if (rt >= 46 || ready == false)
        {
            return
                $"За результатами тесту «ЗБРОЯ» реактивна тривожність РТ = {rt} ({anxietyLevel}); " +
                $"індекс САН = {san:0.#}% ({sanLevel} рівень). {readinessText} " +
                "Рівень схильності до дезадаптивних реакцій підвищений або готовність не підтверджена. " +
                "Рекомендовано додаткову індивідуальну бесіду з психологом і рішення про допуск до служби зі зброєю " +
                "приймати після уточнення стану.";
        }

        if (rt >= 31)
        {
            return
                $"За результатами тесту «ЗБРОЯ» реактивна тривожність РТ = {rt} ({anxietyLevel}); " +
                $"індекс САН = {san:0.#}% ({sanLevel} рівень). {readinessText} " +
                "Показники в межах помірного рівня. Рекомендовано врахувати динаміку стану перед допуском " +
                "до несення служби зі зброєю та за потреби провести уточнювальну бесіду.";
        }

        return
            $"За результатами тесту «ЗБРОЯ» реактивна тривожність РТ = {rt} ({anxietyLevel}); " +
            $"індекс САН = {san:0.#}% ({sanLevel} рівень). {readinessText} " +
            "За показниками реактивної тривожності стан сприятливий для допуску до несення служби зі зброєю " +
            "за відсутності інших негативних чинників впливу на морально-психологічний стан.";
    }

    private static bool ContainsIgnoreCase(string? source, string value)
        => !string.IsNullOrWhiteSpace(source) &&
           source.Contains(value, StringComparison.OrdinalIgnoreCase);
}
