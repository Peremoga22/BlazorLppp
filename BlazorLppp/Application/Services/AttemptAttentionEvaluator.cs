using BlazorLppp.Domain.Entities;

namespace BlazorLppp.Application.Services;

public sealed class AttemptAttentionResult
{
    public bool NeedsAttention { get; init; }

    public bool IsUnreliable { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string? LevelName { get; init; }

    public IReadOnlyDictionary<string, double> ScaleRaw { get; init; }
        = new Dictionary<string, double>();

    public IReadOnlyDictionary<string, double> ScaleSten { get; init; }
        = new Dictionary<string, double>();
}

/// <summary>
/// Застосовує ключі/нормативи конкретної методики.
/// Не ставить діагнозів — лише позначає потребу додаткового перегляду.
/// </summary>
public static class AttemptAttentionEvaluator
{
    public static AttemptAttentionResult Evaluate(
        TestDocument? document,
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        if (document is null || questions.Count == 0)
        {
            return new AttemptAttentionResult();
        }

        if (Adaptivity200Scoring.CanScore(document, questions))
        {
            var scoring = Adaptivity200Scoring.Evaluate(questions, answersByQuestion);
            var needs = scoring.IsResultUnreliable ||
                        scoring.StenOap < 5 ||
                        (scoring.StenSr is not null && scoring.StenSr <= 4) ||
                        scoring.SuicidalRiskSr >= 12 ||
                        scoring.DeviantPropensityDap >= 18;

            var reason = scoring.IsResultUnreliable
                ? "Недостовірний результат (шкала Д) — потрібен повторний перегляд"
                : scoring.StenOap < 5
                    ? "Знижений рівень адаптаційних можливостей (ОАП) — потребує додаткової уваги"
                    : (scoring.StenSr is not null && scoring.StenSr <= 4) || scoring.SuicidalRiskSr >= 12
                        ? "Підвищений показник СР — потребує додаткової уваги"
                        : scoring.DeviantPropensityDap >= 18
                            ? "Підвищений показник ДАП — потребує додаткової уваги"
                            : string.Empty;

            var sten = new Dictionary<string, double>
            {
                ["ПР"] = scoring.StenPr,
                ["КП"] = scoring.StenKp,
                ["МН"] = scoring.StenMn,
                ["ВПС"] = scoring.StenVps,
                ["ДАП"] = scoring.StenDap,
                ["ОАП"] = scoring.StenOap
            };
            if (scoring.StenSr is not null)
            {
                sten["СР"] = scoring.StenSr.Value;
            }

            return new AttemptAttentionResult
            {
                NeedsAttention = needs,
                IsUnreliable = scoring.IsResultUnreliable,
                Reason = reason,
                LevelName = scoring.IsResultUnreliable
                    ? "Недостовірний"
                    : scoring.StenOap switch
                    {
                        >= 8 => "Високий",
                        >= 5 => "Середній",
                        _ => "Знижений"
                    },
                ScaleRaw = new Dictionary<string, double>
                {
                    ["Д"] = scoring.ReliabilityD,
                    ["ПР"] = scoring.BehavioralRegulationPr,
                    ["КП"] = scoring.CommunicativePotentialKp,
                    ["МН"] = scoring.MoralNormativityMn,
                    ["ВПС"] = scoring.MilitaryOrientationVps,
                    ["ДАП"] = scoring.DeviantPropensityDap,
                    ["СР"] = scoring.SuicidalRiskSr,
                    ["ОАП"] = scoring.PersonalAdaptationPotentialOap
                },
                ScaleSten = sten
            };
        }

        if (HorskaScoring.CanScore(document, questions))
        {
            var scoring = HorskaScoring.Evaluate(questions, answersByQuestion);
            var needs = scoring.TotalPoints > 45;
            return new AttemptAttentionResult
            {
                NeedsAttention = needs,
                Reason = needs
                    ? "Високий сумарний показник за методикою — потребує додаткової уваги"
                    : string.Empty,
                LevelName = scoring.RiskLevelName,
                ScaleRaw = new Dictionary<string, double>
                {
                    ["Тривожність"] = scoring.Anxiety.Points,
                    ["Фрустрація"] = scoring.Frustration.Points,
                    ["Агресія"] = scoring.Aggression.Points,
                    ["Ригідність"] = scoring.Rigidity.Points,
                    ["Сумарно"] = scoring.TotalPoints
                }
            };
        }

        if (SuicideRiskScoring.CanScore(document, questions))
        {
            var scoring = SuicideRiskScoring.Evaluate(questions, answersByQuestion);
            var needs = scoring.Score <= 2 || scoring.IsReliabilityLow;
            var reason = scoring.IsReliabilityLow
                ? "Низька достовірність відповідей — потребує додаткової уваги"
                : scoring.Score <= 2
                    ? $"Рівень прояву «{scoring.RiskLevelName}» — потребує додаткової уваги"
                    : string.Empty;

            return new AttemptAttentionResult
            {
                NeedsAttention = needs,
                IsUnreliable = scoring.IsReliabilityLow,
                Reason = reason,
                LevelName = scoring.RiskLevelName,
                ScaleRaw = new Dictionary<string, double>
                {
                    ["L"] = scoring.LieCoefficient,
                    ["Sr"] = scoring.RiskCoefficient,
                    ["Оцінка"] = scoring.Score
                }
            };
        }

        if (ZbroyaScoring.CanScore(document, questions))
        {
            var scoring = ZbroyaScoring.Evaluate(questions, answersByQuestion);
            var needs = scoring.ReactiveAnxiety >= 46 || scoring.ReadyForWeaponDuty == false;
            var reason = scoring.ReadyForWeaponDuty == false
                ? "Готовність до служби зі зброєю не підтверджена — потребує додаткової уваги"
                : scoring.ReactiveAnxiety >= 46
                    ? "Висока реактивна тривожність — потребує додаткової уваги"
                    : string.Empty;

            return new AttemptAttentionResult
            {
                NeedsAttention = needs,
                Reason = reason,
                LevelName = scoring.AnxietyLevelName,
                ScaleRaw = new Dictionary<string, double>
                {
                    ["РТ"] = scoring.ReactiveAnxiety,
                    ["САН"] = scoring.SanIndex
                }
            };
        }

        if (AssingerScoring.CanScore(document, questions))
        {
            var scoring = AssingerScoring.Evaluate(questions, answersByQuestion);
            var destructive = scoring.ScoreThreeCount >= 7 && scoring.ScoreOneCount < 7;
            var needs = scoring.TotalPoints >= 45 || destructive;
            var reason = scoring.TotalPoints >= 45
                ? "Надмірна агресивність за тестом Ассингера — потребує додаткової уваги"
                : destructive
                    ? "Руйнівний характер агресивних реакцій — потребує додаткової уваги"
                    : string.Empty;

            return new AttemptAttentionResult
            {
                NeedsAttention = needs,
                Reason = reason,
                LevelName = scoring.LevelName,
                ScaleRaw = new Dictionary<string, double>
                {
                    ["Сума"] = scoring.TotalPoints,
                    ["Відповідей 3"] = scoring.ScoreThreeCount,
                    ["Відповідей 1"] = scoring.ScoreOneCount
                }
            };
        }

        if (NpnaScoring.CanScore(document, questions))
        {
            var scoring = NpnaScoring.Evaluate(questions, answersByQuestion);
            if (scoring.IsUnscorable)
            {
                return new AttemptAttentionResult
                {
                    NeedsAttention = true,
                    Reason = "Немає відповідей «Так/Ні» — потрібен повторний прохід опитувальника «НПН-А»",
                    LevelName = "Не оброблено"
                };
            }

            var highClinical = scoring.Scales.Any(s => s.Key != "Д" && s.Sten >= 8);
            var needs = scoring.IsResultUnreliable || scoring.Npn.Sten >= 8 || highClinical;
            var reason = scoring.IsResultUnreliable
                ? "Недостовірний результат (шкала Д) — потрібен повторний перегляд"
                : scoring.Npn.Sten >= 8
                    ? "Високий показник нервово-психічної нестійкості — потребує додаткової уваги"
                    : highClinical
                        ? "Високі стени за шкалами акцентуації — потребує додаткової уваги"
                        : string.Empty;

            return new AttemptAttentionResult
            {
                NeedsAttention = needs,
                IsUnreliable = scoring.IsResultUnreliable,
                Reason = reason,
                LevelName = scoring.IsResultUnreliable
                    ? "Недостовірний"
                    : scoring.Npn.LevelName,
                ScaleRaw = scoring.Scales.ToDictionary(s => s.Key, s => (double)s.Raw),
                ScaleSten = scoring.Scales.ToDictionary(s => s.Key, s => (double)s.Sten)
            };
        }

        if (SzchScoring.CanScore(document, questions))
        {
            var scoring = SzchScoring.Evaluate(questions, answersByQuestion);
            var needs = !scoring.IsScorable || scoring.Score >= 15;
            var reason = !scoring.IsScorable
                ? "Недостовірний результат (шкала щирості СЗЧ-4) — потрібен повторний перегляд"
                : scoring.Score > 25
                    ? "Висока ймовірність самовільного залишення частини — потребує додаткової уваги"
                    : scoring.Score >= 15
                        ? "За СЗЧ-4 потрібна спеціальна індивідуальна робота"
                        : string.Empty;

            return new AttemptAttentionResult
            {
                NeedsAttention = needs,
                IsUnreliable = !scoring.IsScorable,
                Reason = reason,
                LevelName = scoring.LevelName,
                ScaleRaw = new Dictionary<string, double>
                {
                    ["Щирість"] = scoring.SincerityMatches,
                    ["СЗЧ"] = scoring.Score
                }
            };
        }

        if (AnonymousSurveyScoring.CanScore(document, questions))
        {
            return new AttemptAttentionResult();
        }

        return new AttemptAttentionResult();
    }
}
