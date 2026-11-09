namespace Api.Dtos;

public record CreateClassRequest(string Name, int TotalSeats);

public record ClassResponse(string Id, string Name, int TotalSeats);

public record ClassSummaryResponse(
    string Id,
    string Name,
    int TotalSeats,
    int HeldSeats,
    int ConfirmedSeats,
    int AvailableSeats);
