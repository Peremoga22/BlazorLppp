using BlazorLppp.Application.Models;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Services;

public static class ZbroyaDocumentTemplate
{
    private static readonly string[] ReactiveItems =
    [
        "Я спокійний",
        "Мені ніщо не загрожує",
        "Я перебуваю в напруженні",
        "Мене проймає жаль до себе",
        "Я почуваюся вільно",
        "Я переживаю стан одинокості",
        "Мене хвилюють можливі невдачі",
        "Я почуваю себе відпочившим",
        "Я схвильований",
        "Я відчуваю відчуття внутрішнього задоволення",
        "Я впевнений у собі",
        "Я нервуюся",
        "Я не знаходжу собі місця",
        "Я збуджений (роздратований)",
        "Я не відчуваю скутості, напруження",
        "Я задоволений",
        "Я стурбований",
        "Я надто збуджений і мені не по собі",
        "Мені радісно",
        "Мені приємно"
    ];

    public static bool IsKnownReactiveItem(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim();
        return ReactiveItems.Any(item =>
            normalized.Equals(item, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(item, StringComparison.OrdinalIgnoreCase));
    }

    public static ParsedTestDocument Create()
    {
        var document = new ParsedTestDocument
        {
            Title = "Тест ЗБРОЯ (готовність до служби зі зброєю)",
            Instruction =
                "Прочитайте уважно кожне з наведених нижче речень і оберіть відповідну цифру залежно від того, " +
                "ЯК ВИ ПОЧУВАЄТЕСЯ ЦІЄЇ МИТІ. Над запитаннями довго не замислюйтеся, тому що правильних чи неправильних відповідей немає. " +
                "1 — Ні, це не так; 2 — Мабуть так; 3 — Правильно; 4 — Абсолютно правильно."
        };

        for (var i = 0; i < ReactiveItems.Length; i++)
        {
            var question = new ParsedTestQuestion
            {
                SortOrder = i + 1,
                Text = ReactiveItems[i],
                Type = QuestionType.SingleChoice
            };
            AddScaleOptions(question);
            document.Questions.Add(question);
        }

        document.Questions.Add(CreateScale(21, "САМОПОЧУТТЯ"));
        document.Questions.Add(CreateScale(22, "АКТИВНІСТЬ"));
        document.Questions.Add(CreateScale(23, "НАСТРІЙ"));

        document.Questions.Add(new ParsedTestQuestion
        {
            SortOrder = 24,
            Text =
                "Чи згодні Ви із зазначеним висловом: «Я маю необхідні знання і практичні навички, вивчив функціональні обов’язки, " +
                "пройшов відповідний інструктаж, його вимоги мені зрозумілі, проблемних питань щодо організації несення служби не маю. " +
                "Мій стан здоров’я, настрій, самопочуття і активність, морально-психологічний стан дозволяють мені виконувати службові обов’язки із зброєю. " +
                "Проблемних питань, негативних чинників впливу на мій морально-психологічний стан не маю. Готовий нести службу зі зброєю»?",
            Type = QuestionType.YesNo,
            Options =
            [
                new ParsedTestOption { SortOrder = 1, Key = "Так", Text = "Так" },
                new ParsedTestOption { SortOrder = 2, Key = "Ні", Text = "Ні" }
            ]
        });

        return document;
    }

    private static ParsedTestQuestion CreateScale(int sortOrder, string text)
        => new()
        {
            SortOrder = sortOrder,
            Text = text,
            Type = QuestionType.Scale,
            ScaleMin = 0,
            ScaleMax = 10,
            Hint = "0% — 50 — 100%"
        };

    private static void AddScaleOptions(ParsedTestQuestion question)
    {
        question.Options.Add(new ParsedTestOption { SortOrder = 1, Key = "1", Text = "Ні, це не так" });
        question.Options.Add(new ParsedTestOption { SortOrder = 2, Key = "2", Text = "Мабуть так" });
        question.Options.Add(new ParsedTestOption { SortOrder = 3, Key = "3", Text = "Правильно" });
        question.Options.Add(new ParsedTestOption { SortOrder = 4, Key = "4", Text = "Абсолютно правильно" });
    }
}
