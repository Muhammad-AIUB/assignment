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
            .MaximumLength(NameMaxLength).WithMessage($"name must be at most {NameMaxLength} characters");

        RuleFor(x => x.TotalSeats)
            .GreaterThan(0).WithMessage("totalSeats must be greater than 0");
    }
}
