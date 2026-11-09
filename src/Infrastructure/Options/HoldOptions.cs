namespace Infrastructure.Options;

public class HoldOptions
{
    public const string ConfigKey = "HOLD_DURATION_SECONDS";
    public const int DefaultDurationSeconds = 600;

    public int DurationSeconds { get; set; } = DefaultDurationSeconds;

    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);
}
