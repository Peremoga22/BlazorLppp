using System.ComponentModel.DataAnnotations;

using BlazorLppp.Domain;

namespace BlazorLppp.Application.Models;

public class RespondentModel : IValidatableObject
{
    [Required(ErrorMessage = "Вкажіть прізвище")]
    [StringLength(100, ErrorMessage = "Прізвище не може перевищувати 100 символів")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть ім’я")]
    [StringLength(100, ErrorMessage = "Ім’я не може перевищувати 100 символів")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть по батькові")]
    [StringLength(100, ErrorMessage = "По батькові не може перевищувати 100 символів")]
    public string MiddleName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть номер підрозділу")]
    [Range(UnitNumbers.Min, UnitNumbers.Max, ErrorMessage = "Оберіть підрозділ від 1 до 5")]
    public int NumberUnit { get; set; }

    [Required(ErrorMessage = "Оберіть тест")]
    public Guid? TestDocumentId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!TestDocumentId.HasValue || TestDocumentId.Value == Guid.Empty)
        {
            yield return new ValidationResult("Оберіть тест", [nameof(TestDocumentId)]);
        }

        foreach (var result in ValidateTrimmed(LastName, nameof(LastName), "Вкажіть прізвище", "Прізвище"))
        {
            yield return result;
        }

        foreach (var result in ValidateTrimmed(FirstName, nameof(FirstName), "Вкажіть ім’я", "Ім’я"))
        {
            yield return result;
        }

        foreach (var result in ValidateTrimmed(MiddleName, nameof(MiddleName), "Вкажіть по батькові", "По батькові"))
        {
            yield return result;
        }
    }

    private static IEnumerable<ValidationResult> ValidateTrimmed(
        string? value,
        string memberName,
        string requiredMessage,
        string fieldLabel)
    {
        var raw = value ?? string.Empty;
        var trimmed = raw.Trim();

        // Лише пробіли — Required цього не ловить
        if (raw.Length > 0 && trimmed.Length == 0)
        {
            yield return new ValidationResult(requiredMessage, [memberName]);
            yield break;
        }

        if (trimmed.Length > 0 && trimmed.Length < 2)
        {
            yield return new ValidationResult(
                $"{fieldLabel} має містити щонайменше 2 символи",
                [memberName]);
        }

        if (trimmed.Length > 100)
        {
            yield return new ValidationResult(
                $"{fieldLabel} не може перевищувати 100 символів",
                [memberName]);
        }
    }
}
