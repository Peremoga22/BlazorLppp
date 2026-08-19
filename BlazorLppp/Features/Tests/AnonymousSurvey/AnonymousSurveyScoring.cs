using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Services;

public sealed class AnonymousSurveyScoringResult
{
    public string RankName { get; init; } = string.Empty;

    public string CombatExperience { get; init; } = string.Empty;

    public IReadOnlyList<string> Family { get; init; } = [];

    public string Relations { get; init; } = string.Empty;

    public string Justice { get; init; } = string.Empty;

    public IReadOnlyList<string> PersonalProblems { get; init; } = [];

    public IReadOnlyList<string> HeardProblems { get; init; } = [];

    public IReadOnlyList<string> Anxiety { get; init; } = [];

    public string Readiness { get; init; } = string.Empty;

    public int ReadinessLevel { get; init; }

    public IReadOnlyList<string> Changes { get; init; } = [];

    public string? OtherAnxiety { get; init; }

    public string? OtherChange { get; init; }

    public bool NeedsAttention { get; init; }

    public string Conclusion { get; init; } = string.Empty;
}

public static class AnonymousSurveyScoring
{
    public const int CombatSort = 1;
    public const int FamilySort = 2;
    public const int RelationsSort = 3;
    public const int JusticeSort = 4;
    public const int PhenomenonFirstSort = 5;
    public const int PhenomenonLastSort = 11;
    public const int AnxietySort = 12;
    public const int ReadinessSort = 13;
    public const int ChangesSort = 14;

    public static bool CanScore(TestDocument? document, IReadOnlyCollection<TestQuestion> questions)
        => LooksLike(document) ||
           (questions.Count is >= 12 and <= 20 &&
            questions.Any(q => ContainsIgnoreCase(q.Text, "бойовий досвід")));

    public static bool LooksLike(TestDocument? document)
        => document is not null &&
           (ContainsIgnoreCase(document.Title, "анонімне опитуван") ||
            ContainsIgnoreCase(document.OriginalFileName, "анонімне") ||
            ContainsIgnoreCase(document.RelativePath, "анонімне") ||
            ContainsIgnoreCase(document.FolderName, "анонімне"));

    public static AnonymousSurveyScoringResult Evaluate(
        TestAttempt attempt,
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var byOrder = questions.ToDictionary(q => q.SortOrder);
        string Single(int sort) => ResolveSingle(byOrder, answersByQuestion, sort);
        IReadOnlyList<string> Multi(int sort, out string? extra)
        {
            extra = null;
            if (!byOrder.TryGetValue(sort, out var question))
            {
                return [];
            }

            answersByQuestion.TryGetValue(question.Id, out var answer);
            var (ids, text) = Unpack(answer?.TextValue);
            extra = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            if (ids.Count == 0 && answer?.SelectedOptionId is Guid one)
            {
                ids.Add(one);
            }

            return question.Options
                .Where(o => ids.Contains(o.Id))
                .OrderBy(o => o.SortOrder)
                .Select(o => o.Text)
                .ToList();
        }

        var combat = Single(CombatSort);
        var family = Multi(FamilySort, out _);
        var relations = Single(RelationsSort);
        var justice = Single(JusticeSort);
        var anxiety = Multi(AnxietySort, out var otherAnxiety);
        var readiness = Single(ReadinessSort);
        var changes = Multi(ChangesSort, out var otherChange);

        var personal = new List<string>();
        var heard = new List<string>();
        for (var sort = PhenomenonFirstSort; sort <= PhenomenonLastSort; sort++)
        {
            if (!byOrder.TryGetValue(sort, out var question))
            {
                continue;
            }

            var mark = ResolveSingle(byOrder, answersByQuestion, sort);
            if (ContainsIgnoreCase(mark, "особисто"))
            {
                personal.Add(question.Text);
            }
            else if (ContainsIgnoreCase(mark, "побратими"))
            {
                heard.Add(question.Text);
            }
        }

        var readinessLevel = ReadinessScore(readiness);
        var needsAttention =
            readinessLevel >= 5 ||
            ContainsIgnoreCase(relations, "Ворожі") ||
            personal.Count >= 3 ||
            ContainsIgnoreCase(justice, "Ні");

        var rankName = attempt.AnonymousRank is AnonymousRank rank
            ? AnonymousRankNames.Display(rank)
            : "не вказано";

        var conclusion = BuildConclusion(
            rankName,
            combat,
            family,
            relations,
            justice,
            personal,
            heard,
            anxiety,
            otherAnxiety,
            readiness,
            changes,
            otherChange,
            needsAttention);

        return new AnonymousSurveyScoringResult
        {
            RankName = rankName,
            CombatExperience = combat,
            Family = family,
            Relations = relations,
            Justice = justice,
            PersonalProblems = personal,
            HeardProblems = heard,
            Anxiety = anxiety,
            Readiness = readiness,
            ReadinessLevel = readinessLevel,
            Changes = changes,
            OtherAnxiety = otherAnxiety,
            OtherChange = otherChange,
            NeedsAttention = needsAttention,
            Conclusion = conclusion
        };
    }

    public static string Pack(IEnumerable<Guid> ids, string? freeText)
    {
        var packed = string.Join(",", ids.Where(id => id != Guid.Empty).Distinct());
        if (!string.IsNullOrWhiteSpace(freeText))
        {
            packed += "|" + freeText.Trim();
        }

        return packed;
    }

    public static (List<Guid> Ids, string? Text) Unpack(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ([], null);
        }

        var split = value.Split('|', 2);
        var ids = split[0]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => Guid.TryParse(token, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();
        var text = split.Length > 1 && !string.IsNullOrWhiteSpace(split[1])
            ? split[1].Trim()
            : null;
        return (ids, text);
    }

    public static bool IsFreeTextOption(string text)
        => text.StartsWith("Інше", StringComparison.OrdinalIgnoreCase) ||
           text.StartsWith("Ваш варіант", StringComparison.OrdinalIgnoreCase);

    private static string ResolveSingle(
        IReadOnlyDictionary<int, TestQuestion> byOrder,
        IReadOnlyDictionary<Guid, TestAnswer> answers,
        int sort)
    {
        if (!byOrder.TryGetValue(sort, out var question))
        {
            return string.Empty;
        }

        if (!answers.TryGetValue(question.Id, out var answer) || answer.SelectedOptionId is null)
        {
            return string.Empty;
        }

        return question.Options.FirstOrDefault(o => o.Id == answer.SelectedOptionId.Value)?.Text
               ?? answer.SelectedOption?.Text
               ?? string.Empty;
    }

    private static int ReadinessScore(string readiness)
    {
        if (ContainsIgnoreCase(readiness, "Готовий повністю"))
        {
            return 1;
        }

        if (ContainsIgnoreCase(readiness, "сам проситись не буду"))
        {
            return 2;
        }

        if (ContainsIgnoreCase(readiness, "побратими"))
        {
            return 3;
        }

        if (ContainsIgnoreCase(readiness, "бракує"))
        {
            return 4;
        }

        if (ContainsIgnoreCase(readiness, "здоров"))
        {
            return 5;
        }

        if (ContainsIgnoreCase(readiness, "БЗВП"))
        {
            return 6;
        }

        if (ContainsIgnoreCase(readiness, "всі методи"))
        {
            return 7;
        }

        return 0;
    }

    private static string BuildConclusion(
        string rank,
        string combat,
        IReadOnlyList<string> family,
        string relations,
        string justice,
        IReadOnlyList<string> personal,
        IReadOnlyList<string> heard,
        IReadOnlyList<string> anxiety,
        string? otherAnxiety,
        string readiness,
        IReadOnlyList<string> changes,
        string? otherChange,
        bool needsAttention)
    {
        var parts = new List<string>
        {
            $"Анонімне опитування. Категорія: {rank}."
        };

        if (!string.IsNullOrWhiteSpace(combat))
        {
            parts.Add($"Бойовий досвід: {combat}.");
        }

        if (family.Count > 0)
        {
            parts.Add("Сімейні обставини: " + string.Join("; ", family) + ".");
        }

        if (!string.IsNullOrWhiteSpace(relations))
        {
            parts.Add($"Взаємовідносини в підрозділі: {relations}.");
        }

        if (!string.IsNullOrWhiteSpace(justice))
        {
            parts.Add($"Дотримання соціальної справедливості при відборі: {justice}.");
        }

        if (personal.Count > 0)
        {
            parts.Add("Особисто стикався з: " + string.Join("; ", personal.Select(Shorten)) + ".");
        }

        if (heard.Count > 0)
        {
            parts.Add("Від побратимів чув про: " + string.Join("; ", heard.Select(Shorten)) + ".");
        }

        var anxietyItems = anxiety.ToList();
        if (!string.IsNullOrWhiteSpace(otherAnxiety))
        {
            anxietyItems.Add(otherAnxiety);
        }

        if (anxietyItems.Count > 0)
        {
            parts.Add("Джерела тривоги: " + string.Join("; ", anxietyItems) + ".");
        }

        if (!string.IsNullOrWhiteSpace(readiness))
        {
            parts.Add($"Особиста готовність: {readiness}.");
        }

        var changeItems = changes.Where(c => !c.StartsWith("Ваш варіант", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(otherChange))
        {
            changeItems.Add(otherChange);
        }

        if (changeItems.Count > 0)
        {
            parts.Add("Першочергові зміни: " + string.Join("; ", changeItems) + ".");
        }

        parts.Add(needsAttention
            ? "Висновок: відповіді потребують уваги психолога (низька готовність, напруга в підрозділі або кілька особистих проблемних факторів)."
            : "Висновок: критичних маркерів за цією анкетою не виявлено; рекомендовано врахувати зазначені фактори при плануванні відряджень.");

        return string.Join(" ", parts);
    }

    private static string Shorten(string value)
        => value.Length <= 90 ? value : value[..87] + "…";

    private static bool ContainsIgnoreCase(string? source, string value)
        => !string.IsNullOrWhiteSpace(source) &&
           source.Contains(value, StringComparison.OrdinalIgnoreCase);
}
