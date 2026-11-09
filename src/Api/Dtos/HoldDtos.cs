namespace Api.Dtos;

public record HoldResponse(string HoldId, string ClassId, string StudentId, string Status, DateTime ExpiresAt);
