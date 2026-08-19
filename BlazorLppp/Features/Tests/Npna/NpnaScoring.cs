using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Services;

public sealed class NpnaScaleScore
{
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Raw { get; init; }

    public int Sten { get; init; }

    public string LevelName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}

public sealed class NpnaScoringResult
{
    public NpnaScaleScore ReliabilityD { get; init; } = new();

    public NpnaScaleScore Npn { get; init; } = new();

    public NpnaScaleScore Hysteria { get; init; } = new();

    public NpnaScaleScore Psychasthenia { get; init; } = new();

    public NpnaScaleScore Psychopathy { get; init; } = new();

    public NpnaScaleScore Paranoia { get; init; } = new();

    public NpnaScaleScore Schizophrenia { get; init; } = new();

    public bool IsResultUnreliable { get; init; }

    public bool IsUnscorable { get; init; }

    public int AnsweredCount { get; init; }

    public string Conclusion { get; init; } = string.Empty;

    public IReadOnlyList<NpnaScaleScore> Scales =>
    [
        ReliabilityD,
        Npn,
        Hysteria,
        Psychasthenia,
        Psychopathy,
        Paranoia,
        Schizophrenia
    ];
}

public static class NpnaScoring
{
    public const int ReliabilityRawThreshold = 8;

    // Шкала Д: збіг із ключем за відповіддю «Ні» (соціально бажані заперечення).
    private static readonly HashSet<int> ReliabilityNo =
    [
        1, 4, 6, 24, 25, 27, 47, 49, 50, 70, 72, 93, 112, 114, 137
    ];

    private static readonly HashSet<int> NpnYes =
    [
        3, 5, 23, 26, 48, 68, 89, 90, 91, 94, 111, 113, 115, 134, 135, 136, 138,
        155, 157, 158, 159, 160, 177, 178, 181, 199, 200, 202, 203, 204, 221, 222,
        223, 225, 226, 243, 244, 245, 246, 247, 248, 249, 265, 266, 267, 268, 269,
        270, 271
    ];

    private static readonly HashSet<int> NpnNo =
    [
        2, 28, 45, 46, 67, 69, 71, 92, 116, 133, 156, 179, 180, 182, 201, 224
    ];

    // 139 є і в «Так», і в «Ні» у бланку; за змістом твердження належить лише до ключа «Ні».
    private static readonly HashSet<int> HysteriaYes =
    [
        7, 8, 9, 10, 29, 31, 32, 51, 52, 53, 54, 73, 74, 75, 76, 95, 96, 97, 98,
        117, 118, 119, 120, 140, 141, 142, 161, 162, 163, 164, 183, 184, 185, 205,
        206, 207, 227, 229, 250, 251, 272, 273
    ];

    private static readonly HashSet<int> HysteriaNo = [30, 139, 228];

    private static readonly HashSet<int> PsychastheniaYes =
    [
        11, 12, 13, 33, 34, 55, 56, 57, 77, 78, 79, 99, 100, 101, 121, 122, 123,
        143, 144, 145, 165, 166, 167, 186, 187, 188, 189, 208, 209, 210, 211, 231,
        232, 233, 252, 253, 254, 255, 274, 275, 276
    ];

    private static readonly HashSet<int> PsychastheniaNo = [35, 230];

    private static readonly HashSet<int> PsychopathyYes =
    [
        14, 15, 17, 36, 37, 38, 39, 58, 59, 60, 61, 80, 81, 82, 83, 102, 103, 105,
        124, 125, 126, 127, 146, 147, 148, 168, 169, 170, 171, 190, 192, 212, 234,
        235, 256, 257, 258
    ];

    private static readonly HashSet<int> PsychopathyNo = [16, 104, 149, 191, 213, 214, 236];

    private static readonly HashSet<int> ParanoiaYes =
    [
        18, 19, 20, 40, 63, 85, 86, 107, 128, 129, 151, 172, 193, 215, 237, 238
    ];

    private static readonly HashSet<int> ParanoiaNo =
    [
        41, 42, 62, 64, 84, 106, 150, 173, 194, 195, 216, 217, 239, 259, 260, 261
    ];

    private static readonly HashSet<int> SchizophreniaYes =
    [
        21, 22, 43, 44, 65, 66, 87, 88, 108, 109, 130, 131, 132, 152, 153, 154,
        174, 175, 196, 197, 198, 218, 219, 220, 240, 241, 242, 262, 263, 264
    ];

    private static readonly (int MinRaw, int Sten)[] StenD =
    [
        (13, 10), (12, 9), (10, 8), (8, 7), (7, 6), (5, 5), (3, 4), (2, 3), (1, 2), (0, 1)
    ];

    private static readonly (int MinRaw, int Sten)[] StenNpn =
    [
        (36, 10), (33, 9), (29, 8), (25, 7), (22, 6), (18, 5), (15, 4), (11, 3), (7, 2), (0, 1)
    ];

    private static readonly (int MinRaw, int Sten)[] StenHysteria =
    [
        (27, 10), (23, 9), (19, 8), (16, 7), (12, 6), (9, 5), (5, 4), (2, 3), (1, 2), (0, 1)
    ];

    private static readonly (int MinRaw, int Sten)[] StenPsychasthenia =
    [
        (30, 10), (27, 9), (23, 8), (20, 7), (16, 6), (13, 5), (10, 4), (6, 3), (3, 2), (0, 1)
    ];

    private static readonly (int MinRaw, int Sten)[] StenPsychopathy =
    [
        (16, 10), (15, 9), (14, 8), (13, 7), (12, 6), (11, 5), (9, 4), (8, 3), (7, 2), (0, 1)
    ];

    private static readonly (int MinRaw, int Sten)[] StenParanoia =
    [
        (11, 10), (10, 9), (9, 8), (7, 7), (6, 6), (4, 5), (3, 4), (1, 3), (0, 2)
    ];

    private static readonly (int MinRaw, int Sten)[] StenSchizophrenia =
    [
        (10, 10), (9, 9), (8, 8), (7, 7), (6, 6), (5, 5), (4, 4), (3, 3), (2, 2), (0, 1)
    ];

    public static bool CanScore(TestDocument? document, IReadOnlyCollection<TestQuestion> questions)
    {
        if (questions.Count is < 250 or > 280)
        {
            return false;
        }

        return LooksLikeNpna(document, questions);
    }

    public static bool LooksLikeNpna(TestDocument? document, IEnumerable<TestQuestion>? questions = null)
    {
        if (document is not null &&
            (ContainsIgnoreCase(document.RelativePath, "нпн") ||
             ContainsIgnoreCase(document.RelativePath, "npn") ||
             ContainsIgnoreCase(document.OriginalFileName, "нпн") ||
             ContainsIgnoreCase(document.OriginalFileName, "npn") ||
             ContainsIgnoreCase(document.Title, "НПН") ||
             ContainsIgnoreCase(document.Title, "NPN") ||
             ContainsIgnoreCase(document.Title, "нервово-психічн") ||
             ContainsIgnoreCase(document.Instruction, "обстежуваним")))
        {
            return true;
        }

        return questions is not null &&
               questions.Any(q =>
                   q.SortOrder == 1 &&
                   ContainsIgnoreCase(q.Text, "негарні думки"));
    }

    public static NpnaScoringResult Evaluate(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var yesNoBySortOrder = new Dictionary<int, bool?>();
        foreach (var question in questions.OrderBy(q => q.SortOrder))
        {
            answersByQuestion.TryGetValue(question.Id, out var answer);
            yesNoBySortOrder[question.SortOrder] = ResolveYesNo(answer, question);
        }

        var answeredCount = yesNoBySortOrder.Count(pair => pair.Value.HasValue);
        var unscorable = answeredCount == 0;

        var reliability = BuildScale(
            "Д",
            "Достовірність",
            CountMatches(yesNoBySortOrder, [], ReliabilityNo),
            StenD,
            "Шкала достовірності оцінює ступінь об’єктивності відповідей. " +
            "8 і більше сирих балів свідчать про прагнення відповідати соціально бажаному типу особистості.");

        var npn = BuildScale(
            "НПН",
            "Нервово-психічна нестійкість",
            CountMatches(yesNoBySortOrder, NpnYes, NpnNo),
            StenNpn,
            "Інтегральна шкала: низький рівень поведінкової регуляції, порушення міжособистісних відносин, " +
            "недостатня соціальна зрілість, знижені адаптаційні можливості. Подальші шкали розкривають структуру НПН.");

        var hysteria = BuildScale(
            "І",
            "Істерія",
            CountMatches(yesNoBySortOrder, HysteriaYes, HysteriaNo),
            StenHysteria,
            "Позерство, егоцентризм, демонстративність і театральність поведінки, бажання бути в центрі уваги, " +
            "поверхневе ставлення до завдань, орієнтація професійної діяльності на зовнішній ефект.");

        var psychasthenia = BuildScale(
            "Пс",
            "Психастенія",
            CountMatches(yesNoBySortOrder, PsychastheniaYes, PsychastheniaNo),
            StenPsychasthenia,
            "Висока тривожність, недовірливість, нерішучість, невпевненість у собі, підвищена вразливість, " +
            "схильність до сумнівів, складність ухвалення рішень, уникнення складних завдань.");

        var psychopathy = BuildScale(
            "Пп",
            "Психопатія",
            CountMatches(yesNoBySortOrder, PsychopathyYes, PsychopathyNo),
            StenPsychopathy,
            "Підвищені збудливість, агресивність, низький самоконтроль, прямолінійна критика, " +
            "непередбачуваність емоцій і вчинків, конфліктність у разі невизнання заслуг.");

        var paranoia = BuildScale(
            "Пя",
            "Параноя",
            CountMatches(yesNoBySortOrder, ParanoiaYes, ParanoiaNo),
            StenParanoia,
            "Підвищена підозрілість, настороженість, схильність приписувати оточенню ворожі наміри, " +
            "ригідність установок, образливість, звинувачення інших у власних невдачах.");

        var schizophrenia = BuildScale(
            "Ш",
            "Шизофренія",
            CountMatches(yesNoBySortOrder, SchizophreniaYes, []),
            StenSchizophrenia,
            "Схильність до теоретичних побудов і несподіваних висновків, емоційна холодність або " +
            "підвищена вразливість, відчуженість, замкнутість, утруднення в спілкуванні.");

        var unreliable = !unscorable && reliability.Raw >= ReliabilityRawThreshold;
        var conclusion = BuildConclusion(
            unscorable,
            answeredCount,
            unreliable,
            reliability,
            npn,
            hysteria,
            psychasthenia,
            psychopathy,
            paranoia,
            schizophrenia);

        return new NpnaScoringResult
        {
            ReliabilityD = reliability,
            Npn = npn,
            Hysteria = hysteria,
            Psychasthenia = psychasthenia,
            Psychopathy = psychopathy,
            Paranoia = paranoia,
            Schizophrenia = schizophrenia,
            IsResultUnreliable = unreliable,
            IsUnscorable = unscorable,
            AnsweredCount = answeredCount,
            Conclusion = conclusion
        };
    }

    internal static bool? ResolveYesNo(TestAnswer? answer, TestQuestion? question = null)
    {
        if (answer is null)
        {
            return null;
        }

        var option = answer.SelectedOption;
        if (option is null && question is not null && answer.SelectedOptionId.HasValue)
        {
            option = question.Options.FirstOrDefault(o => o.Id == answer.SelectedOptionId.Value);
        }

        if (option is null)
        {
            return null;
        }

        return ParseYesNoToken(option.Key) ?? ParseYesNoToken(option.Text);
    }

    private static bool? ParseYesNoToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var token = value.Trim();
        if (token.Equals("Так", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("+", StringComparison.Ordinal))
        {
            return true;
        }

        if (token.Equals("Ні", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("No", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("-", StringComparison.Ordinal) ||
            token.Equals("–", StringComparison.Ordinal) ||
            token.Equals("−", StringComparison.Ordinal))
        {
            return false;
        }

        return null;
    }

    internal static int ToSten(int raw, (int MinRaw, int Sten)[] table)
    {
        foreach (var (minRaw, sten) in table)
        {
            if (raw >= minRaw)
            {
                return sten;
            }
        }

        return 1;
    }

    internal static string MapStenLevel(int sten)
        => sten switch
        {
            >= 8 => "висока вираженість ознак",
            >= 4 => "середня (допустима) вираженість ознак",
            _ => "практична відсутність ознак"
        };

    private static NpnaScaleScore BuildScale(
        string key,
        string name,
        int raw,
        (int MinRaw, int Sten)[] table,
        string description)
    {
        var sten = ToSten(raw, table);
        return new NpnaScaleScore
        {
            Key = key,
            Name = name,
            Raw = raw,
            Sten = sten,
            LevelName = MapStenLevel(sten),
            Description = description
        };
    }

    private static int CountMatches(
        IReadOnlyDictionary<int, bool?> answers,
        IReadOnlyCollection<int> yesKeys,
        IReadOnlyCollection<int> noKeys)
    {
        var count = 0;
        foreach (var number in yesKeys)
        {
            if (answers.TryGetValue(number, out var value) && value == true)
            {
                count++;
            }
        }

        foreach (var number in noKeys)
        {
            if (answers.TryGetValue(number, out var value) && value == false)
            {
                count++;
            }
        }

        return count;
    }

    private static string BuildConclusion(
        bool unscorable,
        int answeredCount,
        bool unreliable,
        NpnaScaleScore reliability,
        NpnaScaleScore npn,
        params NpnaScaleScore[] clinical)
    {
        if (unscorable)
        {
            return
                "Обробка за ключами «НПН-А» не виконана: у спробі немає відповідей «Так/Ні». " +
                "Опитувальник передбачає лише відповіді «Так» або «Ні»; числова шкала для цієї методики не застосовується. " +
                "Рекомендовано пройти тест повторно після оновлення бланка.";
        }

        if (unreliable)
        {
            return
                $"Оброблено {answeredCount} відповідей «Так/Ні». " +
                $"За шкалою достовірності (Д) отримано {reliability.Raw} сирих балів, що відповідає {reliability.Sten} стенам. " +
                "За методикою 8 і більше сирих балів Д означають недостовірність через соціально бажані відповіді. " +
                "Інші шкали не інтерпретуються. Рекомендовано повторне обстеження та індивідуальну бесіду з психологом.";
        }

        var elevated = clinical
            .Where(scale => scale.Sten >= 8)
            .Select(scale => $"{scale.Name} ({scale.Key}): {scale.Raw} сирих / {scale.Sten} стенів — {scale.LevelName}. {scale.Description}")
            .ToList();

        var npnPart = npn.Sten >= 8
            ? $"Інтегральна шкала НПН: {npn.Raw} сирих балів → {npn.Sten} стенів — значна вираженість нервово-психічної нестійкості. {npn.Description} "
            : npn.Sten >= 4
                ? $"Інтегральна шкала НПН: {npn.Raw} сирих балів → {npn.Sten} стенів — середня (допустима) вираженість ознак. "
                : $"Інтегральна шкала НПН: {npn.Raw} сирих балів → {npn.Sten} стенів — ознаки нервово-психічної нестійкості практично відсутні. ";

        var structure = elevated.Count > 0
            ? "Високі стени (8–10) за шкалами акцентуації: " + string.Join(" ", elevated) +
              " Рекомендовано індивідуальну бесіду з психологом та динамічний контроль стану."
            : "Жодна зі шкал акцентуації (І, Пс, Пп, Пя, Ш) не сягає 8 стенів, тож високої акцентуації за цією методикою не виявлено. " +
              "Рекомендовано звичайний психологічний супровід.";

        return
            $"Оброблено {answeredCount} відповідей «Так/Ні» за ключами «НПН-А». " +
            $"Шкала достовірності Д = {reliability.Raw} сирих балів ({reliability.Sten} стенів) — нижче порогу 8, дані можна інтерпретувати. " +
            npnPart +
            structure;
    }

    private static bool ContainsIgnoreCase(string? source, string value)
        => !string.IsNullOrWhiteSpace(source) &&
           source.Contains(value, StringComparison.OrdinalIgnoreCase);
}
