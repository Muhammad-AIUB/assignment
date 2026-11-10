using Api.Dtos;

namespace Api.Services;

public interface IClassService
{
    Task<ClassResponse> CreateAsync(CreateClassRequest request, CancellationToken ct);
    Task<IReadOnlyList<ClassSummaryResponse>> ListAsync(CancellationToken ct);
    Task<ClassSummaryResponse?> GetAsync(string classId, CancellationToken ct);
}
