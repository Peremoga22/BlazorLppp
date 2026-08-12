using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Services;

public sealed class Adaptivity200ScoringResult
{
    public int ReliabilityD { get; init; }

    public string ReliabilityLevelName { get; init; } = string.Empty;

    public bool IsResultUnreliable { get; init; }

    public int BehavioralRegulationPr { get; init; }

    public int CommunicativePotentialKp { get; init; }

    public int MoralNormativityMn { get; init; }

    public int MilitaryOrientationVps { get; init; }

    public int DeviantPropensityDap { get; init; }

    public int SuicidalRiskSr { get; init; }

    public int PersonalAdaptationPotentialOap { get; init; }

    public int StenOap { get; init; }

    public int StenPr { get; init; }

    public int StenKp { get; init; }

    public int StenMn { get; init; }

    public int StenVps { get; init; }

    public int StenDap { get; init; }

    /// <summary>
    /// Для СР = 0 у джерельній таблиці вказано і 9, і 10 стен — однозначно не визначається.
    /// </summary>
    public int? StenSr { get; init; }

    public string StenSrDisplay { get; init; } = string.Empty;

    public string Conclusion { get; init; } = string.Empty;
}

public static class Adaptivity200Scoring
{
    // Д: лише відповіді «Ні»
    private static readonly HashSet<int> ReliabilityNo =
    [
        1, 10, 19, 31, 51, 69, 78, 92, 101, 116, 128, 138, 148
    ];

    private static readonly HashSet<int> PrYes =
    [
        4, 6, 7, 8, 11, 12, 15, 16, 17, 18, 20, 21,
        28, 29, 30, 36, 37, 39, 40, 41, 47, 57, 60,
        63, 65, 67, 68, 70, 73, 80, 82, 83, 84, 86,
        89, 94, 95, 96, 98, 102, 103, 108, 109, 110,
        111, 112, 113, 115, 117, 118, 119, 120, 122,
        123, 124, 125, 127, 129, 131, 135, 136, 137,
        139, 143, 146, 149, 153, 154, 155, 156, 157,
        158, 161, 162
    ];

    private static readonly HashSet<int> PrNo =
    [
        2, 3, 5, 23, 25, 32, 38, 44, 45, 52, 53, 54,
        55, 58, 62, 66, 75, 87, 105, 132, 134, 140
    ];

    private static readonly HashSet<int> KpYes =
    [
        9, 24, 27, 43, 46, 61, 64, 81, 88, 90,
        99, 104, 106, 114, 121, 126, 133, 142,
        151, 152
    ];

    private static readonly HashSet<int> KpNo =
    [
        26, 34, 35, 48, 49, 74, 85, 107, 130,
        144, 147, 159
    ];

    private static readonly HashSet<int> MnYes =
    [
        14, 22, 33, 42, 50, 56, 59, 71, 72,
        77, 79, 91, 93, 141, 145, 150, 164, 165
    ];

    private static readonly HashSet<int> MnNo =
    [
        13, 76, 97, 100, 160, 163
    ];

    private static readonly HashSet<int> VpsYes =
    [
        166, 167, 168, 169, 170, 172, 173, 174,
        175, 176, 177, 179, 180, 181, 183, 184,
        185, 186, 187, 188, 190
    ];

    private static readonly HashSet<int> VpsNo =
    [
        171, 178, 182, 189
    ];

    private static readonly HashSet<int> DapYes =
    [
        6, 9, 14, 15, 22, 36, 39, 42, 47, 50,
        56, 59, 71, 72, 91, 93, 117, 127, 141,
        145, 151, 152, 164, 191, 192, 193, 194,
        195, 196, 197, 198, 199, 200
    ];

    private static readonly HashSet<int> DapNo =
    [
        13, 100, 163
    ];

    private static readonly HashSet<int> SrYes =
    [
        4, 8, 10, 28, 29, 39, 41, 47, 70,
        84, 115, 119, 124, 136, 137, 149,
        154, 155
    ];

    private static readonly HashSet<int> SrNo =
    [
        32, 105
    ];

    public static bool CanScore(TestDocument? document, IReadOnlyCollection<TestQuestion> questions)
        => Adaptivity200Document.IsAdaptivity200(document, questions) &&
           questions.Count >= 180 &&
           questions.All(q => q.Type is QuestionType.YesNo or QuestionType.SingleChoice);

    public static Adaptivity200ScoringResult Evaluate(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var yesNoBySortOrder = new Dictionary<int, bool?>();
        foreach (var question in questions)
        {
            answersByQuestion.TryGetValue(question.Id, out var answer);
            yesNoBySortOrder[question.SortOrder] = ResolveYesNo(answer);
        }

        var d = CountMatches(yesNoBySortOrder, ReliabilityNo, expectedYes: false);
        var pr = CountMatches(yesNoBySortOrder, PrYes, expectedYes: true)
                 + CountMatches(yesNoBySortOrder, PrNo, expectedYes: false);
        var kp = CountMatches(yesNoBySortOrder, KpYes, expectedYes: true)
                 + CountMatches(yesNoBySortOrder, KpNo, expectedYes: false);
        var mn = CountMatches(yesNoBySortOrder, MnYes, expectedYes: true)
                 + CountMatches(yesNoBySortOrder, MnNo, expectedYes: false);
        var vps = CountMatches(yesNoBySortOrder, VpsYes, expectedYes: true)
                  + CountMatches(yesNoBySortOrder, VpsNo, expectedYes: false);
        var dap = CountMatches(yesNoBySortOrder, DapYes, expectedYes: true)
                  + CountMatches(yesNoBySortOrder, DapNo, expectedYes: false);
        var sr = CountMatches(yesNoBySortOrder, SrYes, expectedYes: true)
                 + CountMatches(yesNoBySortOrder, SrNo, expectedYes: false);

        var oap = pr + kp + mn;
        var unreliable = d >= 10;
        var (stenSr, stenSrDisplay) = MapSrSten(sr);

        return new Adaptivity200ScoringResult
        {
            ReliabilityD = d,
            ReliabilityLevelName = MapReliability(d),
            IsResultUnreliable = unreliable,
            BehavioralRegulationPr = pr,
            CommunicativePotentialKp = kp,
            MoralNormativityMn = mn,
            MilitaryOrientationVps = vps,
            DeviantPropensityDap = dap,
            SuicidalRiskSr = sr,
            PersonalAdaptationPotentialOap = oap,
            StenOap = MapOapSten(oap),
            StenPr = MapPrSten(pr),
            StenKp = MapKpSten(kp),
            StenMn = MapMnSten(mn),
            StenVps = MapVpsSten(vps),
            StenDap = MapDapSten(dap),
            StenSr = stenSr,
            StenSrDisplay = stenSrDisplay,
            Conclusion = BuildConclusion(d, unreliable, oap, MapOapSten(oap), pr, kp, mn, vps, dap, sr)
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
            value.Equals("–", StringComparison.Ordinal) ||
            value.Equals("−", StringComparison.Ordinal))
        {
            return false;
        }

        return null;
    }

    private static string MapReliability(int d) => d switch
    {
        <= 5 => "висока достовірність",
        <= 9 => "достатня достовірність",
        _ => "результат недостовірний"
    };

    private static int MapOapSten(int oap) => oap switch
    {
        >= 87 => 1,
        >= 75 => 2,
        >= 63 => 3,
        >= 51 => 4,
        >= 40 => 5,
        >= 31 => 6,
        >= 25 => 7,
        >= 21 => 8,
        >= 18 => 9,
        _ => 10
    };

    private static int MapPrSten(int pr) => pr switch
    {
        >= 57 => 1,
        >= 46 => 2,
        >= 35 => 3,
        >= 27 => 4,
        >= 19 => 5,
        >= 13 => 6,
        >= 9 => 7,
        >= 6 => 8,
        5 => 9,
        _ => 10
    };

    private static int MapKpSten(int kp) => kp switch
    {
        >= 23 => 1,
        >= 20 => 2,
        >= 18 => 3,
        >= 15 => 4,
        >= 13 => 5,
        >= 11 => 6,
        >= 9 => 7,
        >= 7 => 8,
        6 => 9,
        _ => 10
    };

    private static int MapMnSten(int mn) => mn switch
    {
        >= 17 => 1,
        16 => 2,
        >= 14 => 3,
        >= 12 => 4,
        >= 10 => 5,
        >= 8 => 6,
        7 => 7,
        >= 5 => 8,
        4 => 9,
        _ => 10
    };

    private static int MapVpsSten(int vps) => vps switch
    {
        >= 18 => 1,
        >= 16 => 2,
        >= 14 => 3,
        >= 11 => 4,
        >= 8 => 5,
        >= 5 => 6,
        4 => 7,
        >= 2 => 8,
        1 => 9,
        _ => 10
    };

    private static int MapDapSten(int dap) => dap switch
    {
        >= 25 => 1,
        >= 21 => 2,
        >= 18 => 3,
        >= 15 => 4,
        >= 12 => 5,
        >= 10 => 6,
        >= 8 => 7,
        >= 6 => 8,
        >= 4 => 9,
        _ => 10
    };

    private static (int? Sten, string Display) MapSrSten(int sr) => sr switch
    {
        >= 15 => (1, "1"),
        >= 10 => (2, "2"),
        >= 7 => (3, "3"),
        >= 5 => (4, "4"),
        4 => (5, "5"),
        3 => (6, "6"),
        2 => (7, "7"),
        1 => (8, "8"),
        // У джерельній таблиці 0 балів наведено і для 9-го, і для 10-го стену.
        _ => (null, "9 або 10")
    };

    private static string BuildConclusion(
        int d,
        bool unreliable,
        int oap,
        int stenOap,
        int pr,
        int kp,
        int mn,
        int vps,
        int dap,
        int sr)
    {
        if (unreliable)
        {
            return
                $"За шкалою достовірності (Д) отримано {d} балів — результат недостовірний (Д ≥ 10). " +
                "Формування психологічного висновку за методикою «Адаптивність-200» припинено. " +
                "Рекомендовано повторне обстеження з акцентом на відвертість відповідей.";
        }

        var adaptationLevel = stenOap switch
        {
            >= 8 => "високий",
            >= 5 => "середній",
            _ => "знижений"
        };

        return
            $"За методикою «Адаптивність-200» (БОО): Д = {d} ({MapReliability(d)}); " +
            $"ПР = {pr}, КП = {kp}, МН = {mn}; ОАП = {oap} ({stenOap} стенів, рівень адаптаційних можливостей — {adaptationLevel}); " +
            $"ВПС = {vps}, ДАП = {dap}, СР = {sr}. " +
            "Чим вищий стен ОАП, тим вищий рівень адаптаційних можливостей особистості.";
    }
}
