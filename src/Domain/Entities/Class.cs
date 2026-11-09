namespace Domain.Entities;

public class Class
{
    public string Id { get; set; } = $"c_{Guid.NewGuid():N}";
    public string Name { get; set; } = string.Empty;
    public int TotalSeats { get; set; }
}
