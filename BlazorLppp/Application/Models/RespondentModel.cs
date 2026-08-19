using System.ComponentModel.DataAnnotations;

using BlazorLppp.Domain;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Models;

public class RespondentModel : IValidatableObject
{
    [StringLength(100, ErrorMessage = "Прізвище не може перевищувати 100 символів")]
    public string LastName { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Ім’я не може перевищувати 100 символів")]
    public string FirstName { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "По батькові не може перевищувати 100 символів")]
    public string MiddleName { get; set; } = string.Empty;

    [Range(0, 9999, ErrorMessage = "Оберіть підрозділ")]
    public int NumberUnit { get; set; }

    [Required(ErrorMessage = "Оберіть тест")]
    public Guid? TestDocumentId { get; set; }

    public bool IsAnonymous { get; set; }

    public AnonymousRank? AnonymousRank { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!TestDocumentId.HasValue || TestDocumentId.Value == Guid.Empty)
        {
            yield return new ValidationResult("Оберіть тест", [nameof(TestDocumentId)]);
        }

        if (IsAnonymous)
        {
            if (AnonymousRank is null)
            {
                yield return new ValidationResult(
                    "Оберіть категорію: солдат, сержант або офіцер",
                    [nameof(AnonymousRank)]);
            }

            yield break;
        }

        if (NumberUnit < 1)
        {
            yield return new ValidationResult("Оберіть підрозділ", [nameof(NumberUnit)]);
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

        if (trimmed.Length == 0)
        {
            yield return new ValidationResult(requiredMessage, [memberName]);
            yield break;
        }

        if (trimmed.Length < 2)
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
