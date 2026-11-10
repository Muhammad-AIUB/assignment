using Api.Dtos;
using Api.Services;

namespace UnitTests;

/// <summary>
/// Hand-written test doubles for the service layer, used to test the controllers on their own: what they hand to the
/// service, and how they turn each service outcome into an HTTP result. Each records its calls so a test can also
/// prove the service was NOT reached. (The services themselves are SQL against Postgres and are covered by the
/// Testcontainers integration tests.)
/// </summary>
internal sealed class FakeHoldService : IHoldService
{
    public sealed record Call(string Method, string[] Args, CancellationToken Token);

    public List<Call> Calls { get; } = [];

    public CreateHoldResult CreateResult { get; set; } = new(CreateHoldOutcome.Created);
    public HoldStatusResponse? GetResult { get; set; }
    public HoldActionOutcome ConfirmResult { get; set; } = HoldActionOutcome.Succeeded;
    public HoldActionOutcome CancelResult { get; set; } = HoldActionOutcome.Succeeded;
    public IReadOnlyList<MyHoldResponse> ListResult { get; set; } = [];

    public Task<CreateHoldResult> CreateAsync(string classId, string studentId, CancellationToken ct)
    {
        Calls.Add(new(nameof(CreateAsync), [classId, studentId], ct));
        return Task.FromResult(CreateResult);
    }

    public Task<HoldStatusResponse?> GetAsync(string holdId, CancellationToken ct)
    {
        Calls.Add(new(nameof(GetAsync), [holdId], ct));
        return Task.FromResult(GetResult);
    }

    public Task<HoldActionOutcome> ConfirmAsync(string holdId, string studentId, CancellationToken ct)
    {
        Calls.Add(new(nameof(ConfirmAsync), [holdId, studentId], ct));
        return Task.FromResult(ConfirmResult);
    }

    public Task<HoldActionOutcome> CancelAsync(string holdId, string studentId, CancellationToken ct)
    {
        Calls.Add(new(nameof(CancelAsync), [holdId, studentId], ct));
        return Task.FromResult(CancelResult);
    }

    public Task<IReadOnlyList<MyHoldResponse>> ListForStudentAsync(string studentId, CancellationToken ct)
    {
        Calls.Add(new(nameof(ListForStudentAsync), [studentId], ct));
        return Task.FromResult(ListResult);
    }
}

internal sealed class FakeClassService : IClassService
{
    public sealed record Call(string Method, object Arg, CancellationToken Token);

    public List<Call> Calls { get; } = [];

    public ClassResponse CreateResult { get; set; } = new("c_1", "Class", 10);
    public IReadOnlyList<ClassSummaryResponse> ListResult { get; set; } = [];
    public ClassSummaryResponse? GetResult { get; set; }

    public Task<ClassResponse> CreateAsync(CreateClassRequest request, CancellationToken ct)
    {
        Calls.Add(new(nameof(CreateAsync), request, ct));
        return Task.FromResult(CreateResult);
    }

    public Task<IReadOnlyList<ClassSummaryResponse>> ListAsync(CancellationToken ct)
    {
        Calls.Add(new(nameof(ListAsync), "", ct));
        return Task.FromResult(ListResult);
    }

    public Task<ClassSummaryResponse?> GetAsync(string classId, CancellationToken ct)
    {
        Calls.Add(new(nameof(GetAsync), classId, ct));
        return Task.FromResult(GetResult);
    }
}
