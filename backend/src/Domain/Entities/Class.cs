namespace Domain.Entities;

public class Class
{
    public string Id { get; set; } = $"c_{Guid.NewGuid():N}";
    public string Name { get; set; } = string.Empty;
    public int TotalSeats { get; set; }

    /// <summary>Seats currently held (unexpired HELD holds). Mutated only via atomic SQL UPDATEs.</summary>
    public int HeldSeats { get; set; }

    /// <summary>Seats confirmed. Mutated only via atomic SQL UPDATEs.</summary>
    public int ConfirmedSeats { get; set; }
}
