namespace BlazorLppp.Application.Services;

/// <summary>
/// Ліміт вибору у питаннях з кількома відповідями. У БД не зберігається — виводиться з тексту питання.
/// </summary>
public static class MultiChoiceLimit
{
    public static int? FromText(string? questionText)
    {
        if (string.IsNullOrWhiteSpace(questionText))
        {
            return null;
        }

        if (questionText.Contains("до 3", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        // Опитувальник МПС, п. 3.1: «обери два найбільш значущі для тебе твердження».
        if (questionText.Contains("обери два", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return null;
    }
}
