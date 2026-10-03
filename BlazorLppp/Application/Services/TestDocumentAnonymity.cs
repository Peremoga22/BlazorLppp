using BlazorLppp.Domain.Entities;

namespace BlazorLppp.Application.Services;

/// <summary>
/// Чи проходиться тест анонімно: або адміністратор вручну позначив тест, або це «Анонімне опитування»
/// (воно анонімне завжди).
/// </summary>
public static class TestDocumentAnonymity
{
    public static bool IsAnonymous(TestDocument? document)
        => document is not null &&
           (document.IsAnonymous || AnonymousSurveyScoring.LooksLike(document));

    /// <summary>Анонімність вбудована в сам тест і не може бути вимкнена вручну.</summary>
    public static bool IsLockedAnonymous(TestDocument? document)
        => AnonymousSurveyScoring.LooksLike(document);
}
