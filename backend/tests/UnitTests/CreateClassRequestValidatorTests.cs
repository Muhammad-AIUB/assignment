using Api.Dtos;
using Api.Validators;
using FluentValidation.Results;

namespace UnitTests;

/// <summary>POST /classes input rules: a non-blank name of at most 200 characters, and at least one seat.</summary>
public class CreateClassRequestValidatorTests
{
    private readonly CreateClassRequestValidator _validator = new();

    private ValidationResult Validate(string? name, int totalSeats) =>
        _validator.Validate(new CreateClassRequest(name!, totalSeats));

    [Theory]
    [InlineData("Class 6 - Morning Shift", 120)]
    [InlineData("A", 1)]
    [InlineData("Class", int.MaxValue)]
    public void Accepts_a_named_class_with_at_least_one_seat(string name, int seats)
    {
        Assert.True(Validate(name, seats).IsValid);
    }

    [Fact]
    public void Accepts_a_name_of_exactly_the_maximum_length()
    {
        Assert.Equal(200, CreateClassRequestValidator.NameMaxLength);
        Assert.True(Validate(new string('x', 200), 10).IsValid);
    }

    [Fact]
    public void Rejects_a_name_one_character_over_the_maximum_and_says_why()
    {
        var result = Validate(new string('x', 201), 10);

        var error = Assert.Single(result.Errors);
        Assert.Equal("Name", error.PropertyName);
        Assert.Contains("200", error.ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t  ")]
    public void Rejects_a_missing_empty_or_blank_name(string? name)
    {
        var result = Validate(name, 10);

        var error = Assert.Single(result.Errors);
        Assert.Equal("Name", error.PropertyName);
        Assert.Contains("required", error.ErrorMessage);
    }

    [Fact]
    public void Rejects_a_name_containing_a_null_character_because_the_database_cannot_store_it()
    {
        var result = Validate("Class\0Six", 10);

        var error = Assert.Single(result.Errors);
        Assert.Equal("Name", error.PropertyName);
        Assert.Contains("null", error.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Rejects_zero_or_negative_seats(int seats)
    {
        var result = Validate("Class 6", seats);

        var error = Assert.Single(result.Errors);
        Assert.Equal("TotalSeats", error.PropertyName);
        Assert.Contains("greater than 0", error.ErrorMessage);
    }

    [Fact]
    public void Reports_every_problem_at_once_instead_of_one_at_a_time()
    {
        var result = Validate("", 0);

        Assert.Equal(["Name", "TotalSeats"], result.Errors.Select(e => e.PropertyName).Order().ToArray());
    }
}
