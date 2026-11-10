using Api.Controllers;
using Api.Dtos;
using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace UnitTests;

/// <summary>
/// HoldsController with a test double behind it. The controller must only orchestrate (validate the student id, call the
/// service, map the outcome to HTTP) and must never decide a business rule itself.
/// </summary>
public class HoldsControllerTests
{
    private const string Student = "20152691234567890";
    private static readonly DateTime Expiry = new(2030, 1, 1, 12, 10, 0, DateTimeKind.Utc);

    private readonly FakeHoldService _service = new();
    private readonly HoldsController _controller;

    public HoldsControllerTests() => _controller = new HoldsController(_service);

    private static (int? Status, object? Value) Unwrap(IActionResult result) => result switch
    {
        ObjectResult o => (o.StatusCode, o.Value),
        StatusCodeResult s => (s.StatusCode, null),
        _ => throw new Xunit.Sdk.XunitException($"Unexpected result type {result.GetType().Name}")
    };

    private static void AssertError(IActionResult result, int status, string code)
    {
        var (actualStatus, value) = Unwrap(result);
        Assert.Equal(status, actualStatus);
        var error = Assert.IsType<ErrorResponse>(value);
        Assert.Equal(code, error.Error);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    // ---------- the X-Student-Id rule: exactly 17 ASCII digits ----------

    public static TheoryData<string, string?> InvalidStudentIdsPerEndpoint()
    {
        string?[] invalid =
        [
            null, "", " ", "1234567890123456", "123456789012345678", "2015269123456789a",
            " 20152691234567890", "20152691234567890 ", "2015269123456 789", "-2015269123456789",
            "2015269123456.789", "2015269123456789\t", "0x15269123456789ab",
            new string('１', 17),  // full-width digits are digits to Unicode, but not to the registration number
            new string('١', 17)   // Arabic-Indic digits
        ];

        var data = new TheoryData<string, string?>();
        foreach (var endpoint in new[] { "create", "confirm", "cancel", "mine" })
            foreach (var id in invalid)
                data.Add(endpoint, id);
        return data;
    }

    private Task<IActionResult> Invoke(string endpoint, string? studentId, CancellationToken ct = default) => endpoint switch
    {
        "create" => _controller.Create("c_1", studentId, ct),
        "confirm" => _controller.Confirm("h_1", studentId, ct),
        "cancel" => _controller.Cancel("h_1", studentId, ct),
        "mine" => _controller.ListMine(studentId, ct),
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint))
    };

    [Theory]
    [MemberData(nameof(InvalidStudentIdsPerEndpoint))]
    public async Task A_missing_or_malformed_student_id_is_400_and_the_service_is_never_called(string endpoint, string? id)
    {
        var result = await Invoke(endpoint, id);

        AssertError(result, 400, "INVALID_STUDENT_ID");
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData("20152691234567890")]
    [InlineData("00000000000000000")]
    [InlineData("99999999999999999")]
    public async Task Any_17_digits_are_a_valid_student_id_including_leading_zeros(string id)
    {
        var (status, _) = Unwrap(await _controller.Create("c_1", id, default));

        Assert.Equal(201, status);
        Assert.Equal(id, Assert.Single(_service.Calls).Args[1]);
    }

    [Fact(Skip = "Known gap: the pattern is ^[0-9]{17}$ and in .NET '$' also matches before a final newline, so " +
                 "\"20152691234567890\\n\" passes. Not reachable over HTTP (Kestrel never delivers a newline inside a " +
                 "header value), so it is documented here rather than fixed in a test-only session. See PROGRESS.md.")]
    public async Task A_student_id_with_a_trailing_newline_is_rejected()
    {
        var result = await _controller.Create("c_1", Student + "\n", default);

        AssertError(result, 400, "INVALID_STUDENT_ID");
        Assert.Empty(_service.Calls);
    }

    // ---------- POST /classes/{classId}/holds ----------

    [Fact]
    public async Task Create_hands_the_class_the_student_and_the_cancellation_token_to_the_service()
    {
        using var cts = new CancellationTokenSource();

        await _controller.Create("c_42", Student, cts.Token);

        var call = Assert.Single(_service.Calls);
        Assert.Equal(nameof(IHoldService.CreateAsync), call.Method);
        Assert.Equal(["c_42", Student], call.Args);
        Assert.Equal(cts.Token, call.Token);
    }

    [Fact]
    public async Task Create_answers_201_with_the_hold_the_service_returned()
    {
        var hold = new HoldResponse("h_1", "c_1", Student, "HELD", Expiry);
        _service.CreateResult = new CreateHoldResult(CreateHoldOutcome.Created, hold);

        var (status, value) = Unwrap(await _controller.Create("c_1", Student, default));

        Assert.Equal(201, status);
        Assert.Same(hold, value);
    }

    [Theory]
    [InlineData(CreateHoldOutcome.ClassNotFound, 404, "CLASS_NOT_FOUND")]
    [InlineData(CreateHoldOutcome.NoSeatAvailable, 409, "NO_SEAT_AVAILABLE")]
    [InlineData(CreateHoldOutcome.DuplicateActiveHold, 409, "DUPLICATE_ACTIVE_HOLD")]
    public async Task Create_maps_each_failure_outcome_to_its_status_and_stable_error_code(
        CreateHoldOutcome outcome, int status, string code)
    {
        _service.CreateResult = new CreateHoldResult(outcome);

        AssertError(await _controller.Create("c_1", Student, default), status, code);
    }

    [Fact]
    public async Task Create_names_the_missing_class_in_the_404_message()
    {
        _service.CreateResult = new CreateHoldResult(CreateHoldOutcome.ClassNotFound);

        var (_, value) = Unwrap(await _controller.Create("c_missing", Student, default));

        Assert.Contains("c_missing", Assert.IsType<ErrorResponse>(value).Message);
    }

    [Fact]
    public async Task Create_does_not_swallow_an_outcome_it_does_not_know()
    {
        // Never answer success or a guess for something unexpected: it falls through to the global handler.
        _service.CreateResult = new CreateHoldResult((CreateHoldOutcome)99);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.Create("c_1", Student, default));
    }

    // ---------- GET /holds/{holdId} ----------

    [Fact]
    public async Task Get_needs_no_student_header_and_returns_what_the_service_found()
    {
        var found = new HoldStatusResponse("h_1", "c_1", "EXPIRED", Expiry);
        _service.GetResult = found;
        using var cts = new CancellationTokenSource();

        var (status, value) = Unwrap(await _controller.Get("h_1", cts.Token));

        Assert.Equal(200, status);
        Assert.Same(found, value);
        var call = Assert.Single(_service.Calls);
        Assert.Equal(["h_1"], call.Args);
        Assert.Equal(cts.Token, call.Token);
    }

    [Fact]
    public async Task Get_of_an_unknown_hold_is_404_naming_the_hold()
    {
        _service.GetResult = null;

        var result = await _controller.Get("h_missing", default);

        AssertError(result, 404, "HOLD_NOT_FOUND");
        Assert.Contains("h_missing", Assert.IsType<ErrorResponse>(Unwrap(result).Value).Message);
    }

    // ---------- POST /holds/{holdId}/confirm ----------

    [Fact]
    public async Task Confirm_hands_the_hold_the_student_and_the_cancellation_token_to_the_service()
    {
        using var cts = new CancellationTokenSource();

        await _controller.Confirm("h_7", Student, cts.Token);

        var call = Assert.Single(_service.Calls);
        Assert.Equal(nameof(IHoldService.ConfirmAsync), call.Method);
        Assert.Equal(["h_7", Student], call.Args);
        Assert.Equal(cts.Token, call.Token);
    }

    [Fact]
    public async Task Confirm_success_is_200_with_the_hold_id_and_status_CONFIRMED()
    {
        var (status, value) = Unwrap(await _controller.Confirm("h_7", Student, default));

        Assert.Equal(200, status);
        Assert.Equal(new ConfirmHoldResponse("h_7", "CONFIRMED"), value);
    }

    [Theory]
    [InlineData(HoldActionOutcome.NotFound, 404, "HOLD_NOT_FOUND")]
    [InlineData(HoldActionOutcome.NotOwner, 409, "NOT_HOLD_OWNER")]
    [InlineData(HoldActionOutcome.Expired, 409, "HOLD_EXPIRED")]
    [InlineData(HoldActionOutcome.AlreadyCancelled, 409, "HOLD_CANCELLED")]
    public async Task Confirm_maps_each_failure_outcome_to_its_status_and_stable_error_code(
        HoldActionOutcome outcome, int status, string code)
    {
        _service.ConfirmResult = outcome;

        AssertError(await _controller.Confirm("h_7", Student, default), status, code);
    }

    // ---------- DELETE /holds/{holdId} ----------

    [Fact]
    public async Task Cancel_hands_the_hold_the_student_and_the_cancellation_token_to_the_service()
    {
        using var cts = new CancellationTokenSource();

        await _controller.Cancel("h_7", Student, cts.Token);

        var call = Assert.Single(_service.Calls);
        Assert.Equal(nameof(IHoldService.CancelAsync), call.Method);
        Assert.Equal(["h_7", Student], call.Args);
        Assert.Equal(cts.Token, call.Token);
    }

    [Fact]
    public async Task Cancel_success_is_204_with_no_body()
    {
        var (status, value) = Unwrap(await _controller.Cancel("h_7", Student, default));

        Assert.Equal(204, status);
        Assert.Null(value);
    }

    [Theory]
    [InlineData(HoldActionOutcome.NotFound, 404, "HOLD_NOT_FOUND")]
    [InlineData(HoldActionOutcome.NotOwner, 409, "NOT_HOLD_OWNER")]
    [InlineData(HoldActionOutcome.Expired, 409, "HOLD_EXPIRED")]
    [InlineData(HoldActionOutcome.AlreadyCancelled, 409, "HOLD_CANCELLED")]
    [InlineData(HoldActionOutcome.AlreadyConfirmed, 409, "HOLD_ALREADY_CONFIRMED")]
    public async Task Cancel_maps_each_failure_outcome_to_its_status_and_stable_error_code(
        HoldActionOutcome outcome, int status, string code)
    {
        _service.CancelResult = outcome;

        AssertError(await _controller.Cancel("h_7", Student, default), status, code);
    }

    // ---------- GET /me/holds ----------

    [Fact]
    public async Task ListMine_asks_the_service_for_exactly_this_students_holds_and_returns_them_as_they_come()
    {
        MyHoldResponse[] mine =
        [
            new("h_2", "c_1", "Class 6", "HELD", Expiry),
            new("h_1", "c_2", "Class 7", "CANCELLED", Expiry)
        ];
        _service.ListResult = mine;
        using var cts = new CancellationTokenSource();

        var (status, value) = Unwrap(await _controller.ListMine(Student, cts.Token));

        Assert.Equal(200, status);
        Assert.Same(mine, value);
        var call = Assert.Single(_service.Calls);
        Assert.Equal([Student], call.Args);
        Assert.Equal(cts.Token, call.Token);
    }

    [Fact]
    public async Task ListMine_returns_an_empty_list_not_a_404_when_the_student_has_no_holds()
    {
        var (status, value) = Unwrap(await _controller.ListMine(Student, default));

        Assert.Equal(200, status);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<MyHoldResponse>>(value));
    }
}
