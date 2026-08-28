using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Domain;

public static class AnswerOptionStyles
{
    public static readonly IReadOnlyList<AnswerOptionStyle> VisualStyles =
    [
        AnswerOptionStyle.Classic,
        AnswerOptionStyle.Pills,
        AnswerOptionStyle.Dots,
        AnswerOptionStyle.Bordered,
        AnswerOptionStyle.Track,
        AnswerOptionStyle.Compact,
        AnswerOptionStyle.Progress,
        AnswerOptionStyle.Steps
    ];

    public static string DisplayName(AnswerOptionStyle style) => style switch
    {
        AnswerOptionStyle.Classic => "Classic",
        AnswerOptionStyle.Pills => "Pills",
        AnswerOptionStyle.Dots => "Dots",
        AnswerOptionStyle.Bordered => "Bordered",
        AnswerOptionStyle.Track => "Track",
        AnswerOptionStyle.Compact => "Compact",
        AnswerOptionStyle.Progress => "Progress",
        AnswerOptionStyle.Steps => "Steps",
        _ => "Список"
    };

    public static string Description(AnswerOptionStyle style) => style switch
    {
        AnswerOptionStyle.Classic => "Нумеровані квадрати зі стрілками",
        AnswerOptionStyle.Pills => "Округлі пігулки, активна — градієнт",
        AnswerOptionStyle.Dots => "Крапки з повзунком позиції",
        AnswerOptionStyle.Bordered => "Квадрати з рамкою активного варіанту",
        AnswerOptionStyle.Track => "Цифри над доріжкою",
        AnswerOptionStyle.Compact => "Поточний / усього зі стрілкою",
        AnswerOptionStyle.Progress => "Смуга прогресу і лічильник",
        AnswerOptionStyle.Steps => "Кроки, з’єднані лінією",
        _ => "Звичайний список варіантів"
    };

    public static int PreviewSelectedIndex(AnswerOptionStyle style) => style switch
    {
        AnswerOptionStyle.Classic => 2,
        AnswerOptionStyle.Pills => 3,
        AnswerOptionStyle.Dots => 2,
        AnswerOptionStyle.Bordered => 4,
        AnswerOptionStyle.Track => 1,
        AnswerOptionStyle.Compact => 0,
        AnswerOptionStyle.Progress => 2,
        AnswerOptionStyle.Steps => 2,
        _ => 0
    };
}
