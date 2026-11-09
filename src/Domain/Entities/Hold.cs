using Domain.Enums;

namespace Domain.Entities;

public class Hold
{
    public string Id { get; set; } = $"h_{Guid.NewGuid():N}";
    public string ClassId { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public HoldStatus Status { get; set; } = HoldStatus.Held;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}
