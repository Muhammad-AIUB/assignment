namespace Api.Dtos;

public record HoldResponse(string HoldId, string ClassId, string StudentId, string Status, DateTime ExpiresAt);

/// <summary>GET /holds/{holdId}</summary>
public record HoldStatusResponse(string HoldId, string ClassId, string Status, DateTime ExpiresAt);

/// <summary>POST /holds/{holdId}/confirm</summary>
public record ConfirmHoldResponse(string HoldId, string Status);

/// <summary>GET /me/holds item</summary>
public record MyHoldResponse(string HoldId, string ClassId, string ClassName, string Status, DateTime ExpiresAt);
