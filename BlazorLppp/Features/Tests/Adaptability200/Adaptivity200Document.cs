using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Services;

public static class Adaptivity200Document
{
    public static bool IsAdaptivity200(TestDocument? document, IReadOnlyCollection<TestQuestion>? questions = null)
    {
        if (document is not null)
        {
            if (ContainsIgnoreCase(document.RelativePath, "адаптивн") ||
                ContainsIgnoreCase(document.RelativePath, "adaptiv") ||
                ContainsIgnoreCase(document.OriginalFileName, "адаптивн") ||
                ContainsIgnoreCase(document.OriginalFileName, "adaptiv") ||
                ContainsIgnoreCase(document.Title, "адаптивн") ||
                ContainsIgnoreCase(document.Title, "БОО"))
            {
                return true;
            }
        }

        if (document is null || questions is null)
        {
            return false;
        }

        // Фолбек: типовий бланк має близько 200 відповідей «Так/Ні».
        return questions.Count is >= 180 and <= 220 &&
               questions.All(q => q.Type is QuestionType.YesNo or QuestionType.SingleChoice) &&
               (ContainsIgnoreCase(document.Title, "адаптив") ||
                ContainsIgnoreCase(document.Title, "200") ||
                ContainsIgnoreCase(document.OriginalFileName, "200"));
    }

    private static bool ContainsIgnoreCase(string? value, string fragment)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
