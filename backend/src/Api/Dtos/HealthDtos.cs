namespace Api.Dtos;

/// <summary>GET /health (200)</summary>
public record HealthResponse(string Status, string Database);
