using Api.Dtos;
using FluentValidation;

namespace Api.Validators;

public class CreateClassRequestValidator : AbstractValidator<CreateClassRequest>
{
    public const int NameMaxLength = 200;

    public CreateClassRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("name is required")
            .MaximumLength(NameMaxLength).WithMessage($"name must be at most {NameMaxLength} characters")
            // PostgreSQL cannot store U+0000 in text; without this the insert fails in the database (a 500).
            // Null-safe because a missing "name" reaches this rule as null, and NotEmpty has already reported it.
            .Must(name => name is null || !name.Contains('\0')).WithMessage("name must not contain null characters");

        RuleFor(x => x.TotalSeats)
            .GreaterThan(0).WithMessage("totalSeats must be greater than 0");
    }
}
