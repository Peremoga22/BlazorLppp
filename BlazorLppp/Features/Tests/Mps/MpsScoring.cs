using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Services;

public sealed class MpsComponentScore
{
    public string Symbol { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Середнє арифметичне балів (0–10) за формулами 3.2–3.7.</summary>
    public double Average { get; init; }

    public int Sum { get; init; }

    public int Answered { get; init; }

    public int Total { get; init; }

    public string LevelName { get; init; } = string.Empty;
}

public sealed class MpsLowCriterion
{
    public int SortOrder { get; init; }

    public string Text { get; init; } = string.Empty;

    public int Score { get; init; }
}

public sealed class MpsScoringResult
{
    public bool IsScorable { get; init; }

    public IReadOnlyList<MpsComponentScore> Components { get; init; } = [];

    /// <summary>Р МПС — рівень МПС за формулою 3.8 (0–10).</summary>
    public double Score { get; init; }

    public double ScoreMax { get; init; } = 10;

    /// <summary>Коефіцієнт МПС (Р МПС / 10).</summary>
    public double Coefficient { get; init; }

    public string LevelName { get; init; } = string.Empty;

    public bool SupportsTasks { get; init; }

    public string Conclusion { get; init; } = string.Empty;

    public IReadOnlyList<string> MotivationChoices { get; init; } = [];

    public IReadOnlyList<MpsLowCriterion> LowestCriteria { get; init; } = [];
}

public static class MpsScoring
{
    public const string Optimal = "Оптимальний";
    public const string Satisfactory = "Задовільний";
    public const string Critical = "Критичний";
    public const string Unsatisfactory = "Незадовільний";

    public static bool CanScore(TestDocument? document, IReadOnlyCollection<TestQuestion> questions)
    {
        if (document is null || questions.Count != MpsDocumentTemplate.QuestionCount)
        {
            return false;
        }

        if (!LooksLike(document))
        {
            return false;
        }

        return questions.Count(q => q.Type == QuestionType.Scale) == MpsDocumentTemplate.QuestionCount - 1 &&
               questions.Any(q => q.Type == QuestionType.MultiChoice &&
                                  q.SortOrder == MpsDocumentTemplate.MotivationQuestionOrder);
    }

    public static bool LooksLike(TestDocument? document)
    {
        if (document is null)
        {
            return false;
        }

        return ContainsIgnoreCase(document.Title, "морально-психологічного стану") ||
               ContainsIgnoreCase(document.Title, "(МПС)") ||
               ContainsIgnoreCase(document.RelativePath, MpsDocumentTemplate.FolderName) ||
               ContainsIgnoreCase(document.OriginalFileName, "МПС");
    }

    public static MpsScoringResult Evaluate(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var values = new Dictionary<int, int>();
        foreach (var question in questions.Where(q => q.Type == QuestionType.Scale))
        {
            answersByQuestion.TryGetValue(question.Id, out var answer);
            if (answer?.ScaleValue is int value)
            {
                values[question.SortOrder] = Math.Clamp(
                    value,
                    MpsDocumentTemplate.ScaleMinValue,
                    MpsDocumentTemplate.ScaleMaxValue);
            }
        }

        var components = new List<MpsComponentScore>();
        foreach (var component in MpsDocumentTemplate.Components)
        {
            var total = 0;
            var sum = 0;
            var answered = 0;
            for (var order = component.FirstQuestion; order <= component.LastQuestion; order++)
            {
                if (order == MpsDocumentTemplate.MotivationQuestionOrder)
                {
                    continue;
                }

                total++;
                if (values.TryGetValue(order, out var value))
                {
                    sum += value;
                    answered++;
                }
            }

            var average = answered == 0 ? 0 : (double)sum / answered;
            components.Add(new MpsComponentScore
            {
                Symbol = component.Symbol,
                Name = component.Name,
                Sum = sum,
                Answered = answered,
                Total = total,
                Average = Math.Round(average, 2),
                LevelName = MapLevel(average)
            });
        }

        var scorable = components.All(c => c.Answered > 0);
        var score = scorable ? Math.Round(components.Average(c => c.Average), 2) : 0;
        var level = scorable ? MapLevel(score) : "не оброблено";

        var lowest = values
            .Where(pair => pair.Value <= 4)
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(10)
            .Select(pair => new MpsLowCriterion
            {
                SortOrder = pair.Key,
                Text = MpsDocumentTemplate.ItemText(pair.Key) ?? string.Empty,
                Score = pair.Value
            })
            .ToList();

        return new MpsScoringResult
        {
            IsScorable = scorable,
            Components = components,
            Score = score,
            Coefficient = Math.Round(score / 10, 2),
            LevelName = level,
            SupportsTasks = scorable && score >= 7,
            Conclusion = scorable
                ? BuildConclusion(level, score)
                : "Недостатньо відповідей для розрахунку: за кожним компонентом МПС має бути хоча б одна оцінка.",
            MotivationChoices = ResolveMotivation(questions, answersByQuestion),
            LowestCriteria = lowest
        };
    }

    /// <summary>Таблиця 4.1 Інструкції: 8,5–10 / 7–8,49 / 5–6,9 / 1–4,9.</summary>
    public static string MapLevel(double score) => score switch
    {
        >= 8.5 => Optimal,
        >= 7 => Satisfactory,
        >= 5 => Critical,
        _ => Unsatisfactory
    };

    private static List<string> ResolveMotivation(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var question = questions.FirstOrDefault(q => q.SortOrder == MpsDocumentTemplate.MotivationQuestionOrder);
        if (question is null || !answersByQuestion.TryGetValue(question.Id, out var answer))
        {
            return [];
        }

        var (ids, extra) = AnonymousSurveyScoring.Unpack(answer.TextValue);
        if (ids.Count == 0 && answer.SelectedOptionId is Guid selected)
        {
            ids.Add(selected);
        }

        var labels = question.Options
            .OrderBy(o => o.SortOrder)
            .Where(o => ids.Contains(o.Id))
            .Select(o => o.Text)
            .ToList();

        if (!string.IsNullOrWhiteSpace(extra))
        {
            labels = labels
                .Select(label => AnonymousSurveyScoring.IsFreeTextOption(label)
                    ? $"Інше: {extra.Trim()}"
                    : label)
                .ToList();
            if (!labels.Any(label => label.StartsWith("Інше:", StringComparison.OrdinalIgnoreCase)))
            {
                labels.Add($"Інше: {extra.Trim()}");
            }
        }

        return labels;
    }

    private static string BuildConclusion(string level, double score)
    {
        var head = $"Рівень МПС за результатами опитування — {level.ToLowerInvariant()} (Р МПС = {score:0.##} з 10, " +
                   $"коефіцієнт {score / 10:0.##}). ";

        return level switch
        {
            Optimal => head +
                "Морально-психологічний стан сприяє виконанню завдань за призначенням. Характеризується вірою " +
                "в кінцевий успіх, у своїх командирів, у бойову техніку, в самих себе; високий рівень мотиваційної " +
                "і функціональної готовності та фахової здатності; об'єктивні і суб'єктивні чинники сприяють " +
                "виконанню завдань і позитивно впливають на МПС.",
            Satisfactory => head +
                "Морально-психологічний стан сприяє виконанню завдань за призначенням. Достатній рівень мотивації " +
                "та готовності виконувати службові обов'язки в бойових умовах; моральні якості і вольові якості " +
                "сформовані достатньо; об'єктивні і суб'єктивні чинники нейтрально впливають на МПС.",
            Critical => head +
                "Морально-психологічний стан не сприяє виконанню завдань за призначенням. Знижене почуття " +
                "особистої відповідальності, невисока згуртованість, довіра до командирів ставиться під сумнів; " +
                "рівень мотиваційної та функціональної готовності нижче середнього. Потрібен обов'язковий аналіз " +
                "критеріїв, що отримали найнижчі оцінки (п. 4.13 Інструкції).",
            _ => head +
                "Морально-психологічний стан не сприяє виконанню завдань за призначенням. Можливий стан межі " +
                "психофізичних можливостей або розпачу; командири не користуються довірою; чинники служби " +
                "негативно впливають на МПС. Потрібен обов'язковий аналіз критеріїв, що отримали найнижчі " +
                "оцінки, і термінові заходи (п. 4.13–4.14 Інструкції)."
        };
    }

    private static bool ContainsIgnoreCase(string? source, string value)
        => !string.IsNullOrWhiteSpace(source) &&
           source.Contains(value, StringComparison.OrdinalIgnoreCase);
}
