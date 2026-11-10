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

    /// <summary>
    /// Lazy expiry (rule 2): a stored HELD hold whose <c>ExpiresAt</c> has passed is EXPIRED, whether or not
    /// anything has swept the row yet. Every other status is reported as stored.
    /// <c>ExpiresAt &lt;= now</c> is expired, matching the SQL used for seat claims and sweeps.
    /// </summary>
    public static HoldStatus EffectiveStatus(HoldStatus stored, DateTime expiresAt, DateTime now) =>
        stored == HoldStatus.Held && expiresAt <= now ? HoldStatus.Expired : stored;
}
